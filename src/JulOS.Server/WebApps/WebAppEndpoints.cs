using JulOS.Contracts.WebApps;
using JulOS.Infrastructure.WebApps;
using JulOS.Server.Authorization;

namespace JulOS.Server.WebApps;

/// <summary>Maps the authenticated discovery endpoint for local web-application targets.</summary>
internal static class WebAppEndpoints
{
    internal static IEndpointRouteBuilder MapJulOsWebApps(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapGet(
                "/api/v1/webapps",
                (WebAppTargetRegistry registry) => registry.ProxiedHosts()
                    .Select(host => new WebAppSummaryResponse(host))
                    .ToArray())
            .WithTags("WebApps")
            .RequireAuthorization(JulOsAuthorizationPolicies.WebAppUse);

        endpoints.MapGet(
                "/api/v1/webapps/proxy",
                (WebAppTargetRegistry registry) =>
                    new WebProxyConfigResponse(registry.DynamicEnabled, registry.DynamicProxyZone))
            .WithTags("WebApps")
            .RequireAuthorization(JulOsAuthorizationPolicies.WebAppUse);

        return endpoints;
    }
}
