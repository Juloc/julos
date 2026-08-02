namespace JulOS.Contracts.Secrets;

/// <summary>Stable owning-scope names for secret-reference metadata.</summary>
public static class SecretReferenceScopeTypes
{
    /// <summary>The secret belongs to a Core-owned operation.</summary>
    public const string System = "system";

    /// <summary>The secret belongs to one package identity.</summary>
    public const string Package = "package";
}

/// <summary>Stable storage-provider names returned as non-secret metadata.</summary>
public static class SecretStorageProviders
{
    /// <summary>AES-256-GCM with keys loaded from the external JulOS key ring.</summary>
    public const string CoreAesGcmV1 = "core-aes-gcm-v1";
}

/// <summary>Stable public failures owned by secret-reference management.</summary>
public static class SecretReferenceErrorCodes
{
    /// <summary>The submitted metadata or value is invalid.</summary>
    public const string Invalid = "secret_reference.invalid";

    /// <summary>The requested secret reference does not exist.</summary>
    public const string NotFound = "secret_reference.not_found";

    /// <summary>The secret reference was already deleted.</summary>
    public const string Deleted = "secret_reference.deleted";

    /// <summary>The requested operation is not allowed to lease the secret.</summary>
    public const string LeaseDenied = "secret_reference.lease_denied";

    /// <summary>The secret cannot currently be decrypted.</summary>
    public const string Unavailable = "secret_reference.unavailable";

    /// <summary>The short-lived secret lease has expired.</summary>
    public const string LeaseExpired = "secret_reference.lease_expired";
}

/// <summary>Creates one encrypted secret reference.</summary>
/// <param name="OwningScopeType">Either <c>system</c> or <c>package</c>.</param>
/// <param name="OwningScopeId">Required for a package scope and absent for a system scope.</param>
/// <param name="Purpose">A stable non-secret purpose such as <c>remote.password</c>.</param>
/// <param name="SecretValue">The value accepted only for this request and never returned.</param>
public sealed record CreateSecretReferenceRequest(
    string OwningScopeType,
    string? OwningScopeId,
    string Purpose,
    string SecretValue);

/// <summary>Rotates the encrypted value of an existing secret reference.</summary>
/// <param name="SecretValue">The replacement value accepted only for this request and never returned.</param>
/// <param name="Revision">The revision read by the caller.</param>
public sealed record RotateSecretReferenceRequest(
    string SecretValue,
    int Revision);

/// <summary>Non-secret metadata for one opaque secret reference.</summary>
/// <param name="SecretReferenceId">The opaque stable reference identifier.</param>
/// <param name="OwningScopeType">Either <c>system</c> or <c>package</c>.</param>
/// <param name="OwningScopeId">The package identity for a package scope.</param>
/// <param name="Purpose">The stable non-secret purpose.</param>
/// <param name="StorageProvider">The encryption provider that owns the stored value.</param>
/// <param name="IsPresent">Whether encrypted value material is still present.</param>
/// <param name="CreatedAtUtc">When the reference was created.</param>
/// <param name="RotatedAtUtc">When the value was most recently replaced.</param>
/// <param name="DeletedAtUtc">When encrypted value material was destroyed.</param>
/// <param name="Revision">The optimistic-concurrency revision.</param>
public sealed record SecretReferenceResponse(
    Guid SecretReferenceId,
    string OwningScopeType,
    string? OwningScopeId,
    string Purpose,
    string StorageProvider,
    bool IsPresent,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? RotatedAtUtc,
    DateTimeOffset? DeletedAtUtc,
    int Revision);
