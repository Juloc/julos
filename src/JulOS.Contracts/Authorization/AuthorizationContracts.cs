namespace JulOS.Contracts.Authorization;

/// <summary>Stable public permission names owned by the JulOS control plane.</summary>
public static class AuthorizationPermissionNames
{
    public const string SystemVersionRead = "core.system.version.read";
    public const string AuthorizationRead = "core.authorization.read";
    public const string AuthorizationManage = "core.authorization.manage";
    public const string OperationCreate = "core.operation.create";
    public const string OperationRead = "core.operation.read";
    public const string OperationCancel = "core.operation.cancel";
    public const string SecretRead = "core.secret.read";
    public const string SecretManage = "core.secret.manage";
    public const string PackageRead = "core.package.read";
    public const string PackageManage = "core.package.manage";
}

public static class AuthorizationSubjectTypes
{
    public const string User = "user";
    public const string Role = "role";
}

public static class AuthorizationScopeTypes
{
    public const string Global = "global";
    public const string Package = "package";
    public const string Resource = "resource";
}

public static class AuthorizationErrorCodes
{
    public const string InvalidRole = "authorization.role_invalid";
    public const string RoleNotFound = "authorization.role_not_found";
    public const string SystemRoleImmutable = "authorization.system_role_immutable";
    public const string UserNotFound = "authorization.user_not_found";
    public const string LastAdministrator = "authorization.last_administrator";
    public const string InvalidAssignment = "authorization.assignment_invalid";
    public const string DuplicateAssignment = "authorization.assignment_duplicate";
    public const string AssignmentNotFound = "authorization.assignment_not_found";
}

public sealed record AuthorizationRoleResponse(
    Guid RoleId,
    string Name,
    string Description,
    bool IsSystemRole,
    int Revision);

public sealed record CreateAuthorizationRoleRequest(
    string Name,
    string Description);

public sealed record UpdateAuthorizationRoleRequest(
    string Name,
    string Description,
    int Revision);

public sealed record AuthorizationRoleMemberResponse(
    Guid UserId,
    string UserName,
    string DisplayName);

public sealed record PermissionAssignmentResponse(
    Guid AssignmentId,
    string SubjectType,
    Guid SubjectId,
    string Permission,
    string ScopeType,
    string? ScopeId,
    DateTimeOffset GrantedAtUtc,
    Guid GrantedByUserId);

public sealed record GrantPermissionRequest(
    string SubjectType,
    Guid SubjectId,
    string Permission,
    string ScopeType,
    string? ScopeId);
