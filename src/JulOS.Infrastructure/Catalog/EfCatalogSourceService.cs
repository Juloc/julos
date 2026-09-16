using JulOS.Application.Auditing;
using JulOS.Application.Catalog;
using JulOS.Application.Concurrency;
using JulOS.Contracts.Catalog;
using JulOS.Domain;
using JulOS.Domain.Catalog;
using JulOS.Domain.Observability;
using JulOS.Domain.Primitives;
using JulOS.Infrastructure.Persistence.Core;

using Microsoft.EntityFrameworkCore;

namespace JulOS.Infrastructure.Catalog;

/// <summary>Stores the configured catalog sources and the trust decisions about their keys.</summary>
/// <remarks>
/// <para>
/// Every change is audited, because a catalog source decides where application definitions
/// come from and a trust decision decides whose signature is enough to install from. Both
/// are security decisions that have to stay attributable after the fact.
/// </para>
/// <para>
/// The stored authentication secret reference is only ever an identifier. The credential
/// itself lives in the secret store and is never read, returned or logged here.
/// </para>
/// </remarks>
internal sealed class EfCatalogSourceService : ICatalogSourceService
{
    private const string SourceTargetType = "catalog-source";
    private const string PublisherKeyTargetType = "catalog-publisher-key";

    private readonly CoreDbContext context;
    private readonly IIdentifierGenerator identifiers;
    private readonly IAuditService audit;
    private readonly TimeProvider timeProvider;

    public EfCatalogSourceService(
        CoreDbContext context,
        IIdentifierGenerator identifiers,
        IAuditService audit,
        TimeProvider timeProvider)
    {
        this.context = context ?? throw new ArgumentNullException(nameof(context));
        this.identifiers = identifiers ?? throw new ArgumentNullException(nameof(identifiers));
        this.audit = audit ?? throw new ArgumentNullException(nameof(audit));
        this.timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public async Task<IReadOnlyList<CatalogSourceResponse>> ListAsync(
        bool includeRemoved,
        CancellationToken cancellationToken = default)
    {
        var rows = await this.context.CatalogSources
            .AsNoTracking()
            .Where(row => includeRemoved || row.DeletedAtUtc == null)
            .OrderBy(row => row.DisplayName)
            .ThenBy(row => row.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return rows.Select(ToResponse).ToList();
    }

    public async Task<CatalogSourceResponse> ReadAsync(
        Guid catalogSourceId,
        CancellationToken cancellationToken = default)
    {
        var row = await this.RequireSourceAsync(catalogSourceId, cancellationToken).ConfigureAwait(false);
        return ToResponse(row);
    }

    public async Task<CatalogSourceResponse> AddAsync(
        AddCatalogSourceRequest request,
        Guid actingUserId,
        string correlationId,
        string? remoteAddress,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var kind = ParseAddableKind(request.Kind);
        var trustLevel = ParseAssignableTrustLevel(request.TrustLevel);
        var source = Guard(() => CatalogSource.Add(
            new CatalogSourceId(this.identifiers.Create()),
            kind,
            request.DisplayName,
            request.Location,
            trustLevel));

        await this.RequireLocationFreeAsync(source.Location, null, cancellationToken).ConfigureAwait(false);

        var row = CatalogSourceRow.FromDomain(source);
        _ = this.context.CatalogSources.Add(row);
        this.StageAudit(
            actingUserId,
            "catalog.source.add",
            SourceTargetType,
            row.Id,
            correlationId,
            remoteAddress,
            $"Added catalog source '{row.DisplayName}'.",
            // The location is operator-supplied configuration, not a credential: the
            // credential lives behind the secret reference and is never part of this text.
            $"kind={request.Kind}; trust-level={request.TrustLevel}; location={row.Location}");

        _ = await this.context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return ToResponse(row);
    }

    public async Task<CatalogSourceResponse> UpdateAsync(
        Guid catalogSourceId,
        UpdateCatalogSourceRequest request,
        Guid actingUserId,
        string correlationId,
        string? remoteAddress,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var row = await this.RequireSourceAsync(catalogSourceId, cancellationToken).ConfigureAwait(false);
        RequireRevision(row.Revision, request.ExpectedRevision, "catalog source");

        var trustLevel = row.Kind == CatalogSourceKind.Official
            ? CatalogSourceTrustLevel.Official
            : ParseAssignableTrustLevel(request.TrustLevel);

        var source = row.ToDomain();
        Guard(() => source.Update(
            request.DisplayName,
            request.Location,
            request.AuthenticationSecretReferenceId,
            trustLevel,
            request.Enabled));

        await this.RequireLocationFreeAsync(source.Location, row.Id, cancellationToken).ConfigureAwait(false);

        row.DisplayName = source.DisplayName;
        row.Location = source.Location;
        row.AuthenticationSecretReferenceId = source.AuthenticationSecretReferenceId;
        row.TrustLevel = source.TrustLevel;
        row.Enabled = source.Enabled;
        row.Revision = source.Revision.Value;

        this.StageAudit(
            actingUserId,
            "catalog.source.update",
            SourceTargetType,
            row.Id,
            correlationId,
            remoteAddress,
            $"Changed catalog source '{row.DisplayName}'.",
            $"trust-level={TrustLevelName(row.TrustLevel)}; enabled={row.Enabled}; location={row.Location}");

        _ = await this.context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return ToResponse(row);
    }

    public async Task<CatalogSourceResponse> RemoveAsync(
        Guid catalogSourceId,
        int revision,
        Guid actingUserId,
        string correlationId,
        string? remoteAddress,
        CancellationToken cancellationToken = default)
    {
        var row = await this.RequireSourceAsync(catalogSourceId, cancellationToken).ConfigureAwait(false);
        RequireRevision(row.Revision, revision, "catalog source");

        var source = row.ToDomain();
        Guard(() => source.Remove(this.timeProvider.GetUtcNow()));

        row.DeletedAtUtc = source.DeletedAtUtc;
        row.Enabled = source.Enabled;
        row.Revision = source.Revision.Value;

        this.StageAudit(
            actingUserId,
            "catalog.source.remove",
            SourceTargetType,
            row.Id,
            correlationId,
            remoteAddress,
            $"Removed catalog source '{row.DisplayName}'.",
            // Stated explicitly because the difference matters to whoever reads the trail
            // later: installed applications keep resolving through this record.
            "The source is a tombstone; installed applications keep their reference to it.");

        _ = await this.context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return ToResponse(row);
    }

    public async Task<IReadOnlyList<CatalogPublisherKeyResponse>> ListPublisherKeysAsync(
        Guid catalogSourceId,
        CancellationToken cancellationToken = default)
    {
        _ = await this.RequireSourceAsync(catalogSourceId, cancellationToken).ConfigureAwait(false);

        var rows = await this.context.CatalogPublisherKeys
            .AsNoTracking()
            .Where(row => row.CatalogSourceId == catalogSourceId)
            .OrderBy(row => row.PublisherId)
            .ThenBy(row => row.KeyId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return rows.Select(ToResponse).ToList();
    }

    public async Task<CatalogPublisherKeyResponse> ReadPublisherKeyAsync(
        Guid catalogPublisherKeyId,
        CancellationToken cancellationToken = default)
    {
        var row = await this.RequirePublisherKeyAsync(catalogPublisherKeyId, cancellationToken)
            .ConfigureAwait(false);
        return ToResponse(row);
    }

    public async Task<CatalogPublisherKeyResponse> SetPublisherKeyTrustAsync(
        Guid catalogPublisherKeyId,
        SetPublisherKeyTrustRequest request,
        Guid actingUserId,
        string correlationId,
        string? remoteAddress,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (actingUserId == Guid.Empty)
        {
            throw new CatalogFailureException(
                CatalogErrorCodes.PublisherKeyInvalid,
                CatalogFailureReason.Invalid,
                "A trust decision records the administrator who made it.");
        }

        var row = await this.RequirePublisherKeyAsync(catalogPublisherKeyId, cancellationToken)
            .ConfigureAwait(false);
        RequireRevision(row.Revision, request.ExpectedRevision, "publisher key");

        var state = ParseTrustState(request.AdministratorTrustState);
        var key = row.ToDomain();
        Guard(() => key.Decide(
            state,
            state == AdministratorTrustState.Unknown ? null : actingUserId,
            this.timeProvider.GetUtcNow()));

        row.AdministratorTrustState = key.AdministratorTrustState;
        row.AdministratorDecisionByUserId = key.AdministratorDecisionByUserId;
        row.AdministratorDecisionAtUtc = key.AdministratorDecisionAtUtc;
        row.Revision = key.Revision.Value;

        this.StageAudit(
            actingUserId,
            "catalog.publisher-key.decide",
            PublisherKeyTargetType,
            row.Id,
            correlationId,
            remoteAddress,
            $"Recorded '{request.AdministratorTrustState}' for publisher key '{row.KeyId}'.",
            // The fingerprint is the thing an administrator compared before deciding, so it
            // is what makes the recorded decision checkable afterwards.
            $"publisher={row.PublisherId}; fingerprint={row.PublicKeyFingerprint}");

        _ = await this.context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return ToResponse(row);
    }

    private async Task<CatalogSourceRow> RequireSourceAsync(
        Guid catalogSourceId,
        CancellationToken cancellationToken)
    {
        var row = await this.context.CatalogSources
            .SingleOrDefaultAsync(entry => entry.Id == catalogSourceId, cancellationToken)
            .ConfigureAwait(false);

        return row ?? throw new CatalogFailureException(
            CatalogErrorCodes.SourceNotFound,
            CatalogFailureReason.NotFound,
            "No catalog source with that identity is configured.");
    }

    private async Task<CatalogPublisherKeyRow> RequirePublisherKeyAsync(
        Guid catalogPublisherKeyId,
        CancellationToken cancellationToken)
    {
        var row = await this.context.CatalogPublisherKeys
            .SingleOrDefaultAsync(entry => entry.Id == catalogPublisherKeyId, cancellationToken)
            .ConfigureAwait(false);

        return row ?? throw new CatalogFailureException(
            CatalogErrorCodes.PublisherKeyNotFound,
            CatalogFailureReason.NotFound,
            "No publisher key with that identity has been observed.");
    }

    /// <summary>
    /// Refuses a second live source reading from the same location.
    /// </summary>
    /// <remarks>
    /// The partial unique index enforces this as well. Checking here turns the database
    /// error into a stable code that names which rule was broken.
    /// </remarks>
    private async Task RequireLocationFreeAsync(
        string location,
        Guid? exceptSourceId,
        CancellationToken cancellationToken)
    {
        var taken = await this.context.CatalogSources
            .AsNoTracking()
            .AnyAsync(
                row => row.Location == location
                    && row.DeletedAtUtc == null
                    && (exceptSourceId == null || row.Id != exceptSourceId),
                cancellationToken)
            .ConfigureAwait(false);

        if (taken)
        {
            throw new CatalogFailureException(
                CatalogErrorCodes.SourceDuplicate,
                CatalogFailureReason.Duplicate,
                "Another catalog source already reads from that location.");
        }
    }

    private void StageAudit(
        Guid actingUserId,
        string action,
        string targetType,
        Guid targetId,
        string correlationId,
        string? remoteAddress,
        string summary,
        string safeDetails) =>
        this.audit.Stage(new AuditRecord(
            actingUserId == Guid.Empty ? null : actingUserId,
            AgentId: null,
            SourcePackageId: null,
            action,
            targetType,
            targetId.ToString(),
            AuditOutcome.Succeeded,
            correlationId,
            remoteAddress,
            summary,
            safeDetails));

    private static void RequireRevision(int actual, int expected, string what)
    {
        if (actual != expected)
        {
            throw new ConcurrencyConflictException(
                actual,
                new InvalidOperationException($"The {what} changed after it was loaded."));
        }
    }

    /// <summary>
    /// Runs a domain call and republishes its rule violation as a catalog failure.
    /// </summary>
    /// <remarks>
    /// The domain codes are already the documented public codes; what the translation adds
    /// is the refusal kind, which is what decides the HTTP status. An invalid field and a
    /// removed source both violate a rule, but only one of them is a conflict.
    /// </remarks>
    private static T Guard<T>(Func<T> call)
    {
        try
        {
            return call();
        }
        catch (DomainRuleViolationException violation)
        {
            throw Translate(violation);
        }
    }

    private static void Guard(Action call)
    {
        try
        {
            call();
        }
        catch (DomainRuleViolationException violation)
        {
            throw Translate(violation);
        }
    }

    private static CatalogFailureException Translate(DomainRuleViolationException violation) =>
        new(
            violation.Code,
            violation.Code switch
            {
                CatalogErrorCodes.SourceRemoved => CatalogFailureReason.Removed,
                _ => CatalogFailureReason.Invalid,
            },
            violation.Message);

    private static CatalogSourceKind ParseAddableKind(string kind) => kind switch
    {
        CatalogSourceKindNames.Https => CatalogSourceKind.Https,
        CatalogSourceKindNames.Git => CatalogSourceKind.Git,
        CatalogSourceKindNames.Oci => CatalogSourceKind.Oci,
        CatalogSourceKindNames.Local => CatalogSourceKind.Local,
        // The official source is built in. Accepting it here would let an administrator
        // mint a second one and inherit the key set that belongs to the real one.
        _ => throw new CatalogFailureException(
            CatalogErrorCodes.SourceInvalid,
            CatalogFailureReason.Invalid,
            $"'{kind}' is not a catalog source kind that can be added."),
    };

    private static CatalogSourceTrustLevel ParseAssignableTrustLevel(string trustLevel) => trustLevel switch
    {
        CatalogSourceTrustLevelNames.AdministratorTrusted => CatalogSourceTrustLevel.AdministratorTrusted,
        CatalogSourceTrustLevelNames.Custom => CatalogSourceTrustLevel.Custom,
        _ => throw new CatalogFailureException(
            CatalogErrorCodes.SourceInvalid,
            CatalogFailureReason.Invalid,
            $"'{trustLevel}' is not a catalog source trust level that can be assigned."),
    };

    private static AdministratorTrustState ParseTrustState(string state) => state switch
    {
        AdministratorTrustStateNames.Unknown => AdministratorTrustState.Unknown,
        AdministratorTrustStateNames.Trusted => AdministratorTrustState.Trusted,
        AdministratorTrustStateNames.Distrusted => AdministratorTrustState.Distrusted,
        _ => throw new CatalogFailureException(
            CatalogErrorCodes.PublisherKeyInvalid,
            CatalogFailureReason.Invalid,
            $"'{state}' is not a publisher key trust decision."),
    };

    private static CatalogSourceResponse ToResponse(CatalogSourceRow row) => new(
        row.Id,
        KindName(row.Kind),
        row.DisplayName,
        row.Location,
        row.AuthenticationSecretReferenceId,
        TrustLevelName(row.TrustLevel),
        row.Enabled,
        row.DeletedAtUtc,
        row.LastSuccessfulRevision,
        row.LastSuccessfulDigest,
        row.LastRefreshAtUtc,
        RefreshStateName(row.LastRefreshState),
        row.LastFailureCode,
        row.Revision);

    private static CatalogPublisherKeyResponse ToResponse(CatalogPublisherKeyRow row) => new(
        row.Id,
        row.CatalogSourceId,
        row.PublisherId,
        row.KeyId,
        row.Algorithm,
        row.PublicKeyFingerprint,
        row.ValidFromUtc,
        row.ValidUntilUtc,
        row.RevokedAtUtc,
        TrustStateName(row.AdministratorTrustState),
        row.AdministratorDecisionByUserId,
        row.AdministratorDecisionAtUtc,
        row.FirstObservedSourceRevision,
        row.LastObservedSourceRevision,
        row.Revision);

    private static string KindName(CatalogSourceKind kind) => kind switch
    {
        CatalogSourceKind.Official => CatalogSourceKindNames.Official,
        CatalogSourceKind.Https => CatalogSourceKindNames.Https,
        CatalogSourceKind.Git => CatalogSourceKindNames.Git,
        CatalogSourceKind.Oci => CatalogSourceKindNames.Oci,
        CatalogSourceKind.Local => CatalogSourceKindNames.Local,
        _ => throw new InvalidOperationException($"Unmapped catalog source kind '{kind}'."),
    };

    private static string TrustLevelName(CatalogSourceTrustLevel trustLevel) => trustLevel switch
    {
        CatalogSourceTrustLevel.Official => CatalogSourceTrustLevelNames.Official,
        CatalogSourceTrustLevel.AdministratorTrusted => CatalogSourceTrustLevelNames.AdministratorTrusted,
        CatalogSourceTrustLevel.Custom => CatalogSourceTrustLevelNames.Custom,
        _ => throw new InvalidOperationException($"Unmapped catalog source trust level '{trustLevel}'."),
    };

    private static string RefreshStateName(CatalogRefreshState state) => state switch
    {
        CatalogRefreshState.Never => CatalogRefreshStateNames.Never,
        CatalogRefreshState.Fresh => CatalogRefreshStateNames.Fresh,
        CatalogRefreshState.Stale => CatalogRefreshStateNames.Stale,
        _ => throw new InvalidOperationException($"Unmapped catalog refresh state '{state}'."),
    };

    private static string TrustStateName(AdministratorTrustState state) => state switch
    {
        AdministratorTrustState.Unknown => AdministratorTrustStateNames.Unknown,
        AdministratorTrustState.Trusted => AdministratorTrustStateNames.Trusted,
        AdministratorTrustState.Distrusted => AdministratorTrustStateNames.Distrusted,
        _ => throw new InvalidOperationException($"Unmapped administrator trust state '{state}'."),
    };
}
