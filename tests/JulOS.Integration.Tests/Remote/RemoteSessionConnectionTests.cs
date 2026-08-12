using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using JulOS.Application.Remote;
using JulOS.Application.Secrets;
using JulOS.Contracts.Authentication;
using JulOS.Contracts.Remote;
using JulOS.Contracts.Runtime;
using JulOS.Infrastructure.Packages;
using JulOS.Infrastructure.Persistence.Core;
using JulOS.Infrastructure.Remote;
using JulOS.Integration.Tests.Persistence;
using JulOS.PackageSdk;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace JulOS.Integration.Tests.Remote;

[TestClass]
[DoNotParallelize]
public sealed class RemoteSessionConnectionTests
{
    private const string AdministratorPassword = "Valid-Initial-Password-42!";
    private const string CallerPackageId = "de.juloc.julos.remote";

    private static readonly WebApplicationFactoryClientOptions ClientOptions = new()
    {
        BaseAddress = new Uri("https://localhost"),
        AllowAutoRedirect = false,
        HandleCookies = true,
    };

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [TestMethod]
    public async Task ConnectRequiresExactRuntimeAndIsIdempotent()
    {
        await using var database = await CreateMigratedDatabaseAsync().ConfigureAwait(false);
        var clock = new MutableTimeProvider(new DateTimeOffset(2026, 8, 4, 19, 0, 0, TimeSpan.Zero));
        var runtime = new RecordingRuntimeManager();
        var networkProfileId = Guid.Parse("55555555-5555-4555-8555-555555555555");
        using var host = CreateHost(database.ConnectionString, networkProfileId, runtime, clock);
        using var client = host.CreateClient(ClientOptions);
        var administrator = await SetupAdministratorAsync(client).ConfigureAwait(false);

        await using var scope = host.Services.CreateAsyncScope();
        var provisioned = await CreateProvisionedSessionAsync(
            scope.ServiceProvider,
            administrator.UserId,
            networkProfileId,
            clock,
            "remote-provider-connect").ConfigureAwait(false);
        var runtimeId = $"remote-{provisioned.SessionId:N}";
        var connections = scope.ServiceProvider.GetRequiredService<IRemoteSessionConnectionService>();

        var connected = await connections.ConnectAsync(new ConnectRemoteSessionCommand(
            provisioned.SessionId,
            runtimeId,
            provisioned.Revision)).ConfigureAwait(false);

        Assert.AreEqual(RemoteSessionStates.Connected, connected.State);
        Assert.IsNotNull(connected.ConnectedAtUtc);
        Assert.IsNull(connected.EndedAtUtc);
        Assert.IsNull(connected.Display);
        Assert.IsNull(connected.Failure);

        var repeated = await connections.ConnectAsync(new ConnectRemoteSessionCommand(
            provisioned.SessionId,
            runtimeId,
            ExpectedRevision: 1)).ConfigureAwait(false);

        Assert.AreEqual(connected.Revision, repeated.Revision);
        Assert.AreEqual(RemoteSessionStates.Connected, repeated.State);
    }

    [TestMethod]
    public async Task FailureIsCallerSafeIdempotentAndCleanedByLifecycle()
    {
        await using var database = await CreateMigratedDatabaseAsync().ConfigureAwait(false);
        var clock = new MutableTimeProvider(new DateTimeOffset(2026, 8, 4, 20, 0, 0, TimeSpan.Zero));
        var runtime = new RecordingRuntimeManager();
        var networkProfileId = Guid.Parse("66666666-6666-4666-8666-666666666666");
        using var host = CreateHost(database.ConnectionString, networkProfileId, runtime, clock);
        using var client = host.CreateClient(ClientOptions);
        var administrator = await SetupAdministratorAsync(client).ConfigureAwait(false);

        Guid sessionId;
        long failedRevision;
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var provisioned = await CreateProvisionedSessionAsync(
                scope.ServiceProvider,
                administrator.UserId,
                networkProfileId,
                clock,
                "remote-provider-failure").ConfigureAwait(false);
            sessionId = provisioned.SessionId;
            var runtimeId = $"remote-{sessionId:N}";
            var connections = scope.ServiceProvider.GetRequiredService<IRemoteSessionConnectionService>();
            var failed = await connections.FailAsync(new FailRemoteSessionCommand(
                sessionId,
                runtimeId,
                provisioned.Revision,
                RemoteSessionFailureCodes.AuthenticationFailed,
                "The remote endpoint rejected authentication.",
                Retryable: false)).ConfigureAwait(false);

            Assert.AreEqual(RemoteSessionStates.Failed, failed.State);
            Assert.IsNotNull(failed.EndedAtUtc);
            Assert.IsNull(failed.Display);
            Assert.IsNotNull(failed.Failure);
            Assert.AreEqual(RemoteSessionFailureCodes.AuthenticationFailed, failed.Failure.Code);
            Assert.AreEqual("The remote endpoint rejected authentication.", failed.Failure.Detail);
            Assert.IsFalse(failed.Failure.Retryable);
            failedRevision = failed.Revision;
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var lifecycle = scope.ServiceProvider.GetRequiredService<IRemoteSessionLifecycleService>();
            var cleanup = await lifecycle.ReconcileDueAsync(100).ConfigureAwait(false);
            Assert.AreEqual(1, cleanup.Cleaned);
            Assert.AreEqual(0, cleanup.CleanupFailures);
        }
        Assert.AreEqual(1, runtime.RemovalCount);

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var runtimeId = $"remote-{sessionId:N}";
            var connections = scope.ServiceProvider.GetRequiredService<IRemoteSessionConnectionService>();
            var repeated = await connections.FailAsync(new FailRemoteSessionCommand(
                sessionId,
                runtimeId,
                ExpectedRevision: 1,
                RemoteSessionFailureCodes.AuthenticationFailed,
                "The remote endpoint rejected authentication.",
                Retryable: false)).ConfigureAwait(false);
            Assert.AreEqual(failedRevision + 1, repeated.Revision);
            Assert.AreEqual(RemoteSessionStates.Failed, repeated.State);
        }
    }

    [TestMethod]
    public async Task AccountUnavailableRemainsDistinctFromInvalidCredentials()
    {
        await using var database = await CreateMigratedDatabaseAsync().ConfigureAwait(false);
        var clock = new MutableTimeProvider(new DateTimeOffset(2026, 8, 5, 8, 0, 0, TimeSpan.Zero));
        var runtime = new RecordingRuntimeManager();
        var networkProfileId = Guid.Parse("88888888-8888-4888-8888-888888888888");
        using var host = CreateHost(database.ConnectionString, networkProfileId, runtime, clock);
        using var client = host.CreateClient(ClientOptions);
        var administrator = await SetupAdministratorAsync(client).ConfigureAwait(false);

        await using var scope = host.Services.CreateAsyncScope();
        var provisioned = await CreateProvisionedSessionAsync(
            scope.ServiceProvider,
            administrator.UserId,
            networkProfileId,
            clock,
            "remote-provider-account-unavailable").ConfigureAwait(false);
        var runtimeId = $"remote-{provisioned.SessionId:N}";
        var connections = scope.ServiceProvider.GetRequiredService<IRemoteSessionConnectionService>();

        var failed = await connections.FailAsync(new FailRemoteSessionCommand(
            provisioned.SessionId,
            runtimeId,
            provisioned.Revision,
            RemoteProviderFailureCodes.AccountUnavailable,
            "The target account is unavailable.",
            Retryable: false)).ConfigureAwait(false);

        Assert.AreEqual(RemoteSessionStates.Failed, failed.State);
        Assert.IsNotNull(failed.Failure);
        Assert.AreEqual(RemoteProviderFailureCodes.AccountUnavailable, failed.Failure.Code);
        Assert.AreNotEqual(RemoteSessionFailureCodes.AuthenticationFailed, failed.Failure.Code);
        Assert.AreEqual("The target account is unavailable.", failed.Failure.Detail);
        Assert.IsFalse(failed.Failure.Retryable);
    }

    [TestMethod]
    public async Task ActivityUsesServerTimeAndCoalescesFrequentWrites()
    {
        await using var database = await CreateMigratedDatabaseAsync().ConfigureAwait(false);
        var clock = new MutableTimeProvider(new DateTimeOffset(2026, 8, 4, 21, 0, 0, TimeSpan.Zero));
        var runtime = new RecordingRuntimeManager();
        var networkProfileId = Guid.Parse("77777777-7777-4777-8777-777777777777");
        using var host = CreateHost(database.ConnectionString, networkProfileId, runtime, clock);
        using var client = host.CreateClient(ClientOptions);
        var administrator = await SetupAdministratorAsync(client).ConfigureAwait(false);

        Guid sessionId;
        string runtimeId;
        long connectedRevision;
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var provisioned = await CreateProvisionedSessionAsync(
                scope.ServiceProvider,
                administrator.UserId,
                networkProfileId,
                clock,
                "remote-provider-activity").ConfigureAwait(false);
            sessionId = provisioned.SessionId;
            runtimeId = $"remote-{sessionId:N}";
            var connections = scope.ServiceProvider.GetRequiredService<IRemoteSessionConnectionService>();
            var connected = await connections.ConnectAsync(new ConnectRemoteSessionCommand(
                sessionId,
                runtimeId,
                provisioned.Revision)).ConfigureAwait(false);
            connectedRevision = connected.Revision;
            await connections.RecordActivityAsync(new RecordRemoteSessionActivityCommand(
                sessionId,
                runtimeId)).ConfigureAwait(false);
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<CoreDbContext>();
            var row = await context.RemoteSessions.AsNoTracking()
                .SingleAsync(candidate => candidate.Id == sessionId)
                .ConfigureAwait(false);
            Assert.AreEqual(connectedRevision, row.Revision);
        }

        clock.Advance(TimeSpan.FromSeconds(16));
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var connections = scope.ServiceProvider.GetRequiredService<IRemoteSessionConnectionService>();
            await connections.RecordActivityAsync(new RecordRemoteSessionActivityCommand(
                sessionId,
                runtimeId)).ConfigureAwait(false);
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<CoreDbContext>();
            var row = await context.RemoteSessions.AsNoTracking()
                .SingleAsync(candidate => candidate.Id == sessionId)
                .ConfigureAwait(false);
            Assert.AreEqual(connectedRevision + 1, row.Revision);
            Assert.AreEqual(clock.GetUtcNow(), row.LastActivityAtUtc);
        }
    }

    [TestMethod]
    public async Task InteractiveReadIssuesDisplayForConnectedSession()
    {
        await using var database = await CreateMigratedDatabaseAsync().ConfigureAwait(false);
        var clock = new MutableTimeProvider(new DateTimeOffset(2026, 8, 6, 9, 0, 0, TimeSpan.Zero));
        var runtime = new RecordingRuntimeManager();
        var networkProfileId = Guid.Parse("99999999-9999-4999-8999-999999999999");
        using var host = CreateHost(database.ConnectionString, networkProfileId, runtime, clock);
        using var client = host.CreateClient(ClientOptions);
        var administrator = await SetupAdministratorAsync(client).ConfigureAwait(false);

        await using var scope = host.Services.CreateAsyncScope();
        var provisioned = await CreateProvisionedSessionAsync(
            scope.ServiceProvider,
            administrator.UserId,
            networkProfileId,
            clock,
            "remote-provider-display").ConfigureAwait(false);
        var runtimeId = $"remote-{provisioned.SessionId:N}";
        var connections = scope.ServiceProvider.GetRequiredService<IRemoteSessionConnectionService>();
        var connected = await connections.ConnectAsync(new ConnectRemoteSessionCommand(
            provisioned.SessionId,
            runtimeId,
            provisioned.Revision)).ConfigureAwait(false);
        Assert.AreEqual(RemoteSessionStates.Connected, connected.State);
        Assert.IsNull(connected.Display);

        var broker = scope.ServiceProvider.GetRequiredService<CapabilityBroker>();
        broker.SetPackageGrants(CallerPackageId, [InteractiveSessionCapabilityContract.Name]);

        var read = await ReadInteractiveAsync(broker, administrator.UserId, provisioned.SessionId)
            .ConfigureAwait(false);

        Assert.AreEqual(RemoteSessionStates.Connected, read.State);
        Assert.IsNotNull(read.Display);
        StringAssert.StartsWith(
            read.Display.Endpoint,
            $"/api/v1/remote/sessions/{provisioned.SessionId:D}/display?");
        Assert.AreEqual(RemoteDisplayGateway.DisplayKind, read.Display.Kind);
        Assert.AreEqual(clock.GetUtcNow().AddSeconds(60), read.Display.ExpiresAtUtc);
        Assert.IsTrue(read.Revision > connected.Revision);

        var reread = await ReadInteractiveAsync(broker, administrator.UserId, provisioned.SessionId)
            .ConfigureAwait(false);

        Assert.AreEqual(read.Revision, reread.Revision);
        Assert.IsNotNull(reread.Display);
        Assert.AreEqual(read.Display.Endpoint, reread.Display.Endpoint);
    }

    private static async Task<InteractiveSessionResponse> ReadInteractiveAsync(
        CapabilityBroker broker,
        Guid ownerUserId,
        Guid sessionId)
    {
        var request = new CapabilityRequest(
            InteractiveSessionCapabilityContract.Name,
            InteractiveSessionCapabilityContract.Version,
            InteractiveSessionCapabilityContract.ReadOperation,
            Guid.NewGuid().ToString("N"),
            JsonSerializer.SerializeToElement(new ReadInteractiveSessionRequest(sessionId)),
            DateTimeOffset.UtcNow.AddSeconds(5));
        var response = await broker.InvokeAsync(CallerPackageId, ownerUserId, request).ConfigureAwait(false);
        Assert.IsTrue(response.Succeeded, response.ErrorCode);
        var snapshot = response.Payload.Deserialize<InteractiveSessionResponse>(JsonOptions);
        Assert.IsNotNull(snapshot);
        return snapshot;
    }

    private static ServerHost CreateHost(
        string connectionString,
        Guid networkProfileId,
        RecordingRuntimeManager runtime,
        MutableTimeProvider clock) =>
        new(
            connectionString,
            RuntimeSettings(networkProfileId),
            services =>
            {
                services.RemoveAll<IRemoteRuntimeManager>();
                services.AddSingleton<IRemoteRuntimeManager>(runtime);
                services.RemoveAll<TimeProvider>();
                services.AddSingleton<TimeProvider>(clock);
            });

    private static async Task<RemoteSessionResponse> CreateProvisionedSessionAsync(
        IServiceProvider services,
        Guid userId,
        Guid networkProfileId,
        MutableTimeProvider clock,
        string operationKey)
    {
        var secret = await CreateSecretAsync(services, userId, operationKey).ConfigureAwait(false);
        var sessions = services.GetRequiredService<IRemoteSessionService>();
        var request = new CreateRemoteSessionRequest(
            operationKey,
            "rdp",
            new RemoteTargetContract("server.example.test", 3389),
            secret.SecretReferenceId,
            ProfileId: null,
            networkProfileId,
            new RemoteViewportContract(1440, 900, 1m),
            IdleTimeoutSeconds: 120,
            MaximumSessionSeconds: 600,
            RequestedAtUtc: clock.GetUtcNow(),
            DeadlineUtc: clock.GetUtcNow().AddMinutes(2));
        var created = await sessions.CreateAsync(new CreateRemoteSessionCommand(
            userId,
            CallerPackageId,
            request)).ConfigureAwait(false);
        var provisioner = services.GetRequiredService<IRemoteSessionProvisioner>();
        return await provisioner.ProvisionAsync(new ProvisionRemoteSessionCommand(
            userId,
            CallerPackageId,
            created.SessionId,
            created.Revision)).ConfigureAwait(false);
    }

    private static async Task<SecretReferenceSnapshot> CreateSecretAsync(
        IServiceProvider services,
        Guid userId,
        string operationKey)
    {
        var secretService = services.GetRequiredService<ISecretReferenceService>();
        var secretBytes = Encoding.UTF8.GetBytes("test-only-remote-password");
        try
        {
            return await secretService.CreateAsync(new CreateSecretReferenceCommand(
                userId,
                SecretOwningScopeType.Package,
                CallerPackageId,
                "remote.password",
                secretBytes,
                operationKey,
                RemoteAddress: null)).ConfigureAwait(false);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(secretBytes);
        }
    }

    private static Dictionary<string, string?> RuntimeSettings(Guid networkProfileId) =>
        new()
        {
            ["Remote:Providers:0:Protocol"] = "rdp",
            ["Remote:Providers:0:ProviderPackageId"] = "de.juloc.julos.remote-provider",
            ["Remote:Providers:0:PackageVersion"] = "1.0.0",
            ["Remote:Providers:0:Image"] = "ghcr.io/juloc/julos-remote@sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
            ["Remote:Providers:0:MemoryMegabytes"] = "256",
            ["Remote:Providers:0:CpuLimit"] = "1",
            ["Remote:Providers:0:PidsLimit"] = "128",
            ["Remote:NetworkProfiles:0:NetworkProfileId"] = networkProfileId.ToString("D"),
            ["Remote:NetworkProfiles:0:Default"] = "true",
            ["Remote:NetworkProfiles:0:RuntimeNetworks:0"] = "julos-remote",
            ["Remote:NetworkProfiles:0:AllowedTargetPatterns:0"] = "*.example.test",
            ["Remote:NetworkProfiles:0:AllowedPorts:0"] = "3389",
            ["Remote:Display:ProviderEndpointTemplate"] = "ws://julos-{runtimeId}:8081/",
            ["Remote:Display:PublicOrigin"] = "https://localhost",
            ["Remote:Display:GrantLifetimeSeconds"] = "60",
        };

    private static async Task<AuthenticatedUserResponse> SetupAdministratorAsync(HttpClient client)
    {
        using var response = await client.PostAsJsonAsync(
            "/api/v1/auth/setup",
            new InitialAdministratorRequest("admin", "Administrator", AdministratorPassword)).ConfigureAwait(false);
        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<AuthenticatedUserResponse>().ConfigureAwait(false)
            ?? throw new AssertFailedException("Initial setup returned no user.");
    }

    private static async Task<PostgreSqlTestDatabase> CreateMigratedDatabaseAsync()
    {
        var database = await PostgreSqlTestDatabase.CreateAsync().ConfigureAwait(false);
        await CoreDatabaseMigrator.MigrateAsync(database.ConnectionString).ConfigureAwait(false);
        return database;
    }

    private sealed class RecordingRuntimeManager : IRemoteRuntimeManager
    {
        internal int RemovalCount { get; private set; }

        public Task<PackageRuntimeResponse> AllocateAndStartAsync(
            CreatePackageRuntimeRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new PackageRuntimeResponse(
                request.InstanceId,
                request.PackageId,
                request.PackageVersion,
                request.InstanceId,
                request.Image,
                "running",
                DateTimeOffset.UtcNow));
        }

        public Task RemoveAsync(string runtimeId, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            this.RemovalCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class MutableTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private DateTimeOffset utcNow = utcNow;

        public override DateTimeOffset GetUtcNow() => this.utcNow;

        internal void Advance(TimeSpan duration) => this.utcNow = this.utcNow.Add(duration);
    }
}
