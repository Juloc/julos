using JulOS.Domain.Permissions;

using Microsoft.AspNetCore.Authorization;

namespace JulOS.Server.Authorization;

/// <summary>Requires one exact permission on one target scope.</summary>
internal sealed class PermissionRequirement : IAuthorizationRequirement
{
    internal PermissionRequirement(PermissionName permission, PermissionScope target)
    {
        this.Permission = permission;
        this.Target = target;
    }

    internal PermissionName Permission { get; }

    internal PermissionScope Target { get; }
}
