using JulOS.Application.Catalog;
using JulOS.Application.Secrets;
using JulOS.Domain.Catalog;

namespace JulOS.Infrastructure.Catalog;

/// <summary>Reads an administrator-managed catalog directory on the Server host.</summary>
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

        // A directory an administrator manages has no whole-source identity of its own, so
        // the refresh locks to the digest of the index instead.
        return Task.FromResult<CatalogSourceSnapshot>(
            new DirectoryCatalogSnapshot(root, nativeContentIdentity: null, deleteOnDispose: false));
    }
}
