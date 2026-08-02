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

        return services;
    }

    internal static void Configure(DbContextOptionsBuilder options, string connectionString)
    {
        options.UseNpgsql(
            connectionString,
            npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", CoreModelConfiguration.Schema));
    }
}
