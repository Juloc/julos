using JulOS.Application.Catalog;
using JulOS.Application.Secrets;
using JulOS.Domain.Catalog;

namespace JulOS.Infrastructure.Catalog;

/// <summary>
/// Reads an administrator-managed catalog directory on the Server host.
/// </summary>
/// <remarks>
/// The path rules are enforced twice on purpose. The index parser already refuses a path
/// that escapes the source root, and this reader refuses a resolved path outside the root
/// and a symbolic link as well: the first check is about what a catalog may declare, the
/// second about what this host will actually open, and a local source is the one kind where
/// those can differ.
/// </remarks>
internal sealed class LocalCatalogSourceReader : ICatalogSourceReader
{
    public CatalogSourceKind Kind => CatalogSourceKind.Local;

    public Task<CatalogSourceSnapshot> OpenAsync(
        CatalogSourceLocation location,
        SecretLease? credential,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(location);
        cancellationToken.ThrowIfCancellationRequested();

        if (credential is not null)
        {
            // A directory on the Server host is reached by file-system permission, so a
            // configured credential would be a secret nothing consumes.
            throw new CatalogRefreshException(
                CatalogRefreshException.SourceUnavailable,
                "A local catalog source takes no authentication secret.");
        }

        if (!Path.IsPathFullyQualified(location.Location))
        {
            throw new CatalogRefreshException(
                CatalogRefreshException.SourceUnavailable,
                "A local catalog source is an absolute directory path.");
        }

        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(location.Location));
        if (!Directory.Exists(root))
        {
            throw new CatalogRefreshException(
                CatalogRefreshException.SourceUnavailable,
                "The local catalog directory does not exist.");
        }

        return Task.FromResult<CatalogSourceSnapshot>(new Snapshot(root));
    }

    private sealed class Snapshot(string root) : CatalogSourceSnapshot
    {
        public override string? NativeContentIdentity => null;

        public override Task<byte[]?> TryReadAsync(
            string relativePath,
            CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);

            var resolved = Path.GetFullPath(Path.Combine(root, relativePath));
            if (!resolved.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            {
                throw Refuse("resolves outside the catalog directory");
            }

            var file = new FileInfo(resolved);
            if (!file.Exists)
            {
                return Task.FromResult<byte[]?>(null);
            }

            if (file.LinkTarget is not null)
            {
                // A link can point anywhere, including at a file the index never described.
                throw Refuse("is a link rather than a file");
            }

            if (file.Length > MaximumFileBytes)
            {
                throw Refuse($"is larger than {MaximumFileBytes} bytes");
            }

            return ReadAsync();

            async Task<byte[]?> ReadAsync() =>
                await File.ReadAllBytesAsync(resolved, cancellationToken).ConfigureAwait(false);

            CatalogRefreshException Refuse(string what) => new(
                CatalogRefreshException.SourceUnavailable,
                // The declared path is catalog content, not a host path: the resolved
                // location stays out of the message.
                $"The catalog file '{relativePath}' {what}.");
        }
    }
}
