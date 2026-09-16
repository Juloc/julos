using JulOS.Domain.Catalog;

namespace JulOS.Infrastructure.Persistence.Core;

/// <summary>Persistence shape of one catalog source.</summary>
internal sealed class CatalogSourceRow
{
    internal Guid Id { get; set; }

    internal CatalogSourceKind Kind { get; set; }

    internal required string DisplayName { get; set; }

    internal required string Location { get; set; }

    /// <summary>Only the reference is stored; the credential itself never is.</summary>
    internal Guid? AuthenticationSecretReferenceId { get; set; }

    internal CatalogSourceTrustLevel TrustLevel { get; set; }

    internal bool Enabled { get; set; }

    /// <summary>A tombstone, so an installed application keeps a resolvable reference.</summary>
    internal DateTimeOffset? DeletedAtUtc { get; set; }

    internal int? LastSuccessfulRevision { get; set; }

    internal string? LastSuccessfulDigest { get; set; }

    internal DateTimeOffset? LastRefreshAtUtc { get; set; }

    internal CatalogRefreshState LastRefreshState { get; set; }

    internal string? LastFailureCode { get; set; }

    internal int Revision { get; set; }

    internal List<CatalogPublisherKeyRow> PublisherKeys { get; } = [];

    internal static CatalogSourceRow FromDomain(CatalogSource source)
    {
        ArgumentNullException.ThrowIfNull(source);

        return new CatalogSourceRow
        {
            Id = source.Id.Value,
            Kind = source.Kind,
            DisplayName = source.DisplayName,
            Location = source.Location,
            AuthenticationSecretReferenceId = source.AuthenticationSecretReferenceId,
            TrustLevel = source.TrustLevel,
            Enabled = source.Enabled,
            DeletedAtUtc = source.DeletedAtUtc,
            LastSuccessfulRevision = source.LastSuccessfulRevision,
            LastSuccessfulDigest = source.LastSuccessfulDigest,
            LastRefreshAtUtc = source.LastRefreshAtUtc,
            LastRefreshState = source.LastRefreshState,
            LastFailureCode = source.LastFailureCode,
            Revision = source.Revision.Value,
        };
    }

    internal CatalogSource ToDomain() => CatalogSource.Restore(
        new CatalogSourceId(this.Id),
        this.Kind,
        this.DisplayName,
        this.Location,
        this.AuthenticationSecretReferenceId,
        this.TrustLevel,
        this.Enabled,
        this.DeletedAtUtc,
        this.LastSuccessfulRevision,
        this.LastSuccessfulDigest,
        this.LastRefreshAtUtc,
        this.LastRefreshState,
        this.LastFailureCode,
        Domain.Primitives.Revision.From(this.Revision));
}

/// <summary>Persistence shape of one observed catalog publisher key.</summary>
/// <remarks>
/// Trust is stored as evidence: what was observed, when, and separately what an
/// administrator decided. A later decision changes future evaluation and never rewrites
/// what an installed application was verified with.
/// </remarks>
internal sealed class CatalogPublisherKeyRow
{
    internal Guid Id { get; set; }

    internal Guid CatalogSourceId { get; set; }

    internal required string PublisherId { get; set; }

    internal required string KeyId { get; set; }

    internal required string Algorithm { get; set; }

    internal required string PublicKeySpki { get; set; }

    internal required string PublicKeyFingerprint { get; set; }

    internal DateTimeOffset ValidFromUtc { get; set; }

    internal DateTimeOffset? ValidUntilUtc { get; set; }

    internal DateTimeOffset? RevokedAtUtc { get; set; }

    internal AdministratorTrustState AdministratorTrustState { get; set; }

    internal Guid? AdministratorDecisionByUserId { get; set; }

    internal DateTimeOffset? AdministratorDecisionAtUtc { get; set; }

    internal int FirstObservedSourceRevision { get; set; }

    internal int LastObservedSourceRevision { get; set; }

    internal int Revision { get; set; }

    internal static CatalogPublisherKeyRow FromDomain(CatalogPublisherKey key)
    {
        ArgumentNullException.ThrowIfNull(key);

        return new CatalogPublisherKeyRow
        {
            Id = key.Id.Value,
            CatalogSourceId = key.CatalogSourceId,
            PublisherId = key.PublisherId,
            KeyId = key.KeyId,
            Algorithm = key.Algorithm,
            PublicKeySpki = key.PublicKeySpki,
            PublicKeyFingerprint = key.PublicKeyFingerprint,
            ValidFromUtc = key.ValidFromUtc,
            ValidUntilUtc = key.ValidUntilUtc,
            RevokedAtUtc = key.RevokedAtUtc,
            AdministratorTrustState = key.AdministratorTrustState,
            AdministratorDecisionByUserId = key.AdministratorDecisionByUserId,
            AdministratorDecisionAtUtc = key.AdministratorDecisionAtUtc,
            FirstObservedSourceRevision = key.FirstObservedSourceRevision,
            LastObservedSourceRevision = key.LastObservedSourceRevision,
            Revision = key.Revision.Value,
        };
    }

    internal CatalogPublisherKey ToDomain() => CatalogPublisherKey.Restore(
        new CatalogPublisherKeyId(this.Id),
        this.CatalogSourceId,
        this.PublisherId,
        this.KeyId,
        this.Algorithm,
        this.PublicKeySpki,
        this.PublicKeyFingerprint,
        this.ValidFromUtc,
        this.ValidUntilUtc,
        this.RevokedAtUtc,
        this.AdministratorTrustState,
        this.AdministratorDecisionByUserId,
        this.AdministratorDecisionAtUtc,
        this.FirstObservedSourceRevision,
        this.LastObservedSourceRevision,
        Domain.Primitives.Revision.From(this.Revision));
}
