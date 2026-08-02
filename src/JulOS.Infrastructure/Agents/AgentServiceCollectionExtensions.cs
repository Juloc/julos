using JulOS.Application.Agents;

using Microsoft.Extensions.DependencyInjection;

namespace JulOS.Infrastructure.Agents;

/// <summary>Registers the PostgreSQL-backed Agent control plane.</summary>
public static class AgentServiceCollectionExtensions
{
    /// <summary>Adds enrollment, identity, telemetry and command services.</summary>
    /// <param name="services">Dependency-injection service collection.</param>
    /// <returns>The same service collection.</returns>
    public static IServiceCollection AddJulOsAgentControl(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddScoped<IAgentControlService, PostgresAgentControlService>();
        return services;
    }
}
