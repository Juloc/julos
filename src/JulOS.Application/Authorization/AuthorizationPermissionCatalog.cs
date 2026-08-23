using JulOS.Contracts.Authorization;
using JulOS.Domain.Permissions;

namespace JulOS.Application.Authorization;

/// <summary>Permission values owned by Core and available before packages exist.</summary>
public static class AuthorizationPermissionCatalog
{
    /// <summary>The permissions the initial administrator role receives explicitly.</summary>
    public static IReadOnlyList<PermissionName> InitialAdministratorPermissions { get; } =
    [
        PermissionName.Parse(AuthorizationPermissionNames.SystemVersionRead),
        PermissionName.Parse(AuthorizationPermissionNames.AuthorizationRead),
        PermissionName.Parse(AuthorizationPermissionNames.AuthorizationManage),
        PermissionName.Parse(AuthorizationPermissionNames.OperationCreate),
        PermissionName.Parse(AuthorizationPermissionNames.OperationRead),
        PermissionName.Parse(AuthorizationPermissionNames.OperationCancel),
        PermissionName.Parse(AuthorizationPermissionNames.SecretRead),
        PermissionName.Parse(AuthorizationPermissionNames.SecretManage),
        PermissionName.Parse(AuthorizationPermissionNames.PackageRead),
        PermissionName.Parse(AuthorizationPermissionNames.PackageManage),
        PermissionName.Parse(AuthorizationPermissionNames.WebAppUse),
    ];
}
