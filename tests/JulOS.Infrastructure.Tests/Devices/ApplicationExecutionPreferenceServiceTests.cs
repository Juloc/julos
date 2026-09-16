using JulOS.Application.Concurrency;
using JulOS.Application.Devices;
using JulOS.Contracts.Devices;
using JulOS.Contracts.Layouts;
using JulOS.Infrastructure.Devices;
using JulOS.Infrastructure.Identifiers;
using JulOS.Infrastructure.Persistence.Core;
using JulOS.Infrastructure.Persistence.Core.Sqlite;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;

namespace JulOS.Infrastructure.Tests.Devices;

/// <summary>
/// MOB-006 background-execution preference: who may set it, and what may be set.
/// </summary>
[TestClass]
public sealed class ApplicationExecutionPreferenceServiceTests
{
    private readonly List<string> directories = [];

    private static readonly Guid Alice = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Bob = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid KeepActiveCapable = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid SuspendOnly = Guid.Parse("44444444-4444-4444-4444-444444444444");

    [TestCleanup]
    public void Cleanup()
    {
        SqliteConnection.ClearAllPools();
        foreach (var directory in this.directories)
        {
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch (IOException)
            {
                // Temporary test files the operating system still holds are not a failure.
            }
        }
    }

    [TestMethod]
    public async Task AnApplicationThatWasNeverConfiguredSuspends()
    {
        await using var fixture = await this.CreateAsync();

        var resolved = await fixture.Preferences.ReadCurrentAsync(
            Alice, KeepActiveCapable, WorkspaceClassNames.Phone, null);

        Assert.AreEqual(BackgroundModeNames.Suspend, resolved.BackgroundMode);
        Assert.AreEqual(LayoutScopeNames.Shared, resolved.LayoutScope);
        Assert.AreEqual(0, resolved.Revision);
        Assert.IsTrue(
            resolved.SupportsKeepSurfaceActive,
            "The declared capability is reported so the Shell can offer the choice.");
    }

    [TestMethod]
    public async Task AnApplicationThatDoesNotDeclareItCannotBeKeptActive()
    {
        await using var fixture = await this.CreateAsync();

        var refused = await Assert.ThrowsExactlyAsync<ApplicationExecutionPreferenceException>(
            () => fixture.Preferences.WriteCurrentAsync(
                Alice,
                SuspendOnly,
                WorkspaceClassNames.Phone,
                null,
                new UpdateApplicationExecutionPreferenceRequest(
                    BackgroundModeNames.KeepSurfaceActive,
                    ExpectedRevision: null)));

        Assert.AreEqual(ClientDeviceErrorCodes.BackgroundModeUnsupported, refused.Code);

        var resolved = await fixture.Preferences.ReadCurrentAsync(
            Alice, SuspendOnly, WorkspaceClassNames.Phone, null);
        Assert.AreEqual(BackgroundModeNames.Suspend, resolved.BackgroundMode);
        Assert.IsFalse(resolved.SupportsKeepSurfaceActive);
    }

    [TestMethod]
    public async Task TheUserChoiceIsStoredAndReadBack()
    {
        await using var fixture = await this.CreateAsync();

        var written = await fixture.Preferences.WriteCurrentAsync(
            Alice,
            KeepActiveCapable,
            WorkspaceClassNames.Phone,
            null,
            new UpdateApplicationExecutionPreferenceRequest(
                BackgroundModeNames.KeepSurfaceActive,
                ExpectedRevision: null));

        Assert.IsTrue(written.Created);
        Assert.AreEqual(1, written.Preference.Revision);

        var resolved = await fixture.Preferences.ReadCurrentAsync(
            Alice, KeepActiveCapable, WorkspaceClassNames.Phone, null);
        Assert.AreEqual(BackgroundModeNames.KeepSurfaceActive, resolved.BackgroundMode);
    }

    [TestMethod]
    public async Task EachWorkspaceClassIsConfiguredSeparately()
    {
        await using var fixture = await this.CreateAsync();

        _ = await fixture.Preferences.WriteCurrentAsync(
            Alice,
            KeepActiveCapable,
            WorkspaceClassNames.Phone,
            null,
            new UpdateApplicationExecutionPreferenceRequest(BackgroundModeNames.KeepSurfaceActive, null));

        var desktop = await fixture.Preferences.ReadCurrentAsync(
            Alice, KeepActiveCapable, WorkspaceClassNames.DesktopSingle, null);

        Assert.AreEqual(
            BackgroundModeNames.Suspend,
            desktop.BackgroundMode,
            "Keeping a phone application awake says nothing about the desktop.");
    }

    [TestMethod]
    public async Task ADevicePreferenceAnswersAheadOfTheSharedOne()
    {
        await using var fixture = await this.CreateAsync();
        var registration = await fixture.Devices.RegisterOrResolveAsync(
            Alice, null, new RegisterClientDeviceRequest("Phone", WorkspaceClassNames.Phone));
        var key = registration.IssuedKey;
        Assert.IsNotNull(key);

        _ = await fixture.Preferences.WriteCurrentAsync(
            Alice,
            KeepActiveCapable,
            WorkspaceClassNames.Phone,
            null,
            new UpdateApplicationExecutionPreferenceRequest(BackgroundModeNames.Suspend, null));
        _ = await fixture.Preferences.WriteCurrentAsync(
            Alice,
            KeepActiveCapable,
            WorkspaceClassNames.Phone,
            key,
            new UpdateApplicationExecutionPreferenceRequest(BackgroundModeNames.KeepSurfaceActive, null));

        var onDevice = await fixture.Preferences.ReadCurrentAsync(
            Alice, KeepActiveCapable, WorkspaceClassNames.Phone, key);
        var shared = await fixture.Preferences.ReadCurrentAsync(
            Alice, KeepActiveCapable, WorkspaceClassNames.Phone, null);

        Assert.AreEqual(BackgroundModeNames.KeepSurfaceActive, onDevice.BackgroundMode);
        Assert.AreEqual(LayoutScopeNames.Device, onDevice.LayoutScope);
        Assert.AreEqual(BackgroundModeNames.Suspend, shared.BackgroundMode);
    }

    [TestMethod]
    public async Task AnotherUsersPreferenceIsNeitherReadNorOverwritten()
    {
        await using var fixture = await this.CreateAsync();

        _ = await fixture.Preferences.WriteCurrentAsync(
            Alice,
            KeepActiveCapable,
            WorkspaceClassNames.Phone,
            null,
            new UpdateApplicationExecutionPreferenceRequest(BackgroundModeNames.KeepSurfaceActive, null));

        var bob = await fixture.Preferences.ReadCurrentAsync(
            Bob, KeepActiveCapable, WorkspaceClassNames.Phone, null);
        Assert.AreEqual(BackgroundModeNames.Suspend, bob.BackgroundMode);

        _ = await fixture.Preferences.WriteCurrentAsync(
            Bob,
            KeepActiveCapable,
            WorkspaceClassNames.Phone,
            null,
            new UpdateApplicationExecutionPreferenceRequest(BackgroundModeNames.Suspend, null));

        var alice = await fixture.Preferences.ReadCurrentAsync(
            Alice, KeepActiveCapable, WorkspaceClassNames.Phone, null);
        Assert.AreEqual(BackgroundModeNames.KeepSurfaceActive, alice.BackgroundMode);
    }

    [TestMethod]
    public async Task AStaleWriteIsRejectedWithTheAuthoritativeRevision()
    {
        await using var fixture = await this.CreateAsync();
        var created = await fixture.Preferences.WriteCurrentAsync(
            Alice,
            KeepActiveCapable,
            WorkspaceClassNames.Phone,
            null,
            new UpdateApplicationExecutionPreferenceRequest(BackgroundModeNames.KeepSurfaceActive, null));

        _ = await fixture.Preferences.WriteCurrentAsync(
            Alice,
            KeepActiveCapable,
            WorkspaceClassNames.Phone,
            null,
            new UpdateApplicationExecutionPreferenceRequest(
                BackgroundModeNames.Suspend,
                created.Preference.Revision));

        var conflict = await Assert.ThrowsExactlyAsync<ConcurrencyConflictException>(
            () => fixture.Preferences.WriteCurrentAsync(
                Alice,
                KeepActiveCapable,
                WorkspaceClassNames.Phone,
                null,
                new UpdateApplicationExecutionPreferenceRequest(
                    BackgroundModeNames.KeepSurfaceActive,
                    created.Preference.Revision)));

        Assert.AreEqual(2, conflict.CurrentRevision);
    }

    [TestMethod]
    public async Task AnApplicationThatDoesNotExistIsRefused()
    {
        await using var fixture = await this.CreateAsync();

        var failure = await Assert.ThrowsExactlyAsync<ApplicationExecutionPreferenceException>(
            () => fixture.Preferences.ReadCurrentAsync(
                Alice, Guid.NewGuid(), WorkspaceClassNames.Phone, null));

        Assert.AreEqual(ClientDeviceErrorCodes.NotFound, failure.Code);
    }

    private async Task<PreferenceFixture> CreateAsync()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "julos-execution-preferences",
            Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(directory);
        this.directories.Add(directory);

        var connectionString = $"Data Source={Path.Combine(directory, "julos.db")}";
        _ = await SqliteSchemaMigrationRunner.MigrateAsync(connectionString);
        await SeedApplicationsAsync(connectionString);
        return new PreferenceFixture(connectionString);
    }

    /// <summary>
    /// Seeds one application that declares it can stay active and one that does not.
    /// </summary>
    private static async Task SeedApplicationsAsync(string connectionString)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO application_definitions (id, owning_package_id, stable_key, display_name_key,
                                                 instance_policy, default_width, default_height,
                                                 minimum_width, minimum_height, is_enabled,
                                                 surface_contract_version, surface_supports_keep_active,
                                                 surface_handles_back, revision)
            VALUES ($capable, 'de.juloc.julos.reference', 'reference', 'reference.title',
                    'SingleInstancePerUser', 800, 600, 320, 240, 1, '1.0.0', 1, 1, 1),
                   ($suspendOnly, 'de.juloc.julos.hostmetrics', 'host-metrics', 'hostmetrics.title',
                    'SingleInstancePerUser', 800, 600, 320, 240, 1, '1.0.0', 0, 0, 1);
            """;
        _ = command.Parameters.AddWithValue("$capable", KeepActiveCapable.ToString());
        _ = command.Parameters.AddWithValue("$suspendOnly", SuspendOnly.ToString());
        _ = await command.ExecuteNonQueryAsync();
    }

    private sealed class PreferenceFixture : IAsyncDisposable
    {
        private readonly CoreDbContext context;

        public PreferenceFixture(string connectionString)
        {
            var options = new DbContextOptionsBuilder<CoreDbContext>();
            CorePersistenceServiceCollectionExtensions.Configure(
                options,
                new CoreDatabaseConfiguration(CoreDatabaseProvider.Sqlite, connectionString));

            var clock = new FakeTimeProvider(DateTimeOffset.Parse("2026-03-01T08:00:00Z", null));
            var identifiers = new TimeOrderedIdentifierGenerator(clock);
            this.context = new CoreDbContext(options.Options);
            this.Devices = new EfClientDeviceService(this.context, identifiers, clock);
            this.Preferences = new EfApplicationExecutionPreferenceService(this.context, identifiers);
        }

        public EfClientDeviceService Devices { get; }

        public EfApplicationExecutionPreferenceService Preferences { get; }

        public async ValueTask DisposeAsync() => await this.context.DisposeAsync();
    }
}
