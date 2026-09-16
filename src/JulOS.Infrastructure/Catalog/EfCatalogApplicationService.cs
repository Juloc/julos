using System.Text.Json;

using JulOS.Application.Catalog;
using JulOS.Contracts.Catalog;
using JulOS.Domain.Catalog;
using JulOS.Infrastructure.Persistence.Core;

using Microsoft.EntityFrameworkCore;

namespace JulOS.Infrastructure.Catalog;

/// <summary>Serves the cached catalog without ever reaching a source.</summary>
internal sealed class EfCatalogApplicationService : ICatalogApplicationService
{
    private readonly CoreDbContext context;

    public EfCatalogApplicationService(CoreDbContext context) =>
        this.context = context ?? throw new ArgumentNullException(nameof(context));

    public async Task<IReadOnlyList<CatalogApplicationResponse>> ListAsync(
        Guid? catalogSourceId,
        CancellationToken cancellationToken = default)
    {
        if (catalogSourceId is Guid requested)
        {
            _ = await this.RequireSourceAsync(requested, cancellationToken).ConfigureAwait(false);
        }

        var rows = await this.context.CatalogEntryCache
            .AsNoTracking()
            .Where(entry => catalogSourceId == null || entry.CatalogSourceId == catalogSourceId)
            .Join(
                this.context.CatalogSources.AsNoTracking().Where(source => source.DeletedAtUtc == null),
                entry => entry.CatalogSourceId,
                source => source.Id,
                (entry, source) => new { Entry = entry, Source = source })
            .OrderBy(pair => pair.Source.DisplayName)
            .ThenBy(pair => pair.Entry.AppId)
            .ThenBy(pair => pair.Entry.Version)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return rows
            .GroupBy(pair => (pair.Entry.CatalogSourceId, pair.Entry.AppId))
            .Select(group => new CatalogApplicationResponse(
                group.Key.CatalogSourceId,
                // A cached entry exists only after a successful refresh, which is the step
                // that records the identity, so it is never null here.
                group.First().Source.SourceIdentity ?? string.Empty,
                group.First().Source.DisplayName,
                RefreshStateName(group.First().Source.LastRefreshState),
                group.Key.AppId,
                group.Select(pair => ToVersion(pair.Entry)).ToList()))
            .ToList();
    }

    public async Task<CatalogApplicationDetailResponse> ReadAsync(
        Guid catalogSourceId,
        string appId,
        string? version,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(appId);

        var source = await this.RequireSourceAsync(catalogSourceId, cancellationToken).ConfigureAwait(false);
        var entries = await this.context.CatalogEntryCache
            .AsNoTracking()
            .Where(entry => entry.CatalogSourceId == catalogSourceId
                && entry.AppId == appId
                && (version == null || entry.Version == version))
            .OrderBy(entry => entry.Version)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (entries.Count == 0)
        {
            throw NotFound("No such application version is cached for that source.");
        }

        if (entries.Count > 1)
        {
            // Naming no version is only unambiguous while one is cached. Picking one would
            // mean ranking version strings, which the catalog format does not define.
            throw new CatalogFailureException(
                CatalogErrorCodes.SourceInvalid,
                CatalogFailureReason.Invalid,
                "Several versions of that application are cached; name the one to read.");
        }

        var entry = entries[0];
        using var document = JsonDocument.Parse(entry.Definition);

        return new CatalogApplicationDetailResponse(
            catalogSourceId,
            source.SourceIdentity ?? string.Empty,
            RefreshStateName(source.LastRefreshState),
            entry.AppId,
            entry.Version,
            document.RootElement.Clone(),
            ToVersion(entry));
    }

    private async Task<CatalogSourceRow> RequireSourceAsync(
        Guid catalogSourceId,
        CancellationToken cancellationToken)
    {
        var source = await this.context.CatalogSources
            .AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == catalogSourceId && row.DeletedAtUtc == null, cancellationToken)
            .ConfigureAwait(false);

        // A removed source keeps its tombstone so installed applications stay resolvable, but
        // it is not one this installation offers to install from.
        return source ?? throw NotFound("No catalog source with that identity is configured.");
    }

    private static CatalogFailureException NotFound(string message) => new(
        CatalogErrorCodes.SourceNotFound,
        CatalogFailureReason.NotFound,
        message);

    private static CatalogApplicationVersionResponse ToVersion(CatalogEntryCacheRow entry) => new(
        entry.Version,
        entry.DefinitionDigest,
        SignatureStateName(entry.SignatureState),
        entry.PublisherId,
        entry.SignatureKeyId,
        entry.PublicKeyFingerprint,
        entry.TrustAssessmentDigest,
        entry.SourceRevision,
        entry.CachedAtUtc);

    private static string RefreshStateName(CatalogRefreshState state) => state switch
    {
        CatalogRefreshState.Never => CatalogRefreshStateNames.Never,
        CatalogRefreshState.Fresh => CatalogRefreshStateNames.Fresh,
        CatalogRefreshState.Stale => CatalogRefreshStateNames.Stale,
        _ => throw new InvalidOperationException($"Unmapped catalog refresh state '{state}'."),
    };

    private static string SignatureStateName(CatalogSignatureState state) => state switch
    {
        CatalogSignatureState.TrustedSigned => CatalogSignatureStateNames.TrustedSigned,
        CatalogSignatureState.UnknownSigned => CatalogSignatureStateNames.UnknownSigned,
        CatalogSignatureState.NotSigned => CatalogSignatureStateNames.NotSigned,
        CatalogSignatureState.InvalidSignature => CatalogSignatureStateNames.InvalidSignature,
        _ => throw new InvalidOperationException($"Unmapped catalog signature state '{state}'."),
    };
}
