using JulOS.Application.Authorization;
using JulOS.Application.Concurrency;
using JulOS.Domain.Permissions;
using JulOS.Infrastructure.Authentication;
using JulOS.Infrastructure.Persistence.Core;

using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace JulOS.Infrastructure.Authorization;

/// <summary>Administers roles and permission assignments through Identity and Core persistence.</summary>
public sealed class IdentityAuthorizationAdministration : IAuthorizationAdministration
{
    private readonly CoreDbContext context;
    private readonly RoleManager<LocalRole> roleManager;
    private readonly UserManager<LocalUser> userManager;
    private readonly TimeProvider timeProvider;

    /// <summary>Creates the Identity-backed authorization administrator.</summary>
    /// <param name="context">The Core database context.</param>
    /// <param name="roleManager">The local Identity role manager.</param>
    /// <param name="userManager">The local Identity user manager.</param>
    /// <param name="timeProvider">The clock used for identifiers and grant timestamps.</param>
    public IdentityAuthorizationAdministration(
        CoreDbContext context,
        RoleManager<LocalRole> roleManager,
        UserManager<LocalUser> userManager,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(roleManager);
        ArgumentNullException.ThrowIfNull(userManager);
        ArgumentNullException.ThrowIfNull(timeProvider);

        this.context = context;
        this.roleManager = roleManager;
        this.userManager = userManager;
        this.timeProvider = timeProvider;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<AuthorizationRole>> ListRolesAsync(
        CancellationToken cancellationToken = default)
    {
        return await this.context.Roles
            .AsNoTracking()
            .OrderBy(role => role.NormalizedName)
            .Select(role => new AuthorizationRole(
                role.Id,
                role.Name ?? string.Empty,
                role.Description,
                role.IsSystemRole,
                role.Revision))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<AuthorizationRole> CreateRoleAsync(
        string name,
        string description,
        CancellationToken cancellationToken = default)
    {
        ValidateRole(name, description);

        if (await this.roleManager.RoleExistsAsync(name).ConfigureAwait(false))
        {
            throw new AuthorizationAdministrationException(
                AuthorizationAdministrationFailureReason.InvalidRole);
        }

        var role = new LocalRole
        {
            Id = Guid.CreateVersion7(this.timeProvider.GetUtcNow()),
            Name = name,
            Description = description,
            IsSystemRole = false,
            Revision = 1,
        };

        EnsureSucceeded(
            await this.roleManager.CreateAsync(role).ConfigureAwait(false),
            AuthorizationAdministrationFailureReason.InvalidRole);

        return ToRole(role);
    }

    /// <inheritdoc />
    public async Task<AuthorizationRole> UpdateRoleAsync(
        Guid roleId,
        string name,
        string description,
        int revision,
        CancellationToken cancellationToken = default)
    {
        ValidateRole(name, description);
        var role = await this.FindRoleAsync(roleId).ConfigureAwait(false);
        EnsureMutable(role);
        EnsureRevision(role, revision);

        role.Name = name;
        role.Description = description;

        var result = await this.roleManager.UpdateAsync(role).ConfigureAwait(false);
        if (!result.Succeeded && result.Errors.Any(error =>
            string.Equals(error.Code, nameof(IdentityErrorDescriber.ConcurrencyFailure), StringComparison.Ordinal)))
        {
            throw new ConcurrencyConflictException(role.Revision, new InvalidOperationException("The role changed concurrently."));
        }

        EnsureSucceeded(result, AuthorizationAdministrationFailureReason.InvalidRole);
        return ToRole(role);
    }

    /// <inheritdoc />
    public async Task DeleteRoleAsync(
        Guid roleId,
        int revision,
        CancellationToken cancellationToken = default)
    {
        var role = await this.FindRoleAsync(roleId).ConfigureAwait(false);
        EnsureMutable(role);
        EnsureRevision(role, revision);

        var result = await this.roleManager.DeleteAsync(role).ConfigureAwait(false);
        if (!result.Succeeded && result.Errors.Any(error =>
            string.Equals(error.Code, nameof(IdentityErrorDescriber.ConcurrencyFailure), StringComparison.Ordinal)))
        {
            throw new ConcurrencyConflictException(role.Revision, new InvalidOperationException("The role changed concurrently."));
        }

        EnsureSucceeded(result, AuthorizationAdministrationFailureReason.InvalidRole);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<AuthorizationRoleMember>> ListRoleMembersAsync(
        Guid roleId,
        CancellationToken cancellationToken = default)
    {
        _ = await this.FindRoleAsync(roleId).ConfigureAwait(false);

        return await (
            from membership in this.context.UserRoles.AsNoTracking()
            join user in this.context.Users.AsNoTracking() on membership.UserId equals user.Id
            where membership.RoleId == roleId
            orderby user.NormalizedUserName
            select new AuthorizationRoleMember(
                user.Id,
                user.UserName ?? string.Empty,
                user.DisplayName))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task AddRoleMemberAsync(
        Guid roleId,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var role = await this.FindRoleAsync(roleId).ConfigureAwait(false);
        var user = await this.FindUserAsync(userId).ConfigureAwait(false);
        var roleName = RequireRoleName(role);

        if (await this.userManager.IsInRoleAsync(user, roleName).ConfigureAwait(false))
        {
            return;
        }

        EnsureSucceeded(
            await this.userManager.AddToRoleAsync(user, roleName).ConfigureAwait(false),
            AuthorizationAdministrationFailureReason.InvalidRole);
    }

    /// <inheritdoc />
    public async Task RemoveRoleMemberAsync(
        Guid roleId,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var role = await this.FindRoleAsync(roleId).ConfigureAwait(false);
        var user = await this.FindUserAsync(userId).ConfigureAwait(false);
        var roleName = RequireRoleName(role);

        if (!await this.userManager.IsInRoleAsync(user, roleName).ConfigureAwait(false))
        {
            return;
        }

        if (role.IsSystemRole
            && string.Equals(roleName, LocalIdentityNames.AdministratorRole, StringComparison.Ordinal)
            && await this.context.UserRoles.CountAsync(
                membership => membership.RoleId == role.Id,
                cancellationToken).ConfigureAwait(false) <= 1)
        {
            throw new AuthorizationAdministrationException(
                AuthorizationAdministrationFailureReason.LastAdministrator);
        }

        EnsureSucceeded(
            await this.userManager.RemoveFromRoleAsync(user, roleName).ConfigureAwait(false),
            AuthorizationAdministrationFailureReason.InvalidRole);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<StoredPermissionAssignment>> ListPermissionAssignmentsAsync(
        CancellationToken cancellationToken = default)
    {
        var rows = await this.context.PermissionAssignments
            .AsNoTracking()
            .OrderBy(row => row.Permission)
            .ThenBy(row => row.SubjectKind)
            .ThenBy(row => row.SubjectId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return rows.Select(ToStoredAssignment).ToArray();
    }

    /// <inheritdoc />
    public async Task<StoredPermissionAssignment> GrantPermissionAsync(
        PermissionSubject subject,
        PermissionName permission,
        PermissionScope scope,
        Guid grantedByUserId,
        CancellationToken cancellationToken = default)
    {
        await this.EnsureSubjectExistsAsync(subject, cancellationToken).ConfigureAwait(false);
        _ = await this.FindUserAsync(grantedByUserId).ConfigureAwait(false);

        var duplicate = await this.context.PermissionAssignments.AnyAsync(
            row => row.SubjectKind == subject.Kind
                && row.SubjectId == subject.Id.Value
                && row.Permission == permission.Value
                && row.ScopeKind == scope.Kind
                && row.ScopeId == scope.ScopeId,
            cancellationToken).ConfigureAwait(false);

        if (duplicate)
        {
            throw new AuthorizationAdministrationException(
                AuthorizationAdministrationFailureReason.DuplicateAssignment);
        }

        var assignment = PermissionAssignment.Grant(
            new PermissionAssignmentId(Guid.CreateVersion7(this.timeProvider.GetUtcNow())),
            subject,
            permission,
            scope,
            this.timeProvider);

        this.context.PermissionAssignments.Add(
            PermissionAssignmentRow.FromDomain(assignment, grantedByUserId));

        try
        {
            await this.context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException exception)
        {
            throw new AuthorizationAdministrationException(
                AuthorizationAdministrationFailureReason.DuplicateAssignment,
                exception);
        }

        return new StoredPermissionAssignment(assignment, grantedByUserId);
    }

    /// <inheritdoc />
    public async Task RevokePermissionAsync(
        PermissionAssignmentId assignmentId,
        CancellationToken cancellationToken = default)
    {
        var row = await this.context.PermissionAssignments
            .SingleOrDefaultAsync(item => item.Id == assignmentId.Value, cancellationToken)
            .ConfigureAwait(false);

        if (row is null)
        {
            throw new AuthorizationAdministrationException(
                AuthorizationAdministrationFailureReason.AssignmentNotFound);
        }

        this.context.PermissionAssignments.Remove(row);
        await this.context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<LocalRole> FindRoleAsync(Guid roleId)
    {
        return await this.roleManager.FindByIdAsync(roleId.ToString()).ConfigureAwait(false)
            ?? throw new AuthorizationAdministrationException(
                AuthorizationAdministrationFailureReason.RoleNotFound);
    }

    private async Task<LocalUser> FindUserAsync(Guid userId)
    {
        return await this.userManager.FindByIdAsync(userId.ToString()).ConfigureAwait(false)
            ?? throw new AuthorizationAdministrationException(
                AuthorizationAdministrationFailureReason.UserNotFound);
    }

    private async Task EnsureSubjectExistsAsync(
        PermissionSubject subject,
        CancellationToken cancellationToken)
    {
        var exists = subject.Kind switch
        {
            PermissionSubjectKind.User => await this.context.Users.AnyAsync(
                user => user.Id == subject.Id.Value,
                cancellationToken).ConfigureAwait(false),
            PermissionSubjectKind.Role => await this.context.Roles.AnyAsync(
                role => role.Id == subject.Id.Value,
                cancellationToken).ConfigureAwait(false),
            _ => false,
        };

        if (!exists)
        {
            throw new AuthorizationAdministrationException(
                subject.Kind == PermissionSubjectKind.User
                    ? AuthorizationAdministrationFailureReason.UserNotFound
                    : AuthorizationAdministrationFailureReason.RoleNotFound);
        }
    }

    private static StoredPermissionAssignment ToStoredAssignment(PermissionAssignmentRow row)
    {
        var scope = row.ScopeKind switch
        {
            PermissionScopeKind.Global => PermissionScope.Global,
            PermissionScopeKind.Package => PermissionScope.ForPackage(
                JulOS.Domain.Packages.PackageId.Parse(row.ScopeId
                    ?? throw new InvalidOperationException("A package permission assignment has no scope identity."))),
            PermissionScopeKind.Resource => PermissionScope.ForResource(
                PermissionResourceId.Parse(row.ScopeId
                    ?? throw new InvalidOperationException("A resource permission assignment has no scope identity."))),
            _ => throw new InvalidOperationException("Unknown permission scope kind."),
        };

        var assignment = PermissionAssignment.Grant(
            new PermissionAssignmentId(row.Id),
            new PermissionSubject(row.SubjectKind, new PermissionSubjectId(row.SubjectId)),
            PermissionName.Parse(row.Permission),
            scope,
            new FixedTimeProvider(row.GrantedAtUtc));

        return new StoredPermissionAssignment(assignment, row.GrantedByUserId);
    }

    private static AuthorizationRole ToRole(LocalRole role) => new(
        role.Id,
        RequireRoleName(role),
        role.Description,
        role.IsSystemRole,
        role.Revision);

    private static string RequireRoleName(LocalRole role) => role.Name
        ?? throw new InvalidOperationException("A persisted role has no name.");

    private static void EnsureMutable(LocalRole role)
    {
        if (role.IsSystemRole)
        {
            throw new AuthorizationAdministrationException(
                AuthorizationAdministrationFailureReason.SystemRoleImmutable);
        }
    }

    private static void EnsureRevision(LocalRole role, int revision)
    {
        if (revision < 1 || role.Revision != revision)
        {
            throw new ConcurrencyConflictException(
                role.Revision,
                new InvalidOperationException("The submitted role revision is stale."));
        }
    }

    private static void ValidateRole(string name, string description)
    {
        if (string.IsNullOrWhiteSpace(name)
            || name.Length is < 3 or > 128
            || !string.Equals(name, name.Trim(), StringComparison.Ordinal)
            || name.Any(char.IsControl)
            || string.IsNullOrWhiteSpace(description)
            || description.Length > 512
            || !string.Equals(description, description.Trim(), StringComparison.Ordinal)
            || description.Any(char.IsControl))
        {
            throw new AuthorizationAdministrationException(
                AuthorizationAdministrationFailureReason.InvalidRole);
        }
    }

    private static void EnsureSucceeded(
        IdentityResult result,
        AuthorizationAdministrationFailureReason failureReason)
    {
        if (!result.Succeeded)
        {
            throw new AuthorizationAdministrationException(failureReason);
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset value) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => value;
    }
}
