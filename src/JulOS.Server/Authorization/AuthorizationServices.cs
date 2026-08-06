using JulOS.Contracts.Authorization;
using JulOS.Domain.Permissions;

using Microsoft.AspNetCore.Authorization;

namespace JulOS.Server.Authorization;

/// <summary>Registers default-deny authorization and the initial Core permission policies.</summary>
internal static class AuthorizationServices
{
    internal static IServiceCollection AddJulOsAuthorization(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<IAuthorizationHandler, PermissionAuthorizationHandler>();
        services.AddAuthorization(options =>
        {
            options.FallbackPolicy = new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .Build();

            AddPermissionPolicy(
                options,
                JulOsAuthorizationPolicies.SystemVersionRead,
                AuthorizationPermissionNames.SystemVersionRead);
            AddPermissionPolicy(
                options,
                JulOsAuthorizationPolicies.AuthorizationRead,
                AuthorizationPermissionNames.AuthorizationRead);
            AddPermissionPolicy(
                options,
                JulOsAuthorizationPolicies.AuthorizationManage,
                AuthorizationPermissionNames.AuthorizationManage);
            AddPermissionPolicy(
                options,
                JulOsAuthorizationPolicies.OperationCreate,
                AuthorizationPermissionNames.OperationCreate);
            AddPermissionPolicy(
                options,
                JulOsAuthorizationPolicies.OperationRead,
                AuthorizationPermissionNames.OperationRead);
            AddPermissionPolicy(
                options,
                JulOsAuthorizationPolicies.OperationCancel,
                AuthorizationPermissionNames.OperationCancel);
            AddPermissionPolicy(
              options,
              JulOsAuthorizationPolicies.SecretRead,
              AuthorizationPermissionNames.SecretRead);
            AddPermissionPolicy(
              options,
              JulOsAuthorizationPolicies.SecretManage,
              AuthorizationPermissionNames.SecretManage);
        });

        return services;
    }

    private static void AddPermissionPolicy(
        AuthorizationOptions options,
        string policyName,
        string permissionName)
    {
        options.AddPolicy(
            policyName,
            policy => policy
                .RequireAuthenticatedUser()
                .AddRequirements(new PermissionRequirement(
                    PermissionName.Parse(permissionName),
                    PermissionScope.Global)));
    }
}
