using JulOS.Application.Auditing;
using JulOS.Application.Authorization;
using JulOS.Application.Layouts;
using JulOS.Application.Profile;
using JulOS.Application.Operations;
using JulOS.Infrastructure.Auditing;
using JulOS.Infrastructure.Authentication;
using JulOS.Infrastructure.Authorization;
using JulOS.Infrastructure.Layouts;
using JulOS.Infrastructure.Profile;
using JulOS.Infrastructure.Operations;

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
        services.AddScoped<IAuditService, PostgresAuditService>();
        services.AddScoped<IPermissionAssignmentReader, EfPermissionAssignmentReader>();
        services.AddScoped<IAuthorizationAdministration, IdentityAuthorizationAdministration>();
        services.AddScoped<IDesktopLayoutService, PostgresDesktopLayoutService>();
        services.AddScoped<IProfileService, EfProfileService>();
        services.AddScoped<IOperationService, PostgresOperationService>();

        return services;
    }

    internal static void Configure(DbContextOptionsBuilder options, string connectionString)
    {
        options.UseNpgsql(
            connectionString,
            npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", CoreModelConfiguration.Schema));
    }
}
