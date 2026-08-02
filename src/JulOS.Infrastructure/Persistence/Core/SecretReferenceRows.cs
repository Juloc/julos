using JulOS.Application.Secrets;

namespace JulOS.Infrastructure.Persistence.Core;

internal sealed class SecretReferenceRow
{
    internal Guid Id { get; set; }

    internal SecretOwningScopeType OwningScopeType { get; set; }

    internal string? OwningScopeId { get; set; }

    internal required string Purpose { get; set; }

    internal required string StorageProvider { get; set; }

    internal string? EncryptionKeyId { get; set; }

    internal byte[]? Nonce { get; set; }

    internal byte[]? Ciphertext { get; set; }

    internal byte[]? AuthenticationTag { get; set; }

    internal DateTimeOffset CreatedAtUtc { get; set; }

    internal DateTimeOffset? RotatedAtUtc { get; set; }

    internal DateTimeOffset? DeletedAtUtc { get; set; }

    internal int Revision { get; set; }
}
