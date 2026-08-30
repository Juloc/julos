using JulOS.Application.Remote;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace JulOS.Infrastructure.Remote;

/// <summary>Registers configured Remote provider policy and Runtime Manager integration.</summary>
public static class RemoteOrchestrationServiceCollectionExtensions
{
    /// <summary>Adds Remote session runtime orchestration services.</summary>
    public static IServiceCollection AddJulOsRemoteOrchestration(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddSingleton<IRemoteRuntimePolicy>(ConfiguredRemoteRuntimePolicy.Read(configuration));
        services.AddSingleton(serviceProvider => RemoteProviderCallbackAuthenticator.Read(
            configuration,
            serviceProvider.GetRequiredService<TimeProvider>()));
        services.AddSingleton(serviceProvider => RemoteDisplayGateway.Read(
            configuration,
            serviceProvider.GetRequiredService<TimeProvider>()));
        services.AddScoped<IRemoteSessionProvisioner, PostgresRemoteSessionProvisioner>();
        services.AddScoped<IRemoteSessionProvisioningReconciler, PostgresRemoteSessionProvisioningReconciler>();
        services.AddScoped<IRemoteSessionLifecycleService, PostgresRemoteSessionLifecycleService>();
        services.AddScoped<IRemoteSessionConnectionService, PostgresRemoteSessionConnectionService>();
        services.AddScoped<PostgresRemoteDisplayAuthorizationService>();
        var runtimeManager = RemoteRuntimeManagerClientOptions.Read(configuration);
        if (runtimeManager is null)
        {
            services.AddSingleton<IRemoteRuntimeManager, UnavailableRemoteRuntimeManager>();
        }
        else
        {
            services.AddSingleton<IRemoteRuntimeManager>(new HttpRemoteRuntimeManager(runtimeManager));
        }
        services.AddScoped<RemoteSessionCapabilityProvider>();
        return services;
    }
}
