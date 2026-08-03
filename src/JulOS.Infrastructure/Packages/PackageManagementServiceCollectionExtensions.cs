using JulOS.Application.Packages;
using JulOS.Infrastructure.Persistence.Core;
using JulOS.PackageSdk;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace JulOS.Infrastructure.Packages;

/// <summary>Registers package verification, storage, lifecycle, update and capability services.</summary>
public static class PackageManagementServiceCollectionExtensions
{
    /// <summary>Adds the complete JulOS package-management Infrastructure implementation.</summary>
    /// <param name="services">Dependency-injection service collection.</param>
    /// <param name="configuration">Package root and trusted-publisher configuration.</param>
    /// <param name="coreDatabaseConnectionString">Administrative Core PostgreSQL connection.</param>
    /// <returns>The same service collection.</returns>
    public static IServiceCollection AddJulOsPackageManagement(
        this IServiceCollection services,
        IConfiguration configuration,
        string coreDatabaseConnectionString)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(coreDatabaseConnectionString);

        var packageRoot = configuration["Packages:Root"]
            ?? Environment.GetEnvironmentVariable("JULOS_PACKAGE_ROOT")
            ?? "/var/lib/julos/packages";
        var serverEndpointValue = configuration["Packages:ServerEndpoint"]
            ?? Environment.GetEnvironmentVariable("JULOS_PACKAGE_SERVER_ENDPOINT")
            ?? "http://127.0.0.1:8080";
        if (!Uri.TryCreate(serverEndpointValue, UriKind.Absolute, out var serverEndpoint)
            || serverEndpoint.Scheme is not ("http" or "https"))
        {
            throw new InvalidOperationException("Packages:ServerEndpoint must be an absolute HTTP or HTTPS URI.");
        }

        var publishers = configuration.GetSection("Packages:TrustedPublishers")
            .Get<TrustedPublisherConfiguration[]>()
            ?? [];

        services.AddSingleton(new PackageArtifactVerifier(publishers.Select(publisher =>
            new TrustedPackagePublisher(
                publisher.PublisherId,
                publisher.KeyId,
                publisher.PublicKeyPem))));
        services.AddSingleton(new PostgresPackageStorageProvisioner(coreDatabaseConnectionString));
        services.AddSingleton<IPackageWorkerSupervisor>(new ProcessPackageWorkerSupervisor(
            packageRoot,
            serverEndpoint,
            coreDatabaseConnectionString));
        services.AddScoped<IPackageManagementService>(provider => new PostgresPackageManagementService(
            provider.GetRequiredService<CoreDbContext>(),
            provider.GetRequiredService<PackageArtifactVerifier>(),
            provider.GetRequiredService<PostgresPackageStorageProvisioner>(),
            provider.GetRequiredService<IPackageWorkerSupervisor>(),
            packageRoot,
            provider.GetRequiredService<TimeProvider>()));
        services.AddScoped<IPackageUpdateService>(provider => new PostgresPackageUpdateService(
            provider.GetRequiredService<CoreDbContext>(),
            provider.GetRequiredService<PackageArtifactVerifier>(),
            provider.GetRequiredService<IPackageWorkerSupervisor>(),
            packageRoot,
            provider.GetRequiredService<TimeProvider>()));
        services.AddScoped<PackageCapabilityAuthorizer>(provider => new PackageCapabilityAuthorizer(
            provider.GetRequiredService<CoreDbContext>(),
            packageRoot));
        services.AddScoped<HostMetricsCapabilityProvider>();
        services.AddScoped<CapabilityBroker>(provider =>
        {
            var broker = new CapabilityBroker(
                provider.GetRequiredService<JulOS.Application.Auditing.IAuditService>(),
                provider.GetRequiredService<TimeProvider>());
            var hostMetrics = provider.GetRequiredService<HostMetricsCapabilityProvider>();
            broker.Register(hostMetrics.Descriptor.ProviderPackageId, hostMetrics);
            return broker;
        });
        services.AddScoped<ICapabilityClient>(
            provider => provider.GetRequiredService<CapabilityBroker>());
        return services;
    }

    private sealed record TrustedPublisherConfiguration(
        string PublisherId,
        string KeyId,
        string PublicKeyPem);
}
