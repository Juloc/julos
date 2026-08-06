using System.Security.Cryptography;
using System.Text;

using JulOS.Application.Secrets;

namespace JulOS.Infrastructure.Secrets;

internal sealed record ProtectedSecretValue(
    string KeyId,
    byte[] Nonce,
    byte[] Ciphertext,
    byte[] AuthenticationTag);

internal interface ISecretProtector
{
    ProtectedSecretValue Protect(
        Guid secretReferenceId,
        SecretOwningScopeType owningScopeType,
        string? owningScopeId,
        string purpose,
        ReadOnlySpan<byte> plaintext);

    byte[] Unprotect(
        Guid secretReferenceId,
        SecretOwningScopeType owningScopeType,
        string? owningScopeId,
        string purpose,
        string keyId,
        ReadOnlySpan<byte> nonce,
        ReadOnlySpan<byte> ciphertext,
        ReadOnlySpan<byte> authenticationTag);
}

/// <summary>Authenticates and encrypts every value with AES-256-GCM and stable associated metadata.</summary>
internal sealed class AesGcmSecretProtector : ISecretProtector
{
    private const int NonceSizeBytes = 12;
    private const int AuthenticationTagSizeBytes = 16;
    private readonly SecretEncryptionKeyRing keyRing;

    public AesGcmSecretProtector(SecretEncryptionKeyRing keyRing)
    {
        this.keyRing = keyRing ?? throw new ArgumentNullException(nameof(keyRing));
    }

    public ProtectedSecretValue Protect(
        Guid secretReferenceId,
        SecretOwningScopeType owningScopeType,
        string? owningScopeId,
        string purpose,
        ReadOnlySpan<byte> plaintext)
    {
        if (plaintext.IsEmpty)
        {
            throw new SecretReferenceFailureException(SecretReferenceFailureReason.Invalid);
        }

        var nonce = RandomNumberGenerator.GetBytes(NonceSizeBytes);
        var ciphertext = new byte[plaintext.Length];
        var authenticationTag = new byte[AuthenticationTagSizeBytes];
        var associatedData = CreateAssociatedData(secretReferenceId, owningScopeType, owningScopeId, purpose);

        try
        {
            using var aes = new AesGcm(
                this.keyRing.GetKey(this.keyRing.ActiveKeyId),
                AuthenticationTagSizeBytes);
            aes.Encrypt(nonce, plaintext, ciphertext, authenticationTag, associatedData);
            return new ProtectedSecretValue(
                this.keyRing.ActiveKeyId,
                nonce,
                ciphertext,
                authenticationTag);
        }
        catch
        {
            CryptographicOperations.ZeroMemory(ciphertext);
            throw;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(associatedData);
        }
    }

    public byte[] Unprotect(
        Guid secretReferenceId,
        SecretOwningScopeType owningScopeType,
        string? owningScopeId,
        string purpose,
        string keyId,
        ReadOnlySpan<byte> nonce,
        ReadOnlySpan<byte> ciphertext,
        ReadOnlySpan<byte> authenticationTag)
    {
        if (nonce.Length != NonceSizeBytes
            || ciphertext.IsEmpty
            || authenticationTag.Length != AuthenticationTagSizeBytes)
        {
            throw new SecretReferenceFailureException(SecretReferenceFailureReason.Unavailable);
        }

        var plaintext = new byte[ciphertext.Length];
        var associatedData = CreateAssociatedData(secretReferenceId, owningScopeType, owningScopeId, purpose);

        try
        {
            using var aes = new AesGcm(this.keyRing.GetKey(keyId), AuthenticationTagSizeBytes);
            aes.Decrypt(nonce, ciphertext, authenticationTag, plaintext, associatedData);
            return plaintext;
        }
        catch (Exception exception) when (exception is CryptographicException or KeyNotFoundException)
        {
            CryptographicOperations.ZeroMemory(plaintext);
            throw new SecretReferenceFailureException(SecretReferenceFailureReason.Unavailable, exception);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(associatedData);
        }
    }

    private static byte[] CreateAssociatedData(
        Guid secretReferenceId,
        SecretOwningScopeType owningScopeType,
        string? owningScopeId,
        string purpose) => Encoding.UTF8.GetBytes(
            string.Join(
                "\n",
                "JulOS.SecretReference.v1",
                secretReferenceId.ToString("D", System.Globalization.CultureInfo.InvariantCulture),
                owningScopeType.ToString(),
                owningScopeId ?? string.Empty,
                purpose));
}
