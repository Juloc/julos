using JulOS.Contracts.Devices;
using JulOS.Contracts.Layouts;
using JulOS.Infrastructure.Identifiers;
using JulOS.Infrastructure.Layouts;
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
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
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
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public async Task SqliteAppliesWalAndBusyTimeoutPragmas()
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
            await context.Database.OpenConnectionAsync();

            var journalMode = (await ExecuteScalarAsync(context, "PRAGMA journal_mode;"))?
                .ToString()?.ToLowerInvariant();
            var busyTimeout = (await ExecuteScalarAsync(context, "PRAGMA busy_timeout;"))?.ToString();

            Assert.AreEqual("wal", journalMode);
            Assert.AreEqual("5000", busyTimeout);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(directory, recursive: true);
        }
    }

    private static async Task<object?> ExecuteScalarAsync(CoreDbContext context, string sql)
    {
        var connection = context.Database.GetDbConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return await command.ExecuteScalarAsync();
    }

    [TestMethod]
    public async Task SqliteSavesReorderedWindowsWithoutUniqueZIndexConflict()
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
            // This test targets the (layout, z_index) unique index during a window reorder, not
            // referential integrity. EF Core enables SQLite foreign keys by default; relax them on
            // this open connection so placing windows needs no seeded application catalog. The
            // unique index that the fix addresses is still enforced regardless of this pragma.
            await context.Database.OpenConnectionAsync();
            _ = await ExecuteScalarAsync(context, "PRAGMA foreign_keys=OFF;");
            var service = new EfWorkspaceLayoutService(
                context,
                new TimeOrderedIdentifierGenerator(TimeProvider.System),
                TimeProvider.System);

            var userId = Guid.Parse("11111111-1111-4111-8111-111111111111");
            var appId = Guid.Parse("22222222-2222-4222-8222-222222222222");
            var w1 = Guid.Parse("aaaaaaaa-0000-4000-8000-000000000001");
            var w2 = Guid.Parse("aaaaaaaa-0000-4000-8000-000000000002");
            var w3 = Guid.Parse("aaaaaaaa-0000-4000-8000-000000000003");

            static DesktopWindowContract Window(Guid id, Guid application, int zIndex) => new(
                id, application, LaunchTargetId: null, "normal", 0, 0, 800, 600, 0, 0, 800, 600, zIndex,
                SessionReferenceId: null, DisplaySlot: 0);

            var first = await WriteAsync(
                service, userId, 0, [Window(w1, appId, 0), Window(w2, appId, 1), Window(w3, appId, 2)]);
            Assert.AreEqual(1, first.Revision);

            // Bring w1 to the front: the same window ids now carry z-indexes that collide with
            // the previously stored rows if the save updated them in place, tripping the unique
            // index ux_desktop_windows_layout_z_index.
            var second = await WriteAsync(
                service, userId, first.Revision, [Window(w2, appId, 0), Window(w3, appId, 1), Window(w1, appId, 2)]);

            Assert.AreEqual(2, second.Revision);
            CollectionAssert.AreEqual(
                new[] { w2, w3, w1 },
                second.Layout.Windows.Select(window => window.WindowId).ToArray());

            // Closing the middle window leaves a gap the save must re-pack without conflict.
            var third = await WriteAsync(
                service, userId, second.Revision, [Window(w2, appId, 0), Window(w1, appId, 2)]);

            Assert.AreEqual(3, third.Revision);
            CollectionAssert.AreEqual(
                new[] { w2, w1 },
                third.Layout.Windows.Select(window => window.WindowId).ToArray());
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(directory, recursive: true);
        }
    }

    private static async Task<WorkspaceLayoutResponse> WriteAsync(
        EfWorkspaceLayoutService service,
        Guid userId,
        int expectedRevision,
        DesktopWindowContract[] windows)
    {
        var result = await service.WriteCurrentAsync(
            userId,
            WorkspaceClassNames.DesktopSingle,
            presentedKey: null,
            new WorkspaceLayoutWriteRequest(
                new WorkspaceLayoutDocument(
                    LayoutId: null,
                    "Default",
                    PresentationModeNames.Freeform,
                    PrimaryWindowId: null,
                    SecondaryWindowId: null,
                    SplitRatioPermille: null,
                    DisplayCount: 1,
                    UpdatedAtUtc: DateTimeOffset.UnixEpoch,
                    windows,
                    []),
                expectedRevision));
        return result.Layout;
    }
}
