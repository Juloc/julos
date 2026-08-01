namespace JulOS.Domain.Permissions;

/// <summary>
/// The kind of target a <see cref="PermissionScope"/> narrows access to.
/// </summary>
public enum PermissionScopeKind
{
    /// <summary>The whole installation. A global scope satisfies every narrower target scope.</summary>
    Global = 1,

    /// <summary>One specific package.</summary>
    Package = 2,

    /// <summary>One specific resource, such as one Agent, connection or external resource.</summary>
    Resource = 3,
}
