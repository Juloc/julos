using System.Diagnostics;
using System.Text;

using JulOS.Application.Catalog;
using JulOS.Application.Secrets;
using JulOS.Domain.Catalog;

namespace JulOS.Infrastructure.Catalog;

/// <summary>
/// Reads a catalog from a Git repository by materializing one commit.
/// </summary>
/// <remarks>
/// <para>
/// A repository is a moving name plus an immutable history, so the adapter resolves the
/// configured branch or tag once and reports the commit as the source identity. That is what
/// makes a refresh reproducible: the same commit always produces the same catalog, whatever
/// the branch points at later.
/// </para>
/// <para>
/// Git itself does the fetching, invoked as a fixed argument vector with no shell between.
/// Nothing an administrator configures becomes an option — the location is one positional
/// argument after <c>--</c>, and the reference is passed as a value, so a location beginning
/// with a hyphen cannot turn into a flag. Submodules are never fetched: a submodule points at
/// another repository, and a catalog that pulls one in is reaching outside the source an
/// administrator configured.
/// </para>
/// <para>
/// The credential travels in the environment rather than on the command line, because a
/// process list is readable by anything on the host. Only HTTPS remotes are accepted: the
/// other transports Git speaks authenticate with a private key, and supporting them would
/// mean writing that key to disk for the duration of a refresh.
/// </para>
/// </remarks>
internal sealed class GitCatalogSourceReader : ICatalogSourceReader
{
    /// <summary>How long one clone may take before it is abandoned.</summary>
    private static readonly TimeSpan Deadline = TimeSpan.FromMinutes(5);

    private readonly string workingRoot;

    /// <summary>Creates a reader that materializes working copies below one directory.</summary>
    /// <param name="workingRoot">Directory the adapter owns for temporary working copies.</param>
    public GitCatalogSourceReader(string workingRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workingRoot);
        this.workingRoot = workingRoot;
    }

    public CatalogSourceKind Kind => CatalogSourceKind.Git;

    public async Task<CatalogSourceSnapshot> OpenAsync(
        CatalogSourceLocation location,
        SecretLease? credential,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(location);

        var (remote, reference) = Parse(location.Location);
        var directory = Path.Combine(this.workingRoot, Guid.CreateVersion7().ToString("N"));
        _ = Directory.CreateDirectory(directory);

        try
        {
            await RunAsync(
                CloneArguments(remote, reference, directory),
                credential,
                cancellationToken).ConfigureAwait(false);

            var commit = (await RunAsync(
                ["-C", directory, "rev-parse", "HEAD"],
                credential: null,
                cancellationToken).ConfigureAwait(false)).Trim();

            return new DirectoryCatalogSnapshot(
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory)),
                $"git:{commit}",
                deleteOnDispose: true);
        }
        catch
        {
            Delete(directory);
            throw;
        }
    }

    /// <summary>Builds the clone invocation.</summary>
    /// <remarks>
    /// A shallow single-branch clone fetches exactly the one state the refresh needs. History
    /// is not part of a catalog, and fetching it would make every refresh proportional to how
    /// long the repository has existed.
    /// </remarks>
    internal static string[] CloneArguments(string remote, string? reference, string directory)
    {
        var arguments = new List<string>
        {
            "clone",
            "--depth=1",
            "--single-branch",
            "--no-tags",
            "--recurse-submodules=no",
            "--quiet",
        };

        if (reference is not null)
        {
            arguments.Add("--branch");
            arguments.Add(reference);
        }

        // Everything after this is positional, so a location or a reference beginning with a
        // hyphen is a value rather than an option.
        arguments.Add("--");
        arguments.Add(remote);
        arguments.Add(directory);
        return [.. arguments];
    }

    /// <summary>Splits the configured location into a remote and an optional reference.</summary>
    internal static (string Remote, string? Reference) Parse(string location)
    {
        var separator = location.IndexOf('#', StringComparison.Ordinal);
        var remote = separator < 0 ? location : location[..separator];
        var reference = separator < 0 || separator == location.Length - 1
            ? null
            : location[(separator + 1)..];

        if (!Uri.TryCreate(remote, UriKind.Absolute, out var parsed) || parsed.Scheme != Uri.UriSchemeHttps)
        {
            throw new CatalogRefreshException(
                CatalogRefreshException.SourceUnavailable,
                "A Git catalog source is an https:// remote, optionally followed by '#branch-or-tag'.");
        }

        if (reference is not null && !IsPlainReference(reference))
        {
            throw new CatalogRefreshException(
                CatalogRefreshException.SourceUnavailable,
                "A Git reference is letters, digits, dot, slash, underscore and hyphen.");
        }

        return (remote, reference);
    }

    private static bool IsPlainReference(string reference)
    {
        if (reference.Length == 0 || reference[0] == '-' || reference.Contains("..", StringComparison.Ordinal))
        {
            return false;
        }

        foreach (var character in reference)
        {
            var allowed = char.IsAsciiLetterOrDigit(character) || character is '.' or '/' or '_' or '-';
            if (!allowed)
            {
                return false;
            }
        }

        return true;
    }

    private static async Task<string> RunAsync(
        string[] arguments,
        SecretLease? credential,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo("git")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        // Git must never wait for a terminal: an unauthenticated private remote is a failed
        // refresh, not a process that blocks until the deadline.
        startInfo.Environment["GIT_TERMINAL_PROMPT"] = "0";
        startInfo.Environment["GIT_CONFIG_NOSYSTEM"] = "1";
        startInfo.Environment["GCM_INTERACTIVE"] = "never";

        if (credential is not null)
        {
            // Configuration through the environment rather than the argument vector, because
            // a process list is readable by anything running as this user.
            startInfo.Environment["GIT_CONFIG_COUNT"] = "1";
            startInfo.Environment["GIT_CONFIG_KEY_0"] = "http.extraHeader";
            startInfo.Environment["GIT_CONFIG_VALUE_0"] =
                $"Authorization: Bearer {Encoding.UTF8.GetString(credential.Value.Span)}";
        }

        using var process = new Process { StartInfo = startInfo };

        try
        {
            _ = process.Start();
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            throw new CatalogRefreshException(
                CatalogRefreshException.SourceUnavailable,
                "Git is not available on this host, so a Git catalog source cannot be read.",
                exception);
        }

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(Deadline);

        var standardOutput = process.StandardOutput.ReadToEndAsync(deadline.Token);
        var standardError = process.StandardError.ReadToEndAsync(deadline.Token);

        try
        {
            await process.WaitForExitAsync(deadline.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            Kill(process);
            throw new CatalogRefreshException(
                CatalogRefreshException.SourceUnavailable,
                $"Reading the Git catalog source took longer than {Deadline.TotalMinutes:0} minutes.");
        }

        var output = await standardOutput.ConfigureAwait(false);
        var error = await standardError.ConfigureAwait(false);

        // Git's own message can name a private host or a credential helper, so only the fact
        // of the failure reaches the caller; the exit code says which invocation failed.
        return process.ExitCode == 0
            ? output
            : throw new CatalogRefreshException(
                CatalogRefreshException.SourceUnavailable,
                $"The Git catalog source could not be read (git exited with {process.ExitCode}).",
                new InvalidOperationException(Summarize(error)));
    }

    /// <summary>Keeps a bounded, single-line form of git's diagnostics for the server log.</summary>
    private static string Summarize(string error)
    {
        var flattened = error.ReplaceLineEndings(" ").Trim();
        return flattened.Length > 512 ? flattened[..512] : flattened;
    }

    private static void Kill(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (Exception exception) when (exception is InvalidOperationException or NotSupportedException)
        {
            // The process already exited; there is nothing left to stop.
        }
    }

    private static void Delete(string directory)
    {
        try
        {
            Directory.Delete(directory, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A partial working copy in a temporary directory is litter, not a failure.
        }
    }
}
