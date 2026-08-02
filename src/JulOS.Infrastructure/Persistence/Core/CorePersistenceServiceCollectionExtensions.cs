using JulOS.Application.Authorization;
using JulOS.Application.Profile;
using JulOS.Infrastructure.Authentication;
using JulOS.Infrastructure.Authorization;
using JulOS.Infrastructure.Profile;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace JulOS.Infrastructure.Persistence.Core;

/// <summary>Registers the authoritative core PostgreSQL store.</summary>
public static class CorePersistenceServiceCollectionExtensions
{
    /// <summary>Adds the core context with the configured PostgreSQL connection.</summary>
    public static IServiceCollection AddJulOsCorePersistence(
        this IServiceCollection services,
        string connectionString)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        services.AddDbContext<CoreDbContext>(options => Configure(options, connectionString));
        services.AddScoped<InitialAdministratorProvisioner>();
        services.AddScoped<IPermissionAssignmentReader, EfPermissionAssignmentReader>();
        services.AddScoped<IAuthorizationAdministration, IdentityAuthorizationAdministration>();
        services.AddScoped<IProfileService, EfProfileService>();

        return services;
    }

    internal static void Configure(DbContextOptionsBuilder options, string connectionString)
    {
        options.UseNpgsql(
            connectionString,
            npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", CoreModelConfiguration.Schema));
    }
}
