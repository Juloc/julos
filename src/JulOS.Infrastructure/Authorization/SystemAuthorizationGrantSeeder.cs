using JulOS.Application.Authorization;
using JulOS.Domain.Permissions;
using JulOS.Infrastructure.Persistence.Core;

using Microsoft.EntityFrameworkCore;

namespace JulOS.Infrastructure.Authorization;

/// <summary>Creates the explicit platform grants owned by the administrator role.</summary>
internal static class SystemAuthorizationGrantSeeder
{
    internal static async Task EnsureAdministratorPermissionsAsync(
        CoreDbContext context,
        Guid administratorRoleId,
        Guid grantedByUserId,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(timeProvider);

        var existing = await context.PermissionAssignments
            .Where(row => row.SubjectKind == PermissionSubjectKind.Role
                && row.SubjectId == administratorRoleId
                && row.ScopeKind == PermissionScopeKind.Global)
            .Select(row => row.Permission)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var existingNames = existing.ToHashSet(StringComparer.Ordinal);
        var subject = new PermissionSubject(
            PermissionSubjectKind.Role,
            new PermissionSubjectId(administratorRoleId));

        foreach (var permission in AuthorizationPermissionCatalog.InitialAdministratorPermissions)
        {
            if (existingNames.Contains(permission.Value))
            {
                continue;
            }

            var assignment = PermissionAssignment.Grant(
                new PermissionAssignmentId(Guid.CreateVersion7(timeProvider.GetUtcNow())),
                subject,
                permission,
                PermissionScope.Global,
                timeProvider);

            context.PermissionAssignments.Add(
                PermissionAssignmentRow.FromDomain(assignment, grantedByUserId));
        }
    }
}
