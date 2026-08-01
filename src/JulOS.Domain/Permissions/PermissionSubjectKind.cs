namespace JulOS.Domain.Permissions;

/// <summary>
/// Whether a permission subject is one user directly or one role a user holds.
/// </summary>
public enum PermissionSubjectKind
{
    /// <summary>The subject is one specific user.</summary>
    User = 1,

    /// <summary>The subject is one role, granting the permission to every user holding that role.</summary>
    Role = 2,
}
