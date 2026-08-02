using JulOS.Application.Packages;
using JulOS.Infrastructure.Persistence.Core;
using JulOS.PackageSdk;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace JulOS.Infrastructure.Packages;

public static class PackageManagementServiceCollectionExtensions
{
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
        var publishers = configuration.GetSection("Packages:TrustedPublishers")
            .Get<TrustedPublisherConfiguration[]>()
            ?? [];

        services.AddSingleton(new PackageArtifactVerifier(publishers.Select(publisher =>
            new TrustedPackagePublisher(
                publisher.PublisherId,
                publisher.KeyId,
                publisher.PublicKeyPem))));
        services.AddSingleton(new PostgresPackageStorageProvisioner(coreDatabaseConnectionString));
        services.AddScoped<IPackageWorkerSupervisor, DisabledPackageWorkerSupervisor>();
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
        services.AddScoped<CapabilityBroker>();
        services.AddScoped<ICapabilityClient>(provider => provider.GetRequiredService<CapabilityBroker>());
        return services;
    }

    private sealed record TrustedPublisherConfiguration(
        string PublisherId,
        string KeyId,
        string PublicKeyPem);
}
