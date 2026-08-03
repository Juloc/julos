using System.Text.Json;
using System.Text.Json.Serialization;

namespace JulOS.Agent;

internal sealed class AgentIdentityStore
{
    private const int MaximumDocumentBytes = 16 * 1024;
    private const UnixFileMode PrivateFileMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
    private const UnixFileMode PrivateDirectoryMode =
        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private readonly string path;

    internal AgentIdentityStore(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
        {
            throw new ArgumentException("Agent identity path must be absolute.", nameof(path));
        }

        this.path = Path.GetFullPath(path);
    }

    internal IDisposable AcquireProvisioningLock()
    {
        if (OperatingSystem.IsMacOS())
        {
            throw new PlatformNotSupportedException(
                "JulOS Agent provisioning locks are not supported on macOS.");
        }

        var directory = this.EnsureDirectory();
        var lockPath = this.path + ".lock";
        RejectSymbolicLink(lockPath);
        var options = new FileStreamOptions
        {
            Mode = FileMode.OpenOrCreate,
            Access = FileAccess.ReadWrite,
            Share = FileShare.ReadWrite,
            Options = FileOptions.WriteThrough,
        };
        if (OperatingSystem.IsLinux())
        {
            options.UnixCreateMode = PrivateFileMode;
        }

        var stream = new FileStream(lockPath, options);
        try
        {
            if (OperatingSystem.IsLinux())
            {
                File.SetUnixFileMode(lockPath, PrivateFileMode);
            }

            stream.Lock(0, 1);
            _ = directory;
            return stream;
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    internal async Task<AgentProvisioningState?> LoadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(this.path))
        {
            return null;
        }

        RejectSymbolicLink(this.path);
        ValidatePrivateMode(this.path);
        var information = new FileInfo(this.path);
        if (information.Length is < 1 or > MaximumDocumentBytes)
        {
            throw new InvalidOperationException("The Agent identity document has an invalid size.");
        }

        await using var stream = new FileStream(
            this.path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.None,
            bufferSize: 4096,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        AgentProvisioningState? state;
        try
        {
            state = await JsonSerializer.DeserializeAsync<AgentProvisioningState>(
                stream,
                JsonOptions,
                cancellationToken).ConfigureAwait(false);
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException("The Agent identity document is malformed.", exception);
        }

        if (state is null)
        {
            throw new InvalidOperationException("The Agent identity document is empty.");
        }

        state.Validate();
        return state;
    }

    internal async Task SaveAsync(
        AgentProvisioningState state,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(state);
        state.Validate();
        var directory = this.EnsureDirectory();
        if (File.Exists(this.path))
        {
            RejectSymbolicLink(this.path);
            ValidatePrivateMode(this.path);
        }

        var bytes = JsonSerializer.SerializeToUtf8Bytes(state, JsonOptions);
        if (bytes.Length > MaximumDocumentBytes)
        {
            throw new InvalidOperationException("The Agent identity document is too large.");
        }

        var temporaryPath = Path.Combine(
            directory,
            $".{Path.GetFileName(this.path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            var options = new FileStreamOptions
            {
                Mode = FileMode.CreateNew,
                Access = FileAccess.Write,
                Share = FileShare.None,
                Options = FileOptions.Asynchronous | FileOptions.WriteThrough,
            };
            if (OperatingSystem.IsLinux())
            {
                options.UnixCreateMode = PrivateFileMode;
            }

            await using (var stream = new FileStream(temporaryPath, options))
            {
                await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }

            if (OperatingSystem.IsLinux())
            {
                File.SetUnixFileMode(temporaryPath, PrivateFileMode);
            }

            File.Move(temporaryPath, this.path, overwrite: true);
            ValidatePrivateMode(this.path);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private string EnsureDirectory()
    {
        var directory = Path.GetDirectoryName(this.path)
            ?? throw new InvalidOperationException("Agent identity path has no parent directory.");
        if (!Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
            if (OperatingSystem.IsLinux())
            {
                File.SetUnixFileMode(directory, PrivateDirectoryMode);
            }
        }

        return directory;
    }

    private static void RejectSymbolicLink(string path)
    {
        if (!File.Exists(path))
        {
            return;
        }

        var information = new FileInfo(path);
        if (information.LinkTarget is not null
            || (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidOperationException("Symbolic links are not allowed for Agent identity state.");
        }
    }

    private static void ValidatePrivateMode(string path)
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        var mode = File.GetUnixFileMode(path);
        if (mode != PrivateFileMode)
        {
            throw new InvalidOperationException("Agent identity state must have Unix mode 0600.");
        }
    }
}
