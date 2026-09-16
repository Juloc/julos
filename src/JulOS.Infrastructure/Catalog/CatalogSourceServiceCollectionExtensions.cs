using JulOS.Application.Catalog;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace JulOS.Infrastructure.Catalog;

/// <summary>Registers the adapters a catalog refresh reads sources through.</summary>
public static class CatalogSourceServiceCollectionExtensions
{
    /// <summary>The directory Git working copies are materialized below by default.</summary>
    private const string DefaultWorkingRoot = "/var/lib/julos/data/catalog-work";

    /// <summary>Adds the catalog source readers and the refresh hand-off.</summary>
    /// <param name="services">Service collection.</param>
    /// <param name="configuration">Deployment configuration.</param>
    /// <remarks>
    /// The readers are singletons because each owns a long-lived HTTP client or a working
    /// directory; the services that use them stay scoped to a request or an operation.
    /// </remarks>
    public static IServiceCollection AddJulOsCatalogSources(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var workingRoot = configuration["Catalog:WorkingRoot"]
            ?? Environment.GetEnvironmentVariable("JULOS_CATALOG_WORKING_ROOT")
            ?? DefaultWorkingRoot;

        // One dispatcher for the process: the queue is the hand-off between the request that
        // asks for a refresh and the worker that runs it.
        services.TryAddSingleton<ICatalogRefreshDispatcher, CatalogRefreshDispatcher>();
        services.AddSingleton<ICatalogSourceReader, LocalCatalogSourceReader>();
        services.AddSingleton<ICatalogSourceReader>(_ => new HttpsCatalogSourceReader(
            HttpsCatalogSourceReader.CreateClient()));
        services.AddSingleton<ICatalogSourceReader>(_ => new OciCatalogSourceReader(
            HttpsCatalogSourceReader.CreateClient()));
        services.AddSingleton<ICatalogSourceReader>(_ => new GitCatalogSourceReader(workingRoot));

        return services;
    }
}
