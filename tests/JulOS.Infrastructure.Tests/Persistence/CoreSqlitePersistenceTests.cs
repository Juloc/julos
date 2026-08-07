using JulOS.Infrastructure.Persistence.Core;

using Microsoft.EntityFrameworkCore;

namespace JulOS.Infrastructure.Tests.Persistence;

[TestClass]
public sealed class CoreSqlitePersistenceTests
{
    [TestMethod]
    public async Task SqliteDatabaseCanBeInitializedWithoutPostgreSql()
    {
        var directory = Path.Combine(Path.GetTempPath(), "julos-sqlite-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var databasePath = Path.Combine(directory, "julos.db");
        var database = new CoreDatabaseConfiguration(
            CoreDatabaseProvider.Sqlite,
            $"Data Source={databasePath};Cache=Shared");

        try
        {
            await CoreDatabaseMigrator.MigrateAsync(database);

            var options = new DbContextOptionsBuilder<CoreDbContext>();
            CorePersistenceServiceCollectionExtensions.Configure(options, database);
            await using var context = new CoreDbContext(options.Options);

            Assert.IsTrue(await context.Database.CanConnectAsync());
            Assert.IsTrue(File.Exists(databasePath));
            Assert.IsTrue(context.Model.GetEntityTypes().All(entity => entity.GetSchema() is null));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public async Task SqliteRemoteSessionDeadlineQueryExecutesServerSide()
    {
        var directory = Path.Combine(Path.GetTempPath(), "julos-sqlite-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var databasePath = Path.Combine(directory, "julos.db");
        var database = new CoreDatabaseConfiguration(
            CoreDatabaseProvider.Sqlite,
            $"Data Source={databasePath};Cache=Shared");

        try
        {
            await CoreDatabaseMigrator.MigrateAsync(database);

            var options = new DbContextOptionsBuilder<CoreDbContext>();
            CorePersistenceServiceCollectionExtensions.Configure(options, database);
            await using var context = new CoreDbContext(options.Options);
            var now = DateTimeOffset.UtcNow;

            var rows = await context.RemoteSessions
                .Where(row => row.ExpiresAtUtc <= now || row.LastActivityAtUtc <= now)
                .OrderBy(row => row.UpdatedAtUtc)
                .ThenBy(row => row.Id)
                .ToListAsync();

            Assert.AreEqual(0, rows.Count);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
