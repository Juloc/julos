using JulOS.Domain.Permissions;

namespace JulOS.Application.Authorization;

/// <summary>One role independent of ASP.NET Core Identity transport types.</summary>
public sealed record AuthorizationRole(
    Guid Id,
    string Name,
    string Description,
    bool IsSystemRole,
    int Revision);

/// <summary>One role member independent of the identity-store implementation.</summary>
public sealed record AuthorizationRoleMember(
    Guid UserId,
    string UserName,
    string DisplayName);

/// <summary>One stored permission grant including its audit ownership.</summary>
public sealed record StoredPermissionAssignment(
    PermissionAssignment Assignment,
    Guid GrantedByUserId);

/// <summary>The subjects and assignments needed for one authorization decision.</summary>
public sealed record PermissionEvaluationSet(
    IReadOnlyCollection<PermissionSubject> Subjects,
    IReadOnlyCollection<PermissionAssignment> Assignments);

/// <summary>Reads the permission state for one authenticated user.</summary>
public interface IPermissionAssignmentReader
{
    /// <summary>Loads direct and role-derived assignments for the user.</summary>
    Task<PermissionEvaluationSet> ReadForUserAsync(
        Guid userId,
        CancellationToken cancellationToken = default);
}

/// <summary>Administers roles, memberships and explicit permission assignments.</summary>
public interface IAuthorizationAdministration
{
    /// <summary>Lists all roles in stable name order.</summary>
    Task<IReadOnlyList<AuthorizationRole>> ListRolesAsync(CancellationToken cancellationToken = default);

    /// <summary>Creates one custom role.</summary>
    Task<AuthorizationRole> CreateRoleAsync(
        string name,
        string description,
        CancellationToken cancellationToken = default);

    /// <summary>Changes one custom role when its revision is current.</summary>
    Task<AuthorizationRole> UpdateRoleAsync(
        Guid roleId,
        string name,
        string description,
        int revision,
        CancellationToken cancellationToken = default);

    /// <summary>Deletes one custom role when its revision is current.</summary>
    Task DeleteRoleAsync(
        Guid roleId,
        int revision,
        CancellationToken cancellationToken = default);

    /// <summary>Lists users currently assigned to one role.</summary>
    Task<IReadOnlyList<AuthorizationRoleMember>> ListRoleMembersAsync(
        Guid roleId,
        CancellationToken cancellationToken = default);

    /// <summary>Adds one existing user to one existing role.</summary>
    Task AddRoleMemberAsync(
        Guid roleId,
        Guid userId,
        CancellationToken cancellationToken = default);

    /// <summary>Removes one user from one role without removing the last administrator.</summary>
    Task RemoveRoleMemberAsync(
        Guid roleId,
        Guid userId,
        CancellationToken cancellationToken = default);

    /// <summary>Lists every explicit permission assignment.</summary>
    Task<IReadOnlyList<StoredPermissionAssignment>> ListPermissionAssignmentsAsync(
        CancellationToken cancellationToken = default);

    /// <summary>Creates one explicit permission assignment.</summary>
    Task<StoredPermissionAssignment> GrantPermissionAsync(
        PermissionSubject subject,
        PermissionName permission,
        PermissionScope scope,
        Guid grantedByUserId,
        CancellationToken cancellationToken = default);

    /// <summary>Removes one explicit permission assignment.</summary>
    Task RevokePermissionAsync(
        PermissionAssignmentId assignmentId,
        CancellationToken cancellationToken = default);
}
