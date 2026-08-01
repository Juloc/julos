using JulOS.Domain.Packages;

namespace JulOS.Domain.Permissions;

/// <summary>
/// The target a permission assignment applies to, or the target a permission is being
/// checked against.
/// </summary>
/// <remarks>
/// <see cref="Global"/> is the whole installation and satisfies every other scope. A
/// <see cref="PermissionScopeKind.Package"/> or <see cref="PermissionScopeKind.Resource"/>
/// scope satisfies only the exact same kind and identity: a package-scoped assignment
/// grants nothing outside that one package, and a resource-scoped assignment grants
/// nothing outside that one resource. Nothing here widens a narrow grant.
/// </remarks>
public readonly record struct PermissionScope
{
    private PermissionScope(PermissionScopeKind kind, string? scopeId)
    {
        this.Kind = kind;
        this.ScopeId = scopeId;
    }

    /// <summary>The kind of target this scope refers to.</summary>
    public PermissionScopeKind Kind { get; }

    /// <summary>
    /// The identity narrowing the scope, or <see langword="null"/> when <see cref="Kind"/>
    /// is <see cref="PermissionScopeKind.Global"/>.
    /// </summary>
    public string? ScopeId { get; }

    /// <summary>The scope covering the whole installation.</summary>
    public static PermissionScope Global { get; } = new(PermissionScopeKind.Global, scopeId: null);

    /// <summary>Creates the scope covering exactly one package.</summary>
    /// <param name="packageId">The published identity of the package this scope covers.</param>
    public static PermissionScope ForPackage(PackageId packageId) =>
        new(PermissionScopeKind.Package, packageId.Value);

    /// <summary>Creates the scope covering exactly one resource.</summary>
    /// <param name="resourceId">The identity of the resource this scope covers.</param>
    public static PermissionScope ForResource(PermissionResourceId resourceId) =>
        new(PermissionScopeKind.Resource, resourceId.Value);

    /// <summary>
    /// Returns whether a permission granted on this scope reaches <paramref name="target"/>.
    /// </summary>
    /// <param name="target">The scope the caller is asking about.</param>
    public bool Permits(PermissionScope target) =>
        this.Kind == PermissionScopeKind.Global
        || (this.Kind == target.Kind && string.Equals(this.ScopeId, target.ScopeId, StringComparison.Ordinal));
}
