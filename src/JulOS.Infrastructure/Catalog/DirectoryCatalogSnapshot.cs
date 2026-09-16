using JulOS.Application.Catalog;

namespace JulOS.Infrastructure.Catalog;

/// <summary>A catalog snapshot backed by a directory on the Server host.</summary>
/// <remarks>
/// The path rules are enforced twice on purpose. The index parser already refuses a path that
/// escapes the source root, and this snapshot refuses a resolved path outside the root and a
/// symbolic link as well: the first check is about what a catalog may declare, the second
/// about what this host will actually open, and a directory is where those can differ.
/// </remarks>
/// <param name="root">The absolute directory the catalog lives in.</param>
/// <param name="nativeContentIdentity">A whole-source identity, when the transport has one.</param>
/// <param name="deleteOnDispose">Whether the directory is a working copy this snapshot owns.</param>
internal sealed class DirectoryCatalogSnapshot(
    string root,
    string? nativeContentIdentity,
    bool deleteOnDispose) : CatalogSourceSnapshot
{
    public override string? NativeContentIdentity => nativeContentIdentity;

    public override Task<byte[]?> TryReadAsync(
        string relativePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);

        var resolved = Path.GetFullPath(Path.Combine(root, relativePath));
        if (!resolved.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw Refuse(relativePath, "resolves outside the catalog directory");
        }

        var file = new FileInfo(resolved);
        if (!file.Exists)
        {
            return Task.FromResult<byte[]?>(null);
        }

        if (file.LinkTarget is not null)
        {
            // A link can point anywhere, including at a file the index never described.
            throw Refuse(relativePath, "is a link rather than a file");
        }

        if (file.Length > MaximumFileBytes)
        {
            throw Refuse(relativePath, $"is larger than {MaximumFileBytes} bytes");
        }

        return ReadAsync();

        async Task<byte[]?> ReadAsync() =>
            await File.ReadAllBytesAsync(resolved, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public override ValueTask DisposeAsync()
    {
        if (deleteOnDispose)
        {
            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch (IOException)
            {
                // A working copy the operating system still holds open is litter in a
                // temporary directory, not a reason to fail a refresh that already succeeded.
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        return base.DisposeAsync();
    }

    // The declared path is catalog content, not a host path: the resolved location stays out
    // of the message.
    private static CatalogRefreshException Refuse(string relativePath, string what) => new(
        CatalogRefreshException.SourceUnavailable,
        $"The catalog file '{relativePath}' {what}.");
}
