using JulOS.Contracts.Authorization;

namespace JulOS.Application.Authorization;

/// <summary>Reasons role or permission administration can refuse a request.</summary>
public enum AuthorizationAdministrationFailureReason
{
    /// <summary>The role representation is invalid.</summary>
    InvalidRole = 1,
    /// <summary>The requested role does not exist.</summary>
    RoleNotFound = 2,
    /// <summary>A system role cannot be changed or deleted.</summary>
    SystemRoleImmutable = 3,
    /// <summary>The requested user does not exist.</summary>
    UserNotFound = 4,
    /// <summary>The operation would remove the final administrator.</summary>
    LastAdministrator = 5,
    /// <summary>The permission assignment representation is invalid.</summary>
    InvalidAssignment = 6,
    /// <summary>The same permission assignment already exists.</summary>
    DuplicateAssignment = 7,
    /// <summary>The requested permission assignment does not exist.</summary>
    AssignmentNotFound = 8,
}

/// <summary>A safe, typed refusal from role and permission administration.</summary>
public sealed class AuthorizationAdministrationException : Exception
{
    /// <summary>Creates a refusal with one stable reason.</summary>
    public AuthorizationAdministrationException(AuthorizationAdministrationFailureReason reason)
        : base(MessageFor(reason))
    {
        this.Reason = reason;
    }

    /// <summary>Creates a refusal with one stable reason and internal cause.</summary>
    public AuthorizationAdministrationException(
        AuthorizationAdministrationFailureReason reason,
        Exception innerException)
        : base(MessageFor(reason), innerException)
    {
        this.Reason = reason;
    }

    /// <summary>The stable reason for the refusal.</summary>
    public AuthorizationAdministrationFailureReason Reason { get; }

    /// <summary>The public machine-readable code.</summary>
    public string Code => this.Reason switch
    {
        AuthorizationAdministrationFailureReason.InvalidRole => AuthorizationErrorCodes.InvalidRole,
        AuthorizationAdministrationFailureReason.RoleNotFound => AuthorizationErrorCodes.RoleNotFound,
        AuthorizationAdministrationFailureReason.SystemRoleImmutable => AuthorizationErrorCodes.SystemRoleImmutable,
        AuthorizationAdministrationFailureReason.UserNotFound => AuthorizationErrorCodes.UserNotFound,
        AuthorizationAdministrationFailureReason.LastAdministrator => AuthorizationErrorCodes.LastAdministrator,
        AuthorizationAdministrationFailureReason.InvalidAssignment => AuthorizationErrorCodes.InvalidAssignment,
        AuthorizationAdministrationFailureReason.DuplicateAssignment => AuthorizationErrorCodes.DuplicateAssignment,
        AuthorizationAdministrationFailureReason.AssignmentNotFound => AuthorizationErrorCodes.AssignmentNotFound,
        _ => throw new InvalidOperationException("Unknown authorization administration failure."),
    };

    private static string MessageFor(AuthorizationAdministrationFailureReason reason) => reason switch
    {
        AuthorizationAdministrationFailureReason.InvalidRole => "The role representation is invalid.",
        AuthorizationAdministrationFailureReason.RoleNotFound => "The role does not exist.",
        AuthorizationAdministrationFailureReason.SystemRoleImmutable => "A system role cannot be changed or removed.",
        AuthorizationAdministrationFailureReason.UserNotFound => "The user does not exist.",
        AuthorizationAdministrationFailureReason.LastAdministrator => "The last administrator cannot be removed.",
        AuthorizationAdministrationFailureReason.InvalidAssignment => "The permission assignment is invalid.",
        AuthorizationAdministrationFailureReason.DuplicateAssignment => "The permission assignment already exists.",
        AuthorizationAdministrationFailureReason.AssignmentNotFound => "The permission assignment does not exist.",
        _ => "Authorization administration failed.",
    };
}
