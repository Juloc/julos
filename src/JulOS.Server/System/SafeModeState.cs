namespace JulOS.Server.System;

internal sealed record SafeModeState(bool Enabled, string Source)
{
    internal static SafeModeState Read(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var configured = configuration["SafeMode:Enabled"];
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return bool.TryParse(configured, out var enabled)
                ? new SafeModeState(enabled, "configuration")
                : throw new InvalidOperationException("SafeMode:Enabled must be true or false.");
        }

        var environment = Environment.GetEnvironmentVariable("JULOS_SAFE_MODE");
        return string.IsNullOrWhiteSpace(environment)
            ? new SafeModeState(false, "default")
            : bool.TryParse(environment, out var enabled)
                ? new SafeModeState(enabled, "environment")
                : throw new InvalidOperationException("JULOS_SAFE_MODE must be true or false.");
    }
}

internal static class SafeModeEndpoints
{
    internal static IEndpointRouteBuilder MapJulOsSafeMode(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        endpoints.MapGet(
            "/api/v1/system/safe-mode",
            (SafeModeState state) => TypedResults.Ok(new SafeModeResponse(state.Enabled, state.Source)))
            .RequireAuthorization(Authorization.JulOsAuthorizationPolicies.SystemVersionRead);
        return endpoints;
    }
}

internal sealed record SafeModeResponse(bool Enabled, string Source);
