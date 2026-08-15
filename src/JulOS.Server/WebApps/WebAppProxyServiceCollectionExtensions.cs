using JulOS.Infrastructure.WebApps;

using Microsoft.Extensions.DependencyInjection;

namespace JulOS.Server.WebApps;

/// <summary>Registers and wires the local web-application reverse proxy.</summary>
internal static class WebAppProxyServiceCollectionExtensions
{
    /// <summary>Reads the configured web-application targets and the upstream HTTP clients.</summary>
    internal static IServiceCollection AddJulOsWebAppProxy(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var options = WebAppProxyOptions.Read(configuration);
        services.AddSingleton(options);
        services.AddSingleton(WebAppTargetRegistry.Read(configuration));

        services
            .AddHttpClient(WebAppProxyMiddleware.HttpClientName)
            .ConfigurePrimaryHttpMessageHandler(() =>
                WebAppProxyMiddleware.CreateHttpHandler(options, requirePinnedAddresses: false));

        services
            .AddHttpClient(WebAppProxyMiddleware.DynamicHttpClientName)
            .ConfigurePrimaryHttpMessageHandler(() =>
                WebAppProxyMiddleware.CreateHttpHandler(options, requirePinnedAddresses: true));

        return services;
    }

    /// <summary>
    /// Inserts the proxy after authentication and before antiforgery and endpoint routing, so a
    /// matched target host is forwarded and every other request continues to the normal pipeline.
    /// </summary>
    internal static IApplicationBuilder UseJulOsWebAppProxy(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        return app.UseMiddleware<WebAppProxyMiddleware>();
    }
}
