using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace JulOS.Domain.Packages;

/// <summary>The stable warning codes an installation confirmation shows.</summary>
public static class PackageTrustWarnings
{
    /// <summary>The artifact carries no publisher signature at all.</summary>
    /// <remarks>Named NotSigned rather than Unsigned so it is not read as a numeric type.</remarks>
    public const string NotSigned = "package.warning.unsigned";

    /// <summary>The signature is valid but the key is not one this installation trusts.</summary>
    public const string UnknownPublisher = "package.warning.unknown_publisher";

    /// <summary>The package's frontend and worker will run on the isolated path.</summary>
    public const string IsolatedRuntime = "package.warning.isolated_runtime";

    /// <summary>The package declares rights that are not granted by installing alone.</summary>
    public const string CriticalRights = "package.warning.critical_rights";
}

/// <summary>
/// The rights a package declares that an administrator has to see before installing.
/// </summary>
/// <remarks>
/// Digested rather than compared field by field so that "the rights changed" is one
/// comparison an approval can be bound to. Signing a package does not make its rights safe,
/// which is why this is independent of the signature state and shown either way.
/// </remarks>
/// <param name="Permissions">Declared permission names.</param>
/// <param name="RuntimeKind">Declared worker runtime kind.</param>
/// <param name="RuntimeImage">Declared worker image, or null.</param>
/// <param name="NetworkAccess">Whether the worker requests network access.</param>
public sealed record PackageCriticalRights(
    IReadOnlyList<string> Permissions,
    string RuntimeKind,
    string? RuntimeImage,
    bool NetworkAccess)
{
    /// <summary>The lowercase SHA-256 over the canonical form of these rights.</summary>
    public string Digest()
    {
        // Ordinal-sorted so that a manifest listing the same permissions in another order is
        // the same rights, and a rights change is therefore always a real change.
        var canonical = JsonSerializer.Serialize(new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["networkAccess"] = this.NetworkAccess,
            ["permissions"] = this.Permissions.Order(StringComparer.Ordinal).ToArray(),
            ["runtimeImage"] = this.RuntimeImage,
            ["runtimeKind"] = this.RuntimeKind,
        });

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    /// <summary>Whether these rights are ones an administrator is warned about.</summary>
    /// <remarks>
    /// A package that requests no permission and runs no worker asks for nothing beyond being
    /// present, and a warning there would be noise that teaches an administrator to click
    /// through the ones that matter.
    /// </remarks>
    public bool AreCritical => this.Permissions.Count > 0 || this.NetworkAccess || this.RuntimeImage is not null;
}

/// <summary>
/// Everything about one package artifact an administrator confirms before it is installed.
/// </summary>
/// <remarks>
/// <para>
/// The acknowledgement is a digest over this whole assessment rather than a stored token. An
/// approval therefore cannot be replayed for different bytes, a different version, different
/// rights, a different publisher or a different operation: any of those produces a different
/// digest, and the server recomputes it from what was actually uploaded rather than trusting
/// what the caller says it approved.
/// </para>
/// <para>
/// That also means nothing has to be persisted between preview and apply, so there is no
/// window in which an approval exists for an artifact the server no longer has.
/// </para>
/// </remarks>
/// <param name="ArtifactDigest">The verified artifact digest.</param>
/// <param name="PackageId">The package identity the manifest declares.</param>
/// <param name="Version">The version the manifest declares.</param>
/// <param name="SignatureState">How much is known about who produced it.</param>
/// <param name="PublisherId">Who claimed to produce it, or null when unsigned.</param>
/// <param name="KeyId">Which key signed it, or null when unsigned.</param>
/// <param name="PublicKeyFingerprint">The fingerprint of that key, or null when unsigned.</param>
/// <param name="CriticalRightsDigest">The digest over the declared rights.</param>
/// <param name="Warnings">Stable warning codes, ordinally sorted.</param>
public sealed record PackageTrustAssessment(
    string ArtifactDigest,
    string PackageId,
    string Version,
    PackageSignatureState SignatureState,
    string? PublisherId,
    string? KeyId,
    string? PublicKeyFingerprint,
    string CriticalRightsDigest,
    IReadOnlyList<string> Warnings)
{
    /// <summary>
    /// Whether installing requires an explicit administrator acknowledgement.
    /// </summary>
    /// <remarks>
    /// Written as "trusted is the exception" for the same reason isolation is: a signature
    /// state added later needs an acknowledgement by default rather than silently inheriting
    /// the unconfirmed path.
    /// </remarks>
    public bool RequiresAcknowledgement =>
        this.SignatureState != PackageSignatureState.TrustedSigned || this.Warnings.Count > 0;

    /// <summary>Builds the assessment for one evaluated artifact.</summary>
    /// <param name="artifactDigest">The verified artifact digest.</param>
    /// <param name="packageId">The package identity the manifest declares.</param>
    /// <param name="version">The version the manifest declares.</param>
    /// <param name="signatureState">How much is known about who produced it.</param>
    /// <param name="publisherId">Who claimed to produce it, or null when unsigned.</param>
    /// <param name="keyId">Which key signed it, or null when unsigned.</param>
    /// <param name="publicKeyFingerprint">The fingerprint of that key, or null when unsigned.</param>
    /// <param name="rights">The rights the manifest declares.</param>
    public static PackageTrustAssessment For(
        string artifactDigest,
        string packageId,
        string version,
        PackageSignatureState signatureState,
        string? publisherId,
        string? keyId,
        string? publicKeyFingerprint,
        PackageCriticalRights rights)
    {
        ArgumentNullException.ThrowIfNull(rights);

        var warnings = new SortedSet<string>(StringComparer.Ordinal);
        if (signatureState == PackageSignatureState.NotSigned)
        {
            warnings.Add(PackageTrustWarnings.NotSigned);
        }

        if (signatureState == PackageSignatureState.UnknownSigned)
        {
            warnings.Add(PackageTrustWarnings.UnknownPublisher);
        }

        if (PackageIsolation.IsRequiredFor(signatureState))
        {
            warnings.Add(PackageTrustWarnings.IsolatedRuntime);
        }

        if (rights.AreCritical)
        {
            // Shown for a trusted publisher too: signing a package does not make its rights
            // safe, and runtime-right changes are always disclosed.
            warnings.Add(PackageTrustWarnings.CriticalRights);
        }

        return new PackageTrustAssessment(
            artifactDigest,
            packageId,
            version,
            signatureState,
            publisherId,
            keyId,
            publicKeyFingerprint,
            rights.Digest(),
            [.. warnings]);
    }

    /// <summary>
    /// The acknowledgement digest that binds this assessment to one operation.
    /// </summary>
    /// <param name="operationKey">The idempotency key of the operation that will apply it.</param>
    /// <remarks>
    /// The operation is part of the digest so that an approval obtained for one install
    /// cannot be presented for another, even for the identical artifact.
    /// </remarks>
    public string Acknowledgement(string operationKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operationKey);

        var canonical = JsonSerializer.Serialize(new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["artifactDigest"] = this.ArtifactDigest,
            ["criticalRightsDigest"] = this.CriticalRightsDigest,
            ["keyId"] = this.KeyId,
            ["operationKey"] = operationKey,
            ["packageId"] = this.PackageId,
            ["publicKeyFingerprint"] = this.PublicKeyFingerprint,
            ["publisherId"] = this.PublisherId,
            ["signatureState"] = this.SignatureState.ToString(),
            ["version"] = this.Version,
            ["warnings"] = this.Warnings,
        });

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    /// <summary>Whether a presented acknowledgement is the one this assessment requires.</summary>
    /// <param name="presented">The acknowledgement digest the caller supplied, or null.</param>
    /// <param name="operationKey">The idempotency key of the operation applying it.</param>
    public bool IsAcknowledgedBy(string? presented, string operationKey) =>
        !this.RequiresAcknowledgement
        || (presented is { Length: > 0 }
            && CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(presented),
                Encoding.UTF8.GetBytes(this.Acknowledgement(operationKey))));
}
