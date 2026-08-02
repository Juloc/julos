namespace JulOS.Contracts.Authorization;

/// <summary>Stable public permission names owned by the JulOS control plane.</summary>
public static class AuthorizationPermissionNames
{
    /// <summary>Read the running Core version.</summary>
    public const string SystemVersionRead = "core.system.version.read";

    /// <summary>Read roles, memberships and assignments.</summary>
    public const string AuthorizationRead = "core.authorization.read";

    /// <summary>Manage roles, memberships and assignments.</summary>
    public const string AuthorizationManage = "core.authorization.manage";

    /// <summary>Create operation resources.</summary>
    public const string OperationCreate = "core.operation.create";

    /// <summary>Read operation resources.</summary>
    public const string OperationRead = "core.operation.read";

    /// <summary>Request operation cancellation.</summary>
    public const string OperationCancel = "core.operation.cancel";

    /// <summary>Read secret-reference metadata.</summary>
    public const string SecretRead = "core.secret.read";

    /// <summary>Create, rotate and delete secret references.</summary>
    public const string SecretManage = "core.secret.manage";

    /// <summary>Read installed package state.</summary>
    public const string PackageRead = "core.package.read";

    /// <summary>Install, configure, update, enable, disable and remove packages.</summary>
    public const string PackageManage = "core.package.manage";
}

/// <summary>Stable authorization assignment subject types.</summary>
public static class AuthorizationSubjectTypes
{
    /// <summary>A local user subject.</summary>
    public const string User = "user";

    /// <summary>A local role subject.</summary>
    public const string Role = "role";
}

/// <summary>Stable permission assignment scope types.</summary>
public static class AuthorizationScopeTypes
{
    /// <summary>An installation-wide scope.</summary>
    public const string Global = "global";

    /// <summary>A package scope.</summary>
    public const string Package = "package";

    /// <summary>A specific resource scope.</summary>
    public const string Resource = "resource";
}

/// <summary>Stable authorization administration failure codes.</summary>
public static class AuthorizationErrorCodes
{
    /// <summary>The role definition is invalid.</summary>
    public const string InvalidRole = "authorization.role_invalid";

    /// <summary>The requested role does not exist.</summary>
    public const string RoleNotFound = "authorization.role_not_found";

    /// <summary>A protected system role cannot be changed as requested.</summary>
    public const string SystemRoleImmutable = "authorization.system_role_immutable";

    /// <summary>The requested user does not exist.</summary>
    public const string UserNotFound = "authorization.user_not_found";

    /// <summary>The mutation would remove the last administrator.</summary>
    public const string LastAdministrator = "authorization.last_administrator";

    /// <summary>The permission assignment is invalid.</summary>
    public const string InvalidAssignment = "authorization.assignment_invalid";

    /// <summary>The permission assignment already exists.</summary>
    public const string DuplicateAssignment = "authorization.assignment_duplicate";

    /// <summary>The permission assignment does not exist.</summary>
    public const string AssignmentNotFound = "authorization.assignment_not_found";
}

/// <summary>One authorization role.</summary>
/// <param name="RoleId">Role identity.</param>
/// <param name="Name">Role name.</param>
/// <param name="Description">Role description.</param>
/// <param name="IsSystemRole">Whether Core owns and protects the role.</param>
/// <param name="Revision">Optimistic concurrency revision.</param>
public sealed record AuthorizationRoleResponse(
    Guid RoleId,
    string Name,
    string Description,
    bool IsSystemRole,
    int Revision);

/// <summary>Creates a local authorization role.</summary>
/// <param name="Name">Role name.</param>
/// <param name="Description">Role description.</param>
public sealed record CreateAuthorizationRoleRequest(
    string Name,
    string Description);

/// <summary>Updates a local authorization role.</summary>
/// <param name="Name">New role name.</param>
/// <param name="Description">New role description.</param>
/// <param name="Revision">Expected role revision.</param>
public sealed record UpdateAuthorizationRoleRequest(
    string Name,
    string Description,
    int Revision);

/// <summary>One user belonging to a role.</summary>
/// <param name="UserId">User identity.</param>
/// <param name="UserName">Login name.</param>
/// <param name="DisplayName">Display name.</param>
public sealed record AuthorizationRoleMemberResponse(
    Guid UserId,
    string UserName,
    string DisplayName);

/// <summary>One explicit scoped permission assignment.</summary>
/// <param name="AssignmentId">Assignment identity.</param>
/// <param name="SubjectType">User or role subject type.</param>
/// <param name="SubjectId">Subject identity.</param>
/// <param name="Permission">Permission name.</param>
/// <param name="ScopeType">Assignment scope type.</param>
/// <param name="ScopeId">Scope identity when required.</param>
/// <param name="GrantedAtUtc">Grant time.</param>
/// <param name="GrantedByUserId">Granting user identity.</param>
public sealed record PermissionAssignmentResponse(
    Guid AssignmentId,
    string SubjectType,
    Guid SubjectId,
    string Permission,
    string ScopeType,
    string? ScopeId,
    DateTimeOffset GrantedAtUtc,
    Guid GrantedByUserId);

/// <summary>Creates one scoped permission assignment.</summary>
/// <param name="SubjectType">User or role subject type.</param>
/// <param name="SubjectId">Subject identity.</param>
/// <param name="Permission">Permission name.</param>
/// <param name="ScopeType">Assignment scope type.</param>
/// <param name="ScopeId">Scope identity when required.</param>
public sealed record GrantPermissionRequest(
    string SubjectType,
    Guid SubjectId,
    string Permission,
    string ScopeType,
    string? ScopeId);
