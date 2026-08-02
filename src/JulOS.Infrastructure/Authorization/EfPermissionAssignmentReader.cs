using JulOS.Application.Authorization;
using JulOS.Domain.Packages;
using JulOS.Domain.Permissions;
using JulOS.Infrastructure.Persistence.Core;

using Microsoft.EntityFrameworkCore;

namespace JulOS.Infrastructure.Authorization;

/// <summary>Resolves direct and role-derived permission assignments from Core persistence.</summary>
public sealed class EfPermissionAssignmentReader : IPermissionAssignmentReader
{
    private readonly CoreDbContext context;

    /// <summary>Creates a permission reader backed by Core persistence.</summary>
    /// <param name="context">The Core database context.</param>
    public EfPermissionAssignmentReader(CoreDbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        this.context = context;
    }

    /// <inheritdoc />
    public async Task<PermissionEvaluationSet> ReadForUserAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var roleIds = await this.context.UserRoles
            .AsNoTracking()
            .Where(item => item.UserId == userId)
            .Select(item => item.RoleId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var rows = await this.context.PermissionAssignments
            .AsNoTracking()
            .Where(row => (row.SubjectKind == PermissionSubjectKind.User && row.SubjectId == userId)
                || (row.SubjectKind == PermissionSubjectKind.Role && roleIds.Contains(row.SubjectId)))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var subjects = new List<PermissionSubject>(roleIds.Count + 1)
        {
            new(PermissionSubjectKind.User, new PermissionSubjectId(userId)),
        };
        subjects.AddRange(roleIds.Select(roleId =>
            new PermissionSubject(PermissionSubjectKind.Role, new PermissionSubjectId(roleId))));

        var assignments = rows.Select(ToDomain).ToArray();
        return new PermissionEvaluationSet(subjects, assignments);
    }

    private static PermissionAssignment ToDomain(PermissionAssignmentRow row)
    {
        var scope = row.ScopeKind switch
        {
            PermissionScopeKind.Global => PermissionScope.Global,
            PermissionScopeKind.Package => PermissionScope.ForPackage(
                PackageId.Parse(row.ScopeId
                    ?? throw new InvalidOperationException("A package permission assignment has no scope identity."))),
            PermissionScopeKind.Resource => PermissionScope.ForResource(
                PermissionResourceId.Parse(row.ScopeId
                    ?? throw new InvalidOperationException("A resource permission assignment has no scope identity."))),
            _ => throw new InvalidOperationException("Unknown permission scope kind."),
        };

        return PermissionAssignment.Grant(
            new PermissionAssignmentId(row.Id),
            new PermissionSubject(row.SubjectKind, new PermissionSubjectId(row.SubjectId)),
            PermissionName.Parse(row.Permission),
            scope,
            new FixedTimeProvider(row.GrantedAtUtc));
    }

    private sealed class FixedTimeProvider(DateTimeOffset value) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => value;
    }
}
