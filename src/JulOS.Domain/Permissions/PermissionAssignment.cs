namespace JulOS.Domain.Permissions;

/// <summary>
/// One granted permission: a subject holds one permission on one scope.
/// </summary>
/// <remarks>
/// An assignment is granted and later withdrawn as a whole record; nothing about a grant
/// changes in place, so it carries no concurrency revision. <see cref="PermissionEvaluator"/>
/// is the only place assignments are interpreted, and it interprets a set of them, never
/// one assignment in isolation.
/// </remarks>
public sealed class PermissionAssignment
{
    private PermissionAssignment(
        PermissionAssignmentId id,
        PermissionSubject subject,
        PermissionName permission,
        PermissionScope scope,
        DateTimeOffset grantedAtUtc)
    {
        this.Id = id;
        this.Subject = subject;
        this.Permission = permission;
        this.Scope = scope;
        this.GrantedAtUtc = grantedAtUtc;
    }

    /// <summary>The stable identity of this assignment record.</summary>
    public PermissionAssignmentId Id { get; }

    /// <summary>The user or role this permission is granted to.</summary>
    public PermissionSubject Subject { get; }

    /// <summary>The permission being granted.</summary>
    public PermissionName Permission { get; }

    /// <summary>The scope the permission is granted on.</summary>
    public PermissionScope Scope { get; }

    /// <summary>The moment this permission was granted.</summary>
    public DateTimeOffset GrantedAtUtc { get; }

    /// <summary>Grants one permission to one subject on one scope.</summary>
    /// <param name="id">The generated identity of the new assignment record.</param>
    /// <param name="subject">The user or role receiving the permission.</param>
    /// <param name="permission">The permission being granted.</param>
    /// <param name="scope">The scope the permission is granted on.</param>
    /// <param name="timeProvider">The clock the grant moment is recorded from.</param>
    /// <exception cref="ArgumentNullException"><paramref name="timeProvider"/> is <see langword="null"/>.</exception>
    public static PermissionAssignment Grant(
        PermissionAssignmentId id,
        PermissionSubject subject,
        PermissionName permission,
        PermissionScope scope,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);

        return new PermissionAssignment(id, subject, permission, scope, timeProvider.GetUtcNow());
    }
}
