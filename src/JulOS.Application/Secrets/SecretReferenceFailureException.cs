using JulOS.Contracts.Secrets;

namespace JulOS.Application.Secrets;

/// <summary>Reasons secret-reference management can refuse or fail a request.</summary>
public enum SecretReferenceFailureReason
{
    /// <summary>The submitted metadata or value is invalid.</summary>
    Invalid = 1,

    /// <summary>The requested reference does not exist.</summary>
    NotFound = 2,

    /// <summary>The reference no longer contains encrypted value material.</summary>
    Deleted = 3,

    /// <summary>The operation is not authorized to lease the reference.</summary>
    LeaseDenied = 4,

    /// <summary>Required key material is unavailable or ciphertext authentication failed.</summary>
    Unavailable = 5,

    /// <summary>The in-memory lease passed its expiry.</summary>
    LeaseExpired = 6,
}

/// <summary>A typed refusal that never includes submitted secret material.</summary>
public sealed class SecretReferenceFailureException : Exception
{
    /// <summary>Creates one safe secret-reference failure.</summary>
    public SecretReferenceFailureException(SecretReferenceFailureReason reason)
        : base(MessageFor(reason))
    {
        this.Reason = reason;
    }

    /// <summary>Creates one safe failure while retaining an internal cause.</summary>
    public SecretReferenceFailureException(
        SecretReferenceFailureReason reason,
        Exception innerException)
        : base(MessageFor(reason), innerException)
    {
        this.Reason = reason;
    }

    /// <summary>The stable refusal reason.</summary>
    public SecretReferenceFailureReason Reason { get; }

    /// <summary>The public machine-readable code.</summary>
    public string Code => this.Reason switch
    {
        SecretReferenceFailureReason.Invalid => SecretReferenceErrorCodes.Invalid,
        SecretReferenceFailureReason.NotFound => SecretReferenceErrorCodes.NotFound,
        SecretReferenceFailureReason.Deleted => SecretReferenceErrorCodes.Deleted,
        SecretReferenceFailureReason.LeaseDenied => SecretReferenceErrorCodes.LeaseDenied,
        SecretReferenceFailureReason.Unavailable => SecretReferenceErrorCodes.Unavailable,
        SecretReferenceFailureReason.LeaseExpired => SecretReferenceErrorCodes.LeaseExpired,
        _ => throw new InvalidOperationException("Unknown secret-reference failure."),
    };

    private static string MessageFor(SecretReferenceFailureReason reason) => reason switch
    {
        SecretReferenceFailureReason.Invalid => "The secret-reference representation is invalid.",
        SecretReferenceFailureReason.NotFound => "The secret reference does not exist.",
        SecretReferenceFailureReason.Deleted => "The secret reference no longer contains a value.",
        SecretReferenceFailureReason.LeaseDenied => "The operation cannot lease this secret reference.",
        SecretReferenceFailureReason.Unavailable => "The secret reference is temporarily unavailable.",
        SecretReferenceFailureReason.LeaseExpired => "The secret lease has expired.",
        _ => "Secret-reference management failed.",
    };
}
