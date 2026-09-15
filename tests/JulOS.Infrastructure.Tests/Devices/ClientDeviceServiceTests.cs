using JulOS.Application.Concurrency;
using JulOS.Application.Devices;
using JulOS.Contracts.Devices;
using JulOS.Domain.Primitives;
using JulOS.Infrastructure.Devices;
using JulOS.Infrastructure.Identifiers;
using JulOS.Infrastructure.Persistence.Core;
using JulOS.Infrastructure.Persistence.Core.Sqlite;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;

namespace JulOS.Infrastructure.Tests.Devices;

/// <summary>
/// MOB-003 ownership, concurrency and removal behaviour.
///
/// The acceptance criteria here are security properties, so they are exercised against a
/// real SQLite database through the production migration runner rather than against a
/// substitute.
/// </summary>
[TestClass]
public sealed class ClientDeviceServiceTests
{
    private readonly List<string> directories = [];

    private static readonly Guid Alice = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Bob = Guid.Parse("22222222-2222-2222-2222-222222222222");

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
    public async Task RegistrationIssuesAKeyOnceAndResolvesTheSameDeviceAfterwards()
    {
        await using var fixture = await this.CreateAsync();

        var first = await fixture.Service.RegisterOrResolveAsync(
            Alice, null, new RegisterClientDeviceRequest("Laptop", "desktop-single"));

        Assert.IsNotNull(first.IssuedKey);
        Assert.AreEqual("Laptop", first.Device.DisplayName);
        Assert.IsTrue(first.Device.IsCurrentDevice);

        var second = await fixture.Service.RegisterOrResolveAsync(
            Alice, first.IssuedKey, new RegisterClientDeviceRequest("Laptop", "tablet"));

        Assert.IsNull(second.IssuedKey, "A known key must not mint a second key.");
        Assert.AreEqual(first.Device.ClientDeviceId, second.Device.ClientDeviceId);
        Assert.AreEqual("tablet", second.Device.LastDetectedWorkspaceClass);
    }

    [TestMethod]
    public async Task ClearingSiteDataCreatesAVisiblyNewDevice()
    {
        await using var fixture = await this.CreateAsync();

        var first = await fixture.Service.RegisterOrResolveAsync(
            Alice, null, new RegisterClientDeviceRequest("Laptop", "desktop-single"));

        // A browser that cleared its storage presents no key at all.
        var second = await fixture.Service.RegisterOrResolveAsync(
            Alice, null, new RegisterClientDeviceRequest("Laptop", "desktop-single"));

        Assert.AreNotEqual(first.Device.ClientDeviceId, second.Device.ClientDeviceId);
        Assert.IsNotNull(second.IssuedKey);
        Assert.AreEqual(2, (await fixture.Service.ListAsync(Alice, null)).Count);
    }

    [TestMethod]
    public async Task AnotherUsersKeyNeverResolvesToThatUsersDevice()
    {
        await using var fixture = await this.CreateAsync();

        var alice = await fixture.Service.RegisterOrResolveAsync(
            Alice, null, new RegisterClientDeviceRequest("Alice laptop", "desktop-single"));

        // Bob presents Alice's key. It must register a new device for Bob rather than
        // adopting or revealing hers.
        var bob = await fixture.Service.RegisterOrResolveAsync(
            Bob, alice.IssuedKey, new RegisterClientDeviceRequest("Bob laptop", "desktop-single"));

        Assert.AreNotEqual(alice.Device.ClientDeviceId, bob.Device.ClientDeviceId);
        Assert.IsNotNull(bob.IssuedKey);

        var bobsDevices = await fixture.Service.ListAsync(Bob, alice.IssuedKey);
        Assert.AreEqual(1, bobsDevices.Count);
        Assert.AreEqual("Bob laptop", bobsDevices[0].DisplayName);
        Assert.IsFalse(
            bobsDevices[0].IsCurrentDevice,
            "Alice's key must not mark any of Bob's devices as current.");
    }

    [TestMethod]
    public async Task AnotherUserCannotReadUpdateOrRemoveADevice()
    {
        await using var fixture = await this.CreateAsync();

        var alice = await fixture.Service.RegisterOrResolveAsync(
            Alice, null, new RegisterClientDeviceRequest("Alice laptop", "desktop-single"));
        var id = alice.Device.ClientDeviceId;

        Assert.AreEqual(0, (await fixture.Service.ListAsync(Bob, null)).Count);

        var update = await Assert.ThrowsExactlyAsync<ClientDeviceException>(
            () => fixture.Service.UpdateAsync(Bob, id, new UpdateClientDeviceRequest("Taken", null, 1), null));
        Assert.AreEqual(ClientDeviceErrorCodes.NotFound, update.Code);

        var remove = await Assert.ThrowsExactlyAsync<ClientDeviceException>(
            () => fixture.Service.RemoveAsync(Bob, id, 1));
        Assert.AreEqual(ClientDeviceErrorCodes.NotFound, remove.Code);

        Assert.AreEqual(1, (await fixture.Service.ListAsync(Alice, null)).Count);
    }

    [TestMethod]
    public async Task AStalePreferenceWriteIsRejectedWithTheAuthoritativeRevision()
    {
        await using var fixture = await this.CreateAsync();

        var registered = await fixture.Service.RegisterOrResolveAsync(
            Alice, null, new RegisterClientDeviceRequest("Laptop", "desktop-single"));
        var id = registered.Device.ClientDeviceId;

        var updated = await fixture.Service.SetPreferenceAsync(
            Alice, id, "phone", new UpdateDeviceWorkspacePreferenceRequest("device", "fresh", 1), null);
        Assert.AreEqual(2, updated.Revision);

        var conflict = await Assert.ThrowsExactlyAsync<ConcurrencyConflictException>(
            () => fixture.Service.SetPreferenceAsync(
                Alice, id, "phone", new UpdateDeviceWorkspacePreferenceRequest("shared", "resume", 1), null));

        Assert.AreEqual(2, conflict.CurrentRevision);

        var current = (await fixture.Service.ListAsync(Alice, null)).Single();
        Assert.AreEqual("device", current.Preferences.Single().LayoutScope);
        Assert.AreEqual("fresh", current.Preferences.Single().RestoreMode);
    }

    [TestMethod]
    public async Task ADeviceCannotBePinnedToMultiDisplay()
    {
        await using var fixture = await this.CreateAsync();

        var registered = await fixture.Service.RegisterOrResolveAsync(
            Alice, null, new RegisterClientDeviceRequest("Laptop", "desktop-single"));

        var failure = await Assert.ThrowsExactlyAsync<ClientDeviceException>(
            () => fixture.Service.UpdateAsync(
                Alice,
                registered.Device.ClientDeviceId,
                new UpdateClientDeviceRequest("Laptop", "desktop-multi", 1),
                null));

        Assert.AreEqual(ClientDeviceErrorCodes.WorkspacePreferenceInvalid, failure.Code);
    }

    [TestMethod]
    public async Task RemovingADeviceRemovesOnlyItsOwnPreferences()
    {
        await using var fixture = await this.CreateAsync();

        var kept = await fixture.Service.RegisterOrResolveAsync(
            Alice, null, new RegisterClientDeviceRequest("Kept", "desktop-single"));
        var removed = await fixture.Service.RegisterOrResolveAsync(
            Alice, null, new RegisterClientDeviceRequest("Removed", "phone"));

        _ = await fixture.Service.SetPreferenceAsync(
            Alice, kept.Device.ClientDeviceId, "phone",
            new UpdateDeviceWorkspacePreferenceRequest("device", "fresh", 1), null);
        var removedWithPreference = await fixture.Service.SetPreferenceAsync(
            Alice, removed.Device.ClientDeviceId, "phone",
            new UpdateDeviceWorkspacePreferenceRequest("device", "resume", 1), null);

        await fixture.Service.RemoveAsync(
            Alice, removed.Device.ClientDeviceId, removedWithPreference.Revision);

        var remaining = await fixture.Service.ListAsync(Alice, null);
        Assert.AreEqual(1, remaining.Count);
        Assert.AreEqual("Kept", remaining[0].DisplayName);
        Assert.AreEqual(1, remaining[0].Preferences.Count, "The surviving device keeps its own preference.");

        await using var connection = new SqliteConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT count(*) FROM device_workspace_preferences;";
        Assert.AreEqual(1L, await command.ExecuteScalarAsync());
    }

    [TestMethod]
    public async Task TheStoredDeviceRecordNeverContainsTheRawKey()
    {
        await using var fixture = await this.CreateAsync();

        var registered = await fixture.Service.RegisterOrResolveAsync(
            Alice, null, new RegisterClientDeviceRequest("Laptop", "desktop-single"));

        await using var connection = new SqliteConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT client_instance_key_hash FROM client_devices;";
        var stored = (string?)await command.ExecuteScalarAsync();

        Assert.IsNotNull(stored);
        Assert.AreEqual(64, stored.Length, "Only a SHA-256 hash is stored.");
        Assert.AreNotEqual(registered.IssuedKey, stored);
    }

    private async Task<DeviceFixture> CreateAsync()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "julos-client-devices",
            Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(directory);
        this.directories.Add(directory);

        var connectionString = $"Data Source={Path.Combine(directory, "julos.db")}";
        _ = await SqliteSchemaMigrationRunner.MigrateAsync(connectionString);

        return new DeviceFixture(connectionString);
    }

    private sealed class DeviceFixture : IAsyncDisposable
    {
        private readonly CoreDbContext context;

        public DeviceFixture(string connectionString)
        {
            this.ConnectionString = connectionString;

            var options = new DbContextOptionsBuilder<CoreDbContext>();
            CorePersistenceServiceCollectionExtensions.Configure(
                options,
                new CoreDatabaseConfiguration(CoreDatabaseProvider.Sqlite, connectionString));

            var clock = new FakeTimeProvider(DateTimeOffset.Parse("2026-03-01T08:00:00Z", null));
            this.context = new CoreDbContext(options.Options);
            this.Service = new EfClientDeviceService(
                this.context,
                new TimeOrderedIdentifierGenerator(clock),
                clock);
        }

        public string ConnectionString { get; }

        public EfClientDeviceService Service { get; }

        public async ValueTask DisposeAsync() => await this.context.DisposeAsync();
    }
}
