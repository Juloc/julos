using JulOS.Application.Auditing;
using JulOS.Application.Authorization;
using JulOS.Application.Layouts;
using JulOS.Application.Operations;
using JulOS.Application.Profile;
using JulOS.Application.Remote;
using JulOS.Infrastructure.Auditing;
using JulOS.Infrastructure.Authentication;
using JulOS.Infrastructure.Authorization;
using JulOS.Infrastructure.Layouts;
using JulOS.Infrastructure.Operations;
using JulOS.Infrastructure.Profile;
using JulOS.Infrastructure.Remote;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace JulOS.Infrastructure.Persistence.Core;

/// <summary>Supported core database providers.</summary>
public enum CoreDatabaseProvider
{
    /// <summary>PostgreSQL with committed EF Core migrations.</summary>
    PostgreSql,

    /// <summary>SQLite for a single JulOS server instance.</summary>
    Sqlite,
}

/// <summary>Resolved provider and connection string for the JulOS core database.</summary>
public sealed record CoreDatabaseConfiguration(
    CoreDatabaseProvider Provider,
    string ConnectionString)
{
    /// <summary>Reads and validates the core database configuration.</summary>
    public static CoreDatabaseConfiguration Read(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var providerValue = configuration["Database:Provider"]?.Trim();
        var connectionString = configuration.GetConnectionString("CoreDatabase");

        // SQLite is the default core store for a single-host deployment; PostgreSQL is
        // opt-in for larger or multi-instance deployments (D033). An explicit provider is
        // always honoured. When none is set, a configured connection string still selects
        // PostgreSQL so existing deployments that only set ConnectionStrings__CoreDatabase
        // keep working, while a bare deployment with nothing configured gets SQLite.
        var provider = providerValue?.ToLowerInvariant() switch
        {
            "sqlite" => CoreDatabaseProvider.Sqlite,
            "postgres" or "postgresql" => CoreDatabaseProvider.PostgreSql,
            null or "" => string.IsNullOrWhiteSpace(connectionString)
                ? CoreDatabaseProvider.Sqlite
                : CoreDatabaseProvider.PostgreSql,
            _ => throw new InvalidOperationException(
                "Database:Provider must be either 'postgresql' or 'sqlite'."),
        };

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            if (provider == CoreDatabaseProvider.Sqlite)
            {
                connectionString = "Data Source=/var/lib/julos/julos.db;Cache=Shared";
            }
            else
            {
                throw new InvalidOperationException(
                    "The connection string 'CoreDatabase' is not configured. "
                    + "Set ConnectionStrings__CoreDatabase or see deploy/compose/README.md.");
            }
        }

        return new CoreDatabaseConfiguration(provider, connectionString);
    }
}

/// <summary>Registers the authoritative JulOS core store.</summary>
public static class CorePersistenceServiceCollectionExtensions
{
    /// <summary>Adds the core context with the configured database provider.</summary>
    public static IServiceCollection AddJulOsCorePersistence(
        this IServiceCollection services,
        CoreDatabaseConfiguration database)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(database);

        services.AddDbContext<CoreDbContext>(options => Configure(options, database));
        services.AddScoped<InitialAdministratorProvisioner>();
        services.AddScoped<IAuditService, PostgresAuditService>();
        services.AddScoped<IPermissionAssignmentReader, EfPermissionAssignmentReader>();
        services.AddScoped<IAuthorizationAdministration, IdentityAuthorizationAdministration>();
        services.AddScoped<IDesktopLayoutService, PostgresDesktopLayoutService>();
        services.AddScoped<IProfileService, EfProfileService>();
        services.AddScoped<IOperationService, PostgresOperationService>();
        services.AddScoped<RemoteSessionContractValidator>();
        services.AddScoped<IRemoteSessionService, PostgresRemoteSessionService>();

        return services;
    }

    /// <summary>Adds PostgreSQL persistence for compatibility with existing composition roots.</summary>
    public static IServiceCollection AddJulOsCorePersistence(
        this IServiceCollection services,
        string connectionString) =>
        services.AddJulOsCorePersistence(new CoreDatabaseConfiguration(
            CoreDatabaseProvider.PostgreSql,
            connectionString));

    internal static void Configure(
        DbContextOptionsBuilder options,
        CoreDatabaseConfiguration database)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(database);
        ArgumentException.ThrowIfNullOrWhiteSpace(database.ConnectionString);

        if (database.Provider == CoreDatabaseProvider.Sqlite)
        {
            options.UseSqlite(database.ConnectionString);
            options.AddInterceptors(SqlitePerformanceInterceptor.Instance);
            return;
        }

        options.UseNpgsql(
            database.ConnectionString,
            npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", CoreModelConfiguration.Schema));
    }

    internal static void Configure(DbContextOptionsBuilder options, string connectionString) =>
        Configure(
            options,
            new CoreDatabaseConfiguration(CoreDatabaseProvider.PostgreSql, connectionString));
}
