using System.Text;

namespace JulOS.Domain.Catalog;

/// <summary>The detached signature a catalog source may publish beside a definition.</summary>
/// <param name="SchemaVersion">Envelope schema version; only 1 is accepted.</param>
/// <param name="PublisherId">Who claims to have produced the definition.</param>
/// <param name="KeyId">Which of that publisher's keys signed it.</param>
/// <param name="PublicKeyFingerprint">SPKI fingerprint the key must match, as <c>sha256:...</c>.</param>
/// <param name="Algorithm">Signature algorithm; only ECDSA P-256/SHA-256 P1363 is accepted.</param>
/// <param name="ArtifactSha256">The definition digest this signature covers.</param>
/// <param name="CreatedAtUtc">When the signature was made; validity is checked at this instant.</param>
/// <param name="Signature">Base64 signature bytes.</param>
public sealed record CatalogSignatureEnvelope(
    int SchemaVersion,
    string PublisherId,
    string KeyId,
    string PublicKeyFingerprint,
    string Algorithm,
    string ArtifactSha256,
    DateTimeOffset CreatedAtUtc,
    string Signature);

/// <summary>The canonical bytes a catalog signature is made over.</summary>
/// <remarks>
/// Signing a fixed prefix together with the digest, rather than the digest alone, means a
/// signature made for a JulOS app definition cannot be replayed as a signature over the
/// same bytes in another context.
/// </remarks>
public static class CatalogSignatureInput
{
    /// <summary>The only accepted signature algorithm.</summary>
    public const string Algorithm = "ecdsa-p256-sha256-p1363";

    /// <summary>The only accepted envelope schema version.</summary>
    public const int SchemaVersion = 1;

    private const string Prefix = "julos-app-definition-v1";

    /// <summary>
    /// Builds the exact signature input for one definition digest.
    /// </summary>
    /// <param name="definitionSha256">Lowercase hexadecimal definition digest.</param>
    /// <exception cref="ArgumentException">The digest is not a lowercase SHA-256 hexadecimal value.</exception>
    public static byte[] For(string definitionSha256)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(definitionSha256);
        if (!IsLowercaseSha256(definitionSha256))
        {
            throw new ArgumentException(
                "A definition digest is 64 lowercase hexadecimal characters.",
                nameof(definitionSha256));
        }

        return Encoding.UTF8.GetBytes($"{Prefix}\n{definitionSha256}\n");
    }

    /// <summary>Whether a value is a lowercase SHA-256 hexadecimal digest.</summary>
    public static bool IsLowercaseSha256(string value)
    {
        if (value.Length != 64)
        {
            return false;
        }

        foreach (var character in value)
        {
            var hexadecimal = character is >= '0' and <= '9' or >= 'a' and <= 'f';
            if (!hexadecimal)
            {
                return false;
            }
        }

        return true;
    }
}
