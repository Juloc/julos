using JulOS.Application.Concurrency;
using JulOS.Application.Devices;
using JulOS.Application.Layouts;
using JulOS.Contracts.Devices;
using JulOS.Contracts.Layouts;
using JulOS.Infrastructure.Devices;
using JulOS.Infrastructure.Identifiers;
using JulOS.Infrastructure.Layouts;
using JulOS.Infrastructure.Persistence.Core;
using JulOS.Infrastructure.Persistence.Core.Sqlite;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;

namespace JulOS.Infrastructure.Tests.Layouts;

/// <summary>
/// MOB-004 layout identity: one layout per user, workspace class and layout scope.
/// </summary>
/// <remarks>
/// Run against a real SQLite database through the production migration runner, because the
/// rules being asserted are partly enforced by partial unique indexes and check
/// constraints that a substitute could not reproduce.
/// </remarks>
[TestClass]
public sealed class WorkspaceLayoutServiceTests
{
    private readonly List<string> directories = [];

    private static readonly Guid Alice = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Bob = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid ApplicationId = Guid.Parse("33333333-3333-3333-3333-333333333333");

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
    public async Task AWorkspaceThatWasNeverArrangedResolvesToAnEmptySharedLayout()
    {
        await using var fixture = await this.CreateAsync();

        var resolved = await fixture.Layouts.ReadCurrentAsync(Alice, WorkspaceClassNames.Tablet, null);

        Assert.AreEqual(WorkspaceClassNames.Tablet, resolved.WorkspaceClass);
        Assert.AreEqual(LayoutScopeNames.Shared, resolved.LayoutScope);
        Assert.AreEqual(RestoreModeNames.Resume, resolved.RestoreMode);
        Assert.IsTrue(resolved.PersistenceEnabled);
        Assert.AreEqual(0, resolved.Revision);
        Assert.IsNull(resolved.Layout.LayoutId);
        Assert.AreEqual(0, resolved.Layout.Windows.Count);
    }

    [TestMethod]
    public async Task ArrangingOneWorkspaceClassLeavesEveryOtherUntouched()
    {
        await using var fixture = await this.CreateAsync();

        _ = await Write(fixture, WorkspaceClassNames.DesktopSingle, 0, Window("aaaa", 1200, 800));

        var phone = await fixture.Layouts.ReadCurrentAsync(Alice, WorkspaceClassNames.Phone, null);
        var tablet = await fixture.Layouts.ReadCurrentAsync(Alice, WorkspaceClassNames.Tablet, null);
        var desktop = await fixture.Layouts.ReadCurrentAsync(Alice, WorkspaceClassNames.DesktopSingle, null);

        Assert.AreEqual(0, phone.Layout.Windows.Count, "A desktop write must not reach the phone layout.");
        Assert.AreEqual(0, tablet.Layout.Windows.Count);
        Assert.AreEqual(1, desktop.Layout.Windows.Count);
        Assert.AreEqual(1200, desktop.Layout.Windows[0].Width);
    }

    [TestMethod]
    public async Task AnotherUsersLayoutIsNeverReadOrOverwritten()
    {
        await using var fixture = await this.CreateAsync();

        _ = await Write(fixture, WorkspaceClassNames.DesktopSingle, 0, Window("aaaa", 1200, 800));

        var bob = await fixture.Layouts.ReadCurrentAsync(Bob, WorkspaceClassNames.DesktopSingle, null);
        Assert.AreEqual(0, bob.Layout.Windows.Count);
        Assert.IsNull(bob.Layout.LayoutId);

        var bobsWrite = await fixture.Layouts.WriteCurrentAsync(
            Bob,
            WorkspaceClassNames.DesktopSingle,
            null,
            Request(0, Window("bbbb", 800, 600)));

        var alice = await fixture.Layouts.ReadCurrentAsync(Alice, WorkspaceClassNames.DesktopSingle, null);
        Assert.AreEqual(1200, alice.Layout.Windows[0].Width, "Each user has their own shared layout.");
        Assert.AreNotEqual(alice.Layout.LayoutId, bobsWrite.Layout.Layout.LayoutId);
    }

    [TestMethod]
    public async Task ADevicePreferringItsOwnLayoutNeitherReadsNorWritesTheSharedOne()
    {
        await using var fixture = await this.CreateAsync();
        var registration = await fixture.Devices.RegisterOrResolveAsync(
            Alice, null, new RegisterClientDeviceRequest("Laptop", WorkspaceClassNames.DesktopSingle));
        var key = registration.IssuedKey;
        Assert.IsNotNull(key);

        // The shared layout exists first and must survive untouched.
        _ = await Write(fixture, WorkspaceClassNames.DesktopSingle, 0, Window("aaaa", 1200, 800));

        _ = await fixture.Devices.SetPreferenceAsync(
            Alice,
            registration.Device.ClientDeviceId,
            WorkspaceClassNames.DesktopSingle,
            new UpdateDeviceWorkspacePreferenceRequest(LayoutScopeNames.Device, RestoreModeNames.Resume, 1),
            key);

        var resolved = await fixture.Layouts.ReadCurrentAsync(Alice, WorkspaceClassNames.DesktopSingle, key);
        Assert.AreEqual(LayoutScopeNames.Device, resolved.LayoutScope);
        Assert.AreEqual(0, resolved.Layout.Windows.Count, "The device starts with its own empty layout.");

        var written = await fixture.Layouts.WriteCurrentAsync(
            Alice,
            WorkspaceClassNames.DesktopSingle,
            key,
            Request(0, Window("cccc", 640, 480)));
        Assert.IsTrue(written.Created);

        var shared = await fixture.Layouts.ReadCurrentAsync(Alice, WorkspaceClassNames.DesktopSingle, null);
        Assert.AreEqual(1200, shared.Layout.Windows[0].Width, "The shared layout is not the device layout.");

        var device = await fixture.Layouts.ReadCurrentAsync(Alice, WorkspaceClassNames.DesktopSingle, key);
        Assert.AreEqual(640, device.Layout.Windows[0].Width);
    }

    [TestMethod]
    public async Task RemovingADeviceRemovesItsLayoutAndKeepsTheSharedOne()
    {
        await using var fixture = await this.CreateAsync();
        var registration = await fixture.Devices.RegisterOrResolveAsync(
            Alice, null, new RegisterClientDeviceRequest("Laptop", WorkspaceClassNames.DesktopSingle));
        var key = registration.IssuedKey;
        Assert.IsNotNull(key);

        _ = await Write(fixture, WorkspaceClassNames.DesktopSingle, 0, Window("aaaa", 1200, 800));
        var preferred = await fixture.Devices.SetPreferenceAsync(
            Alice,
            registration.Device.ClientDeviceId,
            WorkspaceClassNames.DesktopSingle,
            new UpdateDeviceWorkspacePreferenceRequest(LayoutScopeNames.Device, RestoreModeNames.Resume, 1),
            key);
        _ = await fixture.Layouts.WriteCurrentAsync(
            Alice, WorkspaceClassNames.DesktopSingle, key, Request(0, Window("cccc", 640, 480)));

        await fixture.Devices.RemoveAsync(Alice, registration.Device.ClientDeviceId, preferred.Revision);

        var shared = await fixture.Layouts.ReadCurrentAsync(Alice, WorkspaceClassNames.DesktopSingle, null);
        Assert.AreEqual(1200, shared.Layout.Windows[0].Width, "Removing a device never touches a shared layout.");

        await using var connection = new SqliteConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT count(*) FROM desktop_layouts WHERE client_device_id IS NOT NULL;";
        Assert.AreEqual(0L, await command.ExecuteScalarAsync(), "The device layout is gone with the device.");
    }

    [TestMethod]
    public async Task AFreshWorkspaceRestoresNothingAndRefusesToStoreAnything()
    {
        await using var fixture = await this.CreateAsync();
        var registration = await fixture.Devices.RegisterOrResolveAsync(
            Alice, null, new RegisterClientDeviceRequest("Laptop", WorkspaceClassNames.DesktopSingle));
        var key = registration.IssuedKey;
        Assert.IsNotNull(key);

        // Something is stored first, so the assertion proves fresh mode hides it rather
        // than merely observing an empty database.
        _ = await Write(fixture, WorkspaceClassNames.DesktopSingle, 0, Window("aaaa", 1200, 800));
        _ = await fixture.Devices.SetPreferenceAsync(
            Alice,
            registration.Device.ClientDeviceId,
            WorkspaceClassNames.DesktopSingle,
            new UpdateDeviceWorkspacePreferenceRequest(LayoutScopeNames.Shared, RestoreModeNames.Fresh, 1),
            key);

        var resolved = await fixture.Layouts.ReadCurrentAsync(Alice, WorkspaceClassNames.DesktopSingle, key);
        Assert.AreEqual(RestoreModeNames.Fresh, resolved.RestoreMode);
        Assert.IsFalse(resolved.PersistenceEnabled);
        Assert.IsNull(resolved.Layout.LayoutId);
        Assert.AreEqual(0, resolved.Revision);
        Assert.AreEqual(0, resolved.Layout.Windows.Count, "Fresh mode starts with no restored windows.");

        var refused = await Assert.ThrowsExactlyAsync<WorkspaceLayoutFailureException>(
            () => fixture.Layouts.WriteCurrentAsync(
                Alice, WorkspaceClassNames.DesktopSingle, key, Request(0, Window("cccc", 640, 480))));
        Assert.AreEqual(WorkspaceLayoutErrorCodes.PersistenceDisabled, refused.Code);

        var untouched = await fixture.Layouts.ReadCurrentAsync(Alice, WorkspaceClassNames.DesktopSingle, null);
        Assert.AreEqual(1200, untouched.Layout.Windows[0].Width, "A fresh session stores nothing at all.");
    }

    [TestMethod]
    public async Task AStaleWriteIsRejectedWithTheAuthoritativeRevision()
    {
        await using var fixture = await this.CreateAsync();
        var first = await Write(fixture, WorkspaceClassNames.DesktopSingle, 0, Window("aaaa", 1200, 800));
        Assert.AreEqual(1, first.Revision);

        _ = await Write(fixture, WorkspaceClassNames.DesktopSingle, 1, Window("aaaa", 1000, 700));

        var conflict = await Assert.ThrowsExactlyAsync<ConcurrencyConflictException>(
            () => fixture.Layouts.WriteCurrentAsync(
                Alice, WorkspaceClassNames.DesktopSingle, null, Request(1, Window("aaaa", 900, 600))));
        Assert.AreEqual(2, conflict.CurrentRevision);
    }

    [TestMethod]
    public async Task AWorkspaceClassThatDoesNotExistIsRejected()
    {
        await using var fixture = await this.CreateAsync();

        var failure = await Assert.ThrowsExactlyAsync<WorkspaceLayoutFailureException>(
            () => fixture.Layouts.ReadCurrentAsync(Alice, "desktop", null));

        Assert.AreEqual(WorkspaceLayoutErrorCodes.WorkspaceClassInvalid, failure.Code);
    }

    [TestMethod]
    public async Task ADisplaySlotAboveZeroIsRefusedOutsideMultiDisplay()
    {
        await using var fixture = await this.CreateAsync();

        var failure = await Assert.ThrowsExactlyAsync<WorkspaceLayoutFailureException>(
            () => fixture.Layouts.WriteCurrentAsync(
                Alice,
                WorkspaceClassNames.DesktopSingle,
                null,
                Request(0, Window("aaaa", 800, 600) with { DisplaySlot = 1 })));

        Assert.AreEqual(WorkspaceLayoutErrorCodes.Invalid, failure.Code);
    }

    [TestMethod]
    public async Task MultiDisplayIsInitializedExplicitlyAndCopiesTheSingleDisplayArrangement()
    {
        await using var fixture = await this.CreateAsync();
        _ = await Write(fixture, WorkspaceClassNames.DesktopSingle, 0, Window("aaaa", 1200, 800));

        var absent = await fixture.Layouts.ReadCurrentAsync(Alice, WorkspaceClassNames.DesktopMulti, null);
        Assert.IsNull(absent.Layout.LayoutId, "Multi-display is never created as a side effect of a read.");

        var initialized = await fixture.Layouts.InitializeMultiDisplayAsync(
            Alice, null, copyFromDesktopSingle: true, displayCount: 2);

        Assert.IsTrue(initialized.Created);
        Assert.AreEqual(2, initialized.Layout.Layout.DisplayCount);
        Assert.AreEqual(1, initialized.Layout.Layout.Windows.Count);
        Assert.AreEqual(
            0,
            initialized.Layout.Layout.Windows[0].DisplaySlot,
            "No physical topology is invented: every copied window starts on the first display.");

        var again = await fixture.Layouts.InitializeMultiDisplayAsync(
            Alice, null, copyFromDesktopSingle: true, displayCount: 3);
        Assert.IsFalse(again.Created, "Initializing twice must not replace an arrangement the user made.");
    }

    [TestMethod]
    public async Task APhonePresentationModeIsRefusedOnADesktopWorkspace()
    {
        await using var fixture = await this.CreateAsync();

        var request = Request(0, Window("aaaa", 800, 600));
        var phoneMode = request with
        {
            Layout = request.Layout with { PresentationMode = PresentationModeNames.PhoneSingle },
        };

        var failure = await Assert.ThrowsExactlyAsync<WorkspaceLayoutFailureException>(
            () => fixture.Layouts.WriteCurrentAsync(
                Alice, WorkspaceClassNames.DesktopSingle, null, phoneMode));

        Assert.AreEqual(WorkspaceLayoutErrorCodes.WorkspaceClassInvalid, failure.Code);
    }

    private static async Task<WorkspaceLayoutResponse> Write(
        LayoutFixture fixture,
        string workspaceClass,
        int expectedRevision,
        params DesktopWindowContract[] windows)
    {
        var result = await fixture.Layouts.WriteCurrentAsync(
            Alice, workspaceClass, null, Request(expectedRevision, windows));
        return result.Layout;
    }

    private static WorkspaceLayoutWriteRequest Request(
        int expectedRevision,
        params DesktopWindowContract[] windows) =>
        new(
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
            expectedRevision);

    private static DesktopWindowContract Window(string prefix, int width, int height) => new(
        Guid.Parse($"{prefix}aaaa-0000-4000-8000-000000000001"),
        ApplicationId,
        LaunchTargetId: null,
        "normal",
        0,
        0,
        width,
        height,
        0,
        0,
        width,
        height,
        ZIndex: 0,
        SessionReferenceId: null,
        DisplaySlot: 0);

    private async Task<LayoutFixture> CreateAsync()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "julos-workspace-layouts",
            Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(directory);
        this.directories.Add(directory);

        var connectionString = $"Data Source={Path.Combine(directory, "julos.db")}";
        _ = await SqliteSchemaMigrationRunner.MigrateAsync(connectionString);

        await SeedApplicationAsync(connectionString);
        return new LayoutFixture(connectionString);
    }

    /// <summary>Windows reference an application definition, which the schema enforces.</summary>
    private static async Task SeedApplicationAsync(string connectionString)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO application_definitions (id, owning_package_id, stable_key, display_name_key,
                                                 instance_policy, default_width, default_height,
                                                 minimum_width, minimum_height, is_enabled, revision)
            VALUES ($id, 'de.juloc.julos.reference', 'reference', 'reference.title',
                    'SingleInstancePerUser', 800, 600, 320, 240, 1, 1);
            """;
        _ = command.Parameters.AddWithValue("$id", ApplicationId.ToString());
        _ = await command.ExecuteNonQueryAsync();
    }

    private sealed class LayoutFixture : IAsyncDisposable
    {
        private readonly CoreDbContext context;

        public LayoutFixture(string connectionString)
        {
            this.ConnectionString = connectionString;

            var options = new DbContextOptionsBuilder<CoreDbContext>();
            CorePersistenceServiceCollectionExtensions.Configure(
                options,
                new CoreDatabaseConfiguration(CoreDatabaseProvider.Sqlite, connectionString));

            var clock = new FakeTimeProvider(DateTimeOffset.Parse("2026-03-01T08:00:00Z", null));
            var identifiers = new TimeOrderedIdentifierGenerator(clock);
            this.context = new CoreDbContext(options.Options);
            this.Devices = new EfClientDeviceService(this.context, identifiers, clock);
            this.Layouts = new EfWorkspaceLayoutService(this.context, identifiers, clock);
        }

        public string ConnectionString { get; }

        public EfClientDeviceService Devices { get; }

        public EfWorkspaceLayoutService Layouts { get; }

        public async ValueTask DisposeAsync() => await this.context.DisposeAsync();
    }
}
