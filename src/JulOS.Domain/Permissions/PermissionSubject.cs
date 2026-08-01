namespace JulOS.Domain.Permissions;

/// <summary>
/// Identifies who a permission is assigned to: one user directly, or one role a user holds.
/// </summary>
/// <remarks>
/// A role subject is not expanded into its member users here. Domain evaluates exactly
/// the assignments it is given; resolving which roles a user currently holds is an
/// application-service concern that happens before evaluation is called.
/// </remarks>
/// <param name="Kind">Whether the subject is a user or a role.</param>
/// <param name="Id">The generated identity of the user or role.</param>
public readonly record struct PermissionSubject(PermissionSubjectKind Kind, PermissionSubjectId Id)
{
    /// <summary>Whether the subject is a user or a role.</summary>
    public PermissionSubjectKind Kind { get; } = Kind;

    /// <summary>The generated identity of the user or role.</summary>
    public PermissionSubjectId Id { get; } = Id;
}
