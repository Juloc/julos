using Microsoft.EntityFrameworkCore;

namespace JulOS.Infrastructure.Persistence.Core;

/// <summary>Applies committed core database migrations.</summary>
public static class CoreDatabaseMigrator
{
    /// <summary>
    /// Migrates the configured database to the schema committed with this application.
    /// </summary>
    public static async Task MigrateAsync(
        string connectionString,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        var options = new DbContextOptionsBuilder<CoreDbContext>();
        CorePersistenceServiceCollectionExtensions.Configure(options, connectionString);

        await using var context = new CoreDbContext(options.Options);
        await context.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
    }
}
