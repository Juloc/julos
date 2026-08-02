namespace JulOS.Contracts.Authorization;

/// <summary>Stable public permission names owned by the JulOS control plane.</summary>
public static class AuthorizationPermissionNames
{
    /// <summary>Reads the running control-plane version.</summary>
    public const string SystemVersionRead = "core.system.version.read";

    /// <summary>Reads roles, memberships and permission assignments.</summary>
    public const string AuthorizationRead = "core.authorization.read";

    /// <summary>Creates, changes and removes roles, memberships and permission assignments.</summary>
    public const string AuthorizationManage = "core.authorization.manage";
}

/// <summary>Stable subject names used by the authorization API.</summary>
public static class AuthorizationSubjectTypes
{
    /// <summary>One user receives the grant directly.</summary>
    public const string User = "user";

    /// <summary>Every member of one role receives the grant.</summary>
    public const string Role = "role";
}

/// <summary>Stable scope names used by the authorization API.</summary>
public static class AuthorizationScopeTypes
{
    /// <summary>The whole JulOS installation.</summary>
    public const string Global = "global";

    /// <summary>Exactly one package identity.</summary>
    public const string Package = "package";

    /// <summary>Exactly one opaque resource identity.</summary>
    public const string Resource = "resource";
}

/// <summary>Stable failures owned by role and permission administration.</summary>
public static class AuthorizationErrorCodes
{
    /// <summary>The submitted role representation is invalid.</summary>
    public const string InvalidRole = "authorization.role_invalid";

    /// <summary>The requested role does not exist.</summary>
    public const string RoleNotFound = "authorization.role_not_found";

    /// <summary>A system role cannot be renamed or removed.</summary>
    public const string SystemRoleImmutable = "authorization.system_role_immutable";

    /// <summary>The requested account does not exist.</summary>
    public const string UserNotFound = "authorization.user_not_found";

    /// <summary>The last administrator cannot be removed from the administrator role.</summary>
    public const string LastAdministrator = "authorization.last_administrator";

    /// <summary>The submitted permission assignment is invalid.</summary>
    public const string InvalidAssignment = "authorization.assignment_invalid";

    /// <summary>The same permission assignment already exists.</summary>
    public const string DuplicateAssignment = "authorization.assignment_duplicate";

    /// <summary>The requested permission assignment does not exist.</summary>
    public const string AssignmentNotFound = "authorization.assignment_not_found";
}

/// <summary>A role visible through the authorization API.</summary>
/// <param name="RoleId">The stable role identifier.</param>
/// <param name="Name">The unique role name.</param>
/// <param name="Description">The operator-facing purpose of the role.</param>
/// <param name="IsSystemRole">Whether the role is part of the immutable platform contract.</param>
/// <param name="Revision">The optimistic-concurrency revision.</param>
public sealed record AuthorizationRoleResponse(
    Guid RoleId,
    string Name,
    string Description,
    bool IsSystemRole,
    int Revision);

/// <summary>Creates one custom role.</summary>
/// <param name="Name">The unique role name.</param>
/// <param name="Description">The operator-facing purpose of the role.</param>
public sealed record CreateAuthorizationRoleRequest(
    string Name,
    string Description);

/// <summary>Changes one custom role.</summary>
/// <param name="Name">The unique role name.</param>
/// <param name="Description">The operator-facing purpose of the role.</param>
/// <param name="Revision">The revision read by the caller.</param>
public sealed record UpdateAuthorizationRoleRequest(
    string Name,
    string Description,
    int Revision);

/// <summary>One user holding one role.</summary>
/// <param name="UserId">The stable user identifier.</param>
/// <param name="UserName">The local sign-in name.</param>
/// <param name="DisplayName">The name shown in the interface.</param>
public sealed record AuthorizationRoleMemberResponse(
    Guid UserId,
    string UserName,
    string DisplayName);

/// <summary>One persisted permission assignment.</summary>
/// <param name="AssignmentId">The stable assignment identifier.</param>
/// <param name="SubjectType">Whether the subject is a user or role.</param>
/// <param name="SubjectId">The user or role identifier.</param>
/// <param name="Permission">The exact permission name.</param>
/// <param name="ScopeType">The assignment scope kind.</param>
/// <param name="ScopeId">The package or resource identity for a narrow scope.</param>
/// <param name="GrantedAtUtc">When the grant was created.</param>
/// <param name="GrantedByUserId">The administrator who created the grant.</param>
public sealed record PermissionAssignmentResponse(
    Guid AssignmentId,
    string SubjectType,
    Guid SubjectId,
    string Permission,
    string ScopeType,
    string? ScopeId,
    DateTimeOffset GrantedAtUtc,
    Guid GrantedByUserId);

/// <summary>Creates one explicit permission assignment.</summary>
/// <param name="SubjectType">Either <c>user</c> or <c>role</c>.</param>
/// <param name="SubjectId">The existing user or role identifier.</param>
/// <param name="Permission">The exact dotted permission name.</param>
/// <param name="ScopeType">Either <c>global</c>, <c>package</c> or <c>resource</c>.</param>
/// <param name="ScopeId">Required for package and resource scopes; absent for global.</param>
public sealed record GrantPermissionRequest(
    string SubjectType,
    Guid SubjectId,
    string Permission,
    string ScopeType,
    string? ScopeId);
