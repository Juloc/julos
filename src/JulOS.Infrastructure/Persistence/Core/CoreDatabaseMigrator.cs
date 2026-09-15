using JulOS.Infrastructure.Persistence.Core.Sqlite;

using Microsoft.EntityFrameworkCore;

namespace JulOS.Infrastructure.Persistence.Core;

/// <summary>Initializes or migrates the configured core database.</summary>
public static class CoreDatabaseMigrator
{
    /// <summary>Initializes SQLite or applies committed PostgreSQL migrations.</summary>
    public static async Task MigrateAsync(
        CoreDatabaseConfiguration database,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(database);

        var options = new DbContextOptionsBuilder<CoreDbContext>();
        CorePersistenceServiceCollectionExtensions.Configure(options, database);

        if (database.Provider == CoreDatabaseProvider.Sqlite)
        {
            _ = await SqliteSchemaMigrationRunner
                .MigrateAsync(database.ConnectionString, timeProvider: null, cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        await using var context = new CoreDbContext(options.Options);
        await context.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Applies PostgreSQL migrations for compatibility with existing callers.</summary>
    public static Task MigrateAsync(
        string connectionString,
        CancellationToken cancellationToken = default) =>
        MigrateAsync(
            new CoreDatabaseConfiguration(CoreDatabaseProvider.PostgreSql, connectionString),
            cancellationToken);
}
