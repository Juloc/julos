using JulOS.Domain.Primitives;

namespace JulOS.Domain.Catalog;

/// <summary>Where a catalog source gets its content from.</summary>
public enum CatalogSourceKind
{
    /// <summary>The built-in JulOS source.</summary>
    Official = 1,

    /// <summary>A static versioned catalog served over HTTPS.</summary>
    Https = 2,

    /// <summary>A public or private Git repository.</summary>
    Git = 3,

    /// <summary>A catalog artifact stored in an OCI registry.</summary>
    Oci = 4,

    /// <summary>An administrator-managed local catalog.</summary>
    Local = 5,
}

/// <summary>How much this installation trusts a source as a source.</summary>
/// <remarks>
/// Independent of any artifact signature. A trusted source can still publish an
/// unsigned definition, and an untrusted one can publish a correctly signed definition.
/// </remarks>
public enum CatalogSourceTrustLevel
{
    /// <summary>The built-in source.</summary>
    Official = 1,

    /// <summary>Explicitly trusted by an administrator.</summary>
    AdministratorTrusted = 2,

    /// <summary>Added but not elevated.</summary>
    Custom = 3,
}

/// <summary>What the last refresh of a source achieved.</summary>
public enum CatalogRefreshState
{
    /// <summary>Never refreshed.</summary>
    Never = 1,

    /// <summary>The cached catalog is the result of a successful refresh.</summary>
    Fresh = 2,

    /// <summary>
    /// The last refresh failed and the previous valid catalog is still being served.
    /// </summary>
    /// <remarks>
    /// Explicitly marked rather than silently kept: a user looking at a catalog is entitled
    /// to know it is not current.
    /// </remarks>
    Stale = 3,
}

/// <summary>The generated identity of one catalog source.</summary>
/// <param name="Value">The generated identifier value.</param>
public readonly record struct CatalogSourceId(Guid Value)
{
    /// <summary>The generated identifier value, validated to identify an entity.</summary>
    public Guid Value { get; } = EntityIdentifier.Validated(Value);
}

/// <summary>
/// One place JulOS reads application definitions from.
/// </summary>
/// <remarks>
/// A source is a reference, not a container. Removing it leaves installed applications
/// alone and keeps the reference resolvable, because an installation records which source
/// it came from and that history stays true after the source is gone.
/// </remarks>
public sealed class CatalogSource
{
    private CatalogSource(
        CatalogSourceId id,
        CatalogSourceKind kind,
        string displayName,
        string location,
        CatalogSourceTrustLevel trustLevel)
    {
        this.Id = id;
        this.Kind = kind;
        this.DisplayName = displayName;
        this.Location = location;
        this.TrustLevel = trustLevel;
        this.Enabled = true;
        this.LastRefreshState = CatalogRefreshState.Never;
        this.Revision = Revision.Initial;
    }

    /// <summary>The generated identity of this source.</summary>
    public CatalogSourceId Id { get; }

    /// <summary>Where the source gets its content from.</summary>
    public CatalogSourceKind Kind { get; }

    /// <summary>The name shown to administrators.</summary>
    public string DisplayName { get; private set; }

    /// <summary>The source location, interpreted by the adapter for its kind.</summary>
    /// <remarks>Bounded so that the uniqueness index over it stays within the index row limit.</remarks>
    public string Location { get; private set; }

    /// <summary>The secret reference holding private credentials, if the source needs any.</summary>
    /// <remarks>Credentials are never stored here; only the reference to them is.</remarks>
    public Guid? AuthenticationSecretReferenceId { get; private set; }

    /// <summary>How much this installation trusts the source as a source.</summary>
    public CatalogSourceTrustLevel TrustLevel { get; private set; }

    /// <summary>Whether the source is consulted at all.</summary>
    public bool Enabled { get; private set; }

    /// <summary>When the source was removed, or null. A tombstone, not a deletion.</summary>
    public DateTimeOffset? DeletedAtUtc { get; private set; }

    /// <summary>The source revision of the last catalog that parsed completely.</summary>
    public int? LastSuccessfulRevision { get; private set; }

    /// <summary>The immutable digest of that catalog.</summary>
    public string? LastSuccessfulDigest { get; private set; }

    /// <summary>When the source was last refreshed, successfully or not.</summary>
    public DateTimeOffset? LastRefreshAtUtc { get; private set; }

    /// <summary>What that refresh achieved.</summary>
    public CatalogRefreshState LastRefreshState { get; private set; }

    /// <summary>The stable code of the last failure, or null.</summary>
    public string? LastFailureCode { get; private set; }

    /// <summary>The concurrency revision.</summary>
    public Revision Revision { get; private set; }

    /// <summary>Whether the source still takes part in refreshes.</summary>
    public bool IsActive => this.Enabled && this.DeletedAtUtc is null;

    /// <summary>Adds a source.</summary>
    /// <exception cref="DomainRuleViolationException">A required value is missing or invalid.</exception>
    public static CatalogSource Add(
        CatalogSourceId id,
        CatalogSourceKind kind,
        string displayName,
        string location,
        CatalogSourceTrustLevel trustLevel)
    {
        if (kind == CatalogSourceKind.Official && trustLevel != CatalogSourceTrustLevel.Official)
        {
            throw new DomainRuleViolationException(
                "catalog.source_invalid",
                "The official source is official; its trust level is not a choice.");
        }

        if (kind != CatalogSourceKind.Official && trustLevel == CatalogSourceTrustLevel.Official)
        {
            // Otherwise an administrator could mint a second "official" source and inherit
            // the pinned key set that belongs to the real one.
            throw new DomainRuleViolationException(
                "catalog.source_invalid",
                "Only the built-in source is official.");
        }

        return new CatalogSource(
            id,
            kind,
            RequireText(displayName, "display name", 128),
            RequireLocation(location),
            trustLevel);
    }

    /// <summary>Changes what an administrator may change about a source.</summary>
    /// <remarks>
    /// The kind is deliberately not among them. Changing it would keep the identity that
    /// installed applications point at while changing what that identity means.
    /// </remarks>
    public void Update(
        string displayName,
        string location,
        Guid? authenticationSecretReferenceId,
        CatalogSourceTrustLevel trustLevel,
        bool enabled)
    {
        this.RequireLive();

        if (this.Kind == CatalogSourceKind.Official && trustLevel != CatalogSourceTrustLevel.Official)
        {
            throw new DomainRuleViolationException(
                "catalog.source_invalid",
                "The official source is official; its trust level is not a choice.");
        }

        if (this.Kind != CatalogSourceKind.Official && trustLevel == CatalogSourceTrustLevel.Official)
        {
            throw new DomainRuleViolationException(
                "catalog.source_invalid",
                "Only the built-in source is official.");
        }

        this.DisplayName = RequireText(displayName, "display name", 128);
        this.Location = RequireLocation(location);
        this.AuthenticationSecretReferenceId = authenticationSecretReferenceId;
        this.TrustLevel = trustLevel;
        this.Enabled = enabled;
        this.Revision = this.Revision.Next();
    }

    /// <summary>Records a refresh that parsed completely.</summary>
    public void RecordRefreshSucceeded(int sourceRevision, string digest, DateTimeOffset refreshedAtUtc)
    {
        this.RequireLive();

        this.LastSuccessfulRevision = sourceRevision;
        this.LastSuccessfulDigest = RequireText(digest, "digest", 256);
        this.LastRefreshAtUtc = refreshedAtUtc;
        this.LastRefreshState = CatalogRefreshState.Fresh;
        this.LastFailureCode = null;
        this.Revision = this.Revision.Next();
    }

    /// <summary>
    /// Records a refresh that failed.
    /// </summary>
    /// <remarks>
    /// The last successful revision and digest are deliberately untouched: a failed refresh
    /// keeps the last valid catalog and marks it stale, and never replaces it with a
    /// partially parsed source.
    /// </remarks>
    public void RecordRefreshFailed(string failureCode, DateTimeOffset refreshedAtUtc)
    {
        this.RequireLive();

        this.LastRefreshAtUtc = refreshedAtUtc;
        this.LastRefreshState = this.LastSuccessfulRevision is null
            ? CatalogRefreshState.Never
            : CatalogRefreshState.Stale;
        this.LastFailureCode = RequireText(failureCode, "failure code", 128);
        this.Revision = this.Revision.Next();
    }

    /// <summary>
    /// Removes the source, leaving a tombstone.
    /// </summary>
    /// <remarks>
    /// Installed applications keep pointing at it. A deployment records which source it came
    /// from, and that history must stay resolvable after the source is gone.
    /// </remarks>
    public void Remove(DateTimeOffset removedAtUtc)
    {
        if (this.Kind == CatalogSourceKind.Official)
        {
            throw new DomainRuleViolationException(
                "catalog.source_invalid",
                "The built-in official source cannot be removed.");
        }

        if (this.DeletedAtUtc is not null)
        {
            return;
        }

        this.DeletedAtUtc = removedAtUtc;
        this.Enabled = false;
        this.Revision = this.Revision.Next();
    }

    /// <summary>Restores a persisted source without advancing its revision.</summary>
    public static CatalogSource Restore(
        CatalogSourceId id,
        CatalogSourceKind kind,
        string displayName,
        string location,
        Guid? authenticationSecretReferenceId,
        CatalogSourceTrustLevel trustLevel,
        bool enabled,
        DateTimeOffset? deletedAtUtc,
        int? lastSuccessfulRevision,
        string? lastSuccessfulDigest,
        DateTimeOffset? lastRefreshAtUtc,
        CatalogRefreshState lastRefreshState,
        string? lastFailureCode,
        Revision revision) =>
        new(id, kind, displayName, location, trustLevel)
        {
            AuthenticationSecretReferenceId = authenticationSecretReferenceId,
            Enabled = enabled,
            DeletedAtUtc = deletedAtUtc,
            LastSuccessfulRevision = lastSuccessfulRevision,
            LastSuccessfulDigest = lastSuccessfulDigest,
            LastRefreshAtUtc = lastRefreshAtUtc,
            LastRefreshState = lastRefreshState,
            LastFailureCode = lastFailureCode,
            Revision = revision,
        };

    private void RequireLive()
    {
        if (this.DeletedAtUtc is not null)
        {
            throw new DomainRuleViolationException(
                "catalog.source_removed",
                "A removed catalog source is a tombstone and is not changed further.");
        }
    }

    /// <summary>
    /// Validates a source location and refuses one carrying a password.
    /// </summary>
    /// <remarks>
    /// A location is stored, returned to administrators and written to the audit trail, so a
    /// password embedded in it would be a secret in a URL and in a log. Private credentials
    /// belong in the secret reference instead. A userinfo without a password is left alone:
    /// <c>git@host:path</c> names a user, not a credential.
    /// </remarks>
    private static string RequireLocation(string location)
    {
        var trimmed = RequireText(location, "location", 512);
        var authorityStart = trimmed.IndexOf("//", StringComparison.Ordinal) is var scheme and >= 0
            ? scheme + 2
            : 0;
        var authorityEnd = trimmed.IndexOf('/', authorityStart);
        var authority = authorityEnd < 0 ? trimmed[authorityStart..] : trimmed[authorityStart..authorityEnd];
        var userInfoEnd = authority.IndexOf('@', StringComparison.Ordinal);

        return userInfoEnd >= 0 && authority[..userInfoEnd].Contains(':', StringComparison.Ordinal)
            ? throw new DomainRuleViolationException(
                "catalog.source_invalid",
                "A catalog source location carries no password; use an authentication secret reference.")
            : trimmed;
    }

    private static string RequireText(string value, string what, int maximumLength)
    {
        var trimmed = value?.Trim() ?? string.Empty;
        return trimmed.Length == 0 || trimmed.Length > maximumLength
            ? throw new DomainRuleViolationException(
                "catalog.source_invalid",
                $"A catalog source {what} has 1 to {maximumLength} characters.")
            : trimmed;
    }
}
