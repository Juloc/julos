using JulOS.Application.Packages;
using JulOS.Application.Remote;
using JulOS.Application.Secrets;
using JulOS.Infrastructure.Persistence.Core;
using JulOS.Infrastructure.Remote;
using JulOS.PackageSdk;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace JulOS.Infrastructure.Packages;

/// <summary>Registers package verification, storage, lifecycle, update and capability services.</summary>
public static class PackageManagementServiceCollectionExtensions
{
    /// <summary>Adds the complete JulOS package-management Infrastructure implementation.</summary>
    public static IServiceCollection AddJulOsPackageManagement(
        this IServiceCollection services,
        IConfiguration configuration,
        CoreDatabaseConfiguration coreDatabase)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(coreDatabase);

        var packageRoot = configuration["Packages:Root"]
            ?? Environment.GetEnvironmentVariable("JULOS_PACKAGE_ROOT")
            ?? "/var/lib/julos/packages";
        var officialCatalogRoot = configuration["Packages:OfficialCatalogRoot"]
            ?? Environment.GetEnvironmentVariable("JULOS_OFFICIAL_PACKAGE_CATALOG_ROOT")
            ?? "/application/official-packages";
        var serverEndpointValue = configuration["Packages:ServerEndpoint"]
            ?? Environment.GetEnvironmentVariable("JULOS_PACKAGE_SERVER_ENDPOINT")
            ?? "http://127.0.0.1:8080";
        if (!Uri.TryCreate(serverEndpointValue, UriKind.Absolute, out var serverEndpoint)
            || serverEndpoint.Scheme is not ("http" or "https"))
        {
            throw new InvalidOperationException("Packages:ServerEndpoint must be an absolute HTTP or HTTPS URI.");
        }

        var officialCatalog = OfficialPackageCatalogIndex.Load(officialCatalogRoot);
        var publishers = configuration.GetSection("Packages:TrustedPublishers")
            .Get<TrustedPublisherConfiguration[]>()
            ?? [];
        var trustedPublishers = publishers.Select(publisher =>
            new TrustedPackagePublisher(
                publisher.PublisherId,
                publisher.KeyId,
                publisher.PublicKeyPem))
            .ToList();
        if (officialCatalog.TrustedPublisher is not null)
        {
            trustedPublishers.Add(officialCatalog.TrustedPublisher);
        }

        services.AddSingleton(officialCatalog);
        services.AddSingleton(new PackageArtifactVerifier(trustedPublishers));
        services.AddSingleton(new PostgresPackageStorageProvisioner(
            coreDatabase,
            packageRoot));
        services.AddSingleton(_ => new ProcessPackageWorkerSupervisor(
            packageRoot,
            serverEndpoint,
            coreDatabase.Provider,
            coreDatabase.ConnectionString));
        services.AddSingleton<IPackageWorkerSupervisor>(provider =>
            provider.GetRequiredService<ProcessPackageWorkerSupervisor>());
        services.AddSingleton<IPackageWorkerCommandDispatcher>(provider =>
            provider.GetRequiredService<ProcessPackageWorkerSupervisor>());
        services.AddSingleton<InteractiveSessionCoordinator>();
        services.AddScoped(provider => new InteractiveSessionCapabilityProvider(
            provider.GetRequiredService<CoreDbContext>(),
            provider.GetRequiredService<IPackageWorkerCommandDispatcher>(),
            provider.GetRequiredService<InteractiveSessionCoordinator>(),
            provider.GetRequiredService<IRemoteRuntimeManager>(),
            provider.GetRequiredService<IRemoteRuntimePolicy>(),
            provider.GetRequiredService<IRemoteSessionService>(),
            provider.GetRequiredService<IRemoteSessionProvisioner>(),
            provider.GetRequiredService<IRemoteSessionLifecycleService>(),
            provider.GetRequiredService<ISecretReferenceService>(),
            provider.GetRequiredService<TimeProvider>()));
        services.AddScoped<IInteractiveSessionCleanupService>(provider => new InteractiveSessionCleanupService(
            provider.GetRequiredService<CoreDbContext>(),
            provider.GetRequiredService<IRemoteRuntimeManager>(),
            provider.GetRequiredService<ISecretReferenceService>()));
        services.AddScoped<IPackageManagementService>(provider => new PostgresPackageManagementService(
            provider.GetRequiredService<CoreDbContext>(),
            provider.GetRequiredService<PackageArtifactVerifier>(),
            provider.GetRequiredService<PostgresPackageStorageProvisioner>(),
            provider.GetRequiredService<IPackageWorkerSupervisor>(),
            packageRoot,
            provider.GetRequiredService<TimeProvider>()));
        services.AddScoped<IDesktopApplicationCatalog>(provider => new PostgresDesktopApplicationCatalog(
            provider.GetRequiredService<CoreDbContext>(),
            packageRoot,
            provider.GetRequiredService<TimeProvider>()));
        services.AddScoped<IPackageUpdateService>(provider => new PostgresPackageUpdateService(
            provider.GetRequiredService<CoreDbContext>(),
            provider.GetRequiredService<PackageArtifactVerifier>(),
            provider.GetRequiredService<IPackageWorkerSupervisor>(),
            packageRoot,
            provider.GetRequiredService<TimeProvider>()));
        services.AddScoped<IOfficialPackageStoreService, OfficialPackageStoreService>();
        services.AddScoped<PackageCapabilityAuthorizer>(provider => new PackageCapabilityAuthorizer(
            provider.GetRequiredService<CoreDbContext>(),
            packageRoot));
        services.AddScoped<HostMetricsCapabilityProvider>();
        services.AddScoped<RemoteSessionCapabilityProvider>();
        services.AddScoped(provider => new InteractiveProfilesCapabilityProvider(
            provider.GetRequiredService<IPackageWorkerCommandDispatcher>()));
        services.AddScoped<CapabilityBroker>(provider =>
        {
            var broker = new CapabilityBroker(
                provider.GetRequiredService<JulOS.Application.Auditing.IAuditService>(),
                provider.GetRequiredService<TimeProvider>());
            var hostMetrics = provider.GetRequiredService<HostMetricsCapabilityProvider>();
            broker.Register(hostMetrics.Descriptor.ProviderPackageId, hostMetrics);
            var remote = provider.GetRequiredService<RemoteSessionCapabilityProvider>();
            broker.Register(remote.Descriptor.ProviderPackageId, remote);
            var interactive = provider.GetRequiredService<InteractiveSessionCapabilityProvider>();
            broker.Register(interactive.Descriptor.ProviderPackageId, interactive);
            var interactiveProfiles = provider.GetRequiredService<InteractiveProfilesCapabilityProvider>();
            broker.Register(interactiveProfiles.Descriptor.ProviderPackageId, interactiveProfiles);
            return broker;
        });
        services.AddScoped<ICapabilityClient>(
            provider => provider.GetRequiredService<CapabilityBroker>());
        return services;
    }

    /// <summary>Adds PostgreSQL package management for compatibility with existing callers.</summary>
    public static IServiceCollection AddJulOsPackageManagement(
        this IServiceCollection services,
        IConfiguration configuration,
        string coreDatabaseConnectionString) =>
        services.AddJulOsPackageManagement(
            configuration,
            new CoreDatabaseConfiguration(
                CoreDatabaseProvider.PostgreSql,
                coreDatabaseConnectionString));

    private sealed record TrustedPublisherConfiguration(
        string PublisherId,
        string KeyId,
        string PublicKeyPem);
}
