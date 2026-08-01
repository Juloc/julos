namespace JulOS.Domain.Permissions;

/// <summary>
/// Decides whether a set of permission assignments grants a permission on a target scope.
/// </summary>
/// <remarks>
/// This is a pure function of its arguments: it reads no ambient clock, configuration or
/// store, so the same assignments, subject, permission and target always produce the same
/// answer. An empty or non-matching assignment set denies by construction, because
/// <see cref="Grants"/> requires finding an explicit matching assignment and never assumes
/// one.
/// </remarks>
public static class PermissionEvaluator
{
    /// <summary>
    /// Returns whether <paramref name="assignments"/> grants <paramref name="permission"/> to
    /// <paramref name="subject"/> on <paramref name="target"/>.
    /// </summary>
    /// <param name="assignments">The assignments to evaluate. An empty collection grants nothing.</param>
    /// <param name="subject">The user or role asking to act.</param>
    /// <param name="permission">The permission the action requires.</param>
    /// <param name="target">The scope the action would apply to.</param>
    /// <exception cref="ArgumentNullException"><paramref name="assignments"/> is <see langword="null"/>.</exception>
    public static bool Grants(
        IReadOnlyCollection<PermissionAssignment> assignments,
        PermissionSubject subject,
        PermissionName permission,
        PermissionScope target)
    {
        ArgumentNullException.ThrowIfNull(assignments);

        foreach (var assignment in assignments)
        {
            if (assignment.Subject == subject
                && assignment.Permission == permission
                && assignment.Scope.Permits(target))
            {
                return true;
            }
        }

        return false;
    }
}
