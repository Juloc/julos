using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;

using JulOS.Application.Remote;
using JulOS.Application.Secrets;
using JulOS.Contracts.Authentication;
using JulOS.Contracts.Remote;
using JulOS.Contracts.Runtime;
using JulOS.Domain.Observability;
using JulOS.Infrastructure.Persistence.Core;
using JulOS.Integration.Tests.Persistence;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace JulOS.Integration.Tests.Remote;

[TestClass]
[DoNotParallelize]
public sealed class RemoteSessionLifecycleTests
{
    private const string AdministratorPassword = "Valid-Initial-Password-42!";
    private const string CallerPackageId = "de.juloc.julos.remote";
    private const string CleanupProblemType = "remote.runtime_cleanup_failed";

    private static readonly WebApplicationFactoryClientOptions ClientOptions = new()
    {
        BaseAddress = new Uri("https://localhost"),
        AllowAutoRedirect = false,
        HandleCookies = true,
    };

    [TestMethod]
    public async Task DisconnectRemovesRuntimeOnceAndIsIdempotent()
    {
        await using var database = await CreateMigratedDatabaseAsync().ConfigureAwait(false);
        var clock = new MutableTimeProvider(new DateTimeOffset(2026, 8, 4, 16, 0, 0, TimeSpan.Zero));
        var runtime = new RecordingRuntimeManager();
        var networkProfileId = Guid.Parse("33333333-3333-4333-8333-333333333333");
        using var host = CreateHost(database.ConnectionString, networkProfileId, runtime, clock);
        using var client = host.CreateClient(ClientOptions);
        var administrator = await SetupAdministratorAsync(client).ConfigureAwait(false);

        await using var scope = host.Services.CreateAsyncScope();
        var provisioned = await CreateProvisionedSessionAsync(
            scope.ServiceProvider,
            administrator.UserId,
            networkProfileId,
            clock,
            "remote-disconnect-success").ConfigureAwait(false);
        var lifecycle = scope.ServiceProvider.GetRequiredService<IRemoteSessionLifecycleService>();

        var disconnected = await lifecycle.DisconnectAsync(new DisconnectRemoteSessionCommand(
            administrator.UserId,
            CallerPackageId,
            new DisconnectRemoteSessionRequest(
                provisioned.SessionId,
                provisioned.Revision,
                "User closed the session."))).ConfigureAwait(false);

        Assert.AreEqual(RemoteSessionStates.Disconnected, disconnected.State);
        Assert.IsNotNull(disconnected.EndedAtUtc);
        Assert.IsNull(disconnected.Display);
        Assert.AreEqual(1, runtime.RemovalCount);

        var repeated = await lifecycle.DisconnectAsync(new DisconnectRemoteSessionCommand(
            administrator.UserId,
            CallerPackageId,
            new DisconnectRemoteSessionRequest(
                provisioned.SessionId,
                ExpectedRevision: 1,
                Reason: null))).ConfigureAwait(false);

        Assert.AreEqual(disconnected.Revision, repeated.Revision);
        Assert.AreEqual(RemoteSessionStates.Disconnected, repeated.State);
        Assert.AreEqual(1, runtime.RemovalCount);
    }

    [TestMethod]
    public async Task KeepActiveDetachRevokesDisplayWithoutRemovingRuntime()
    {
        await using var database = await CreateMigratedDatabaseAsync().ConfigureAwait(false);
        var clock = new MutableTimeProvider(new DateTimeOffset(2026, 8, 4, 16, 30, 0, TimeSpan.Zero));
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
            "remote-detach-keep-active").ConfigureAwait(false);
        var runtimeId = $"remote-{provisioned.SessionId:N}";
        var connections = scope.ServiceProvider.GetRequiredService<IRemoteSessionConnectionService>();
        var connected = await connections.ConnectAsync(new ConnectRemoteSessionCommand(
            provisioned.SessionId,
            runtimeId,
            provisioned.Revision)).ConfigureAwait(false);

        var context = scope.ServiceProvider.GetRequiredService<CoreDbContext>();
        var row = await context.RemoteSessions
            .SingleAsync(candidate => candidate.Id == connected.SessionId)
            .ConfigureAwait(false);
        row.DisplayKind = "graphical";
        row.DisplayContractVersion = "1.0.0";
        row.DisplayEndpoint = $"/api/v1/remote/sessions/{row.Id:D}/display";
        row.DisplayExpiresAtUtc = clock.GetUtcNow().AddMinutes(1);
        await context.SaveChangesAsync().ConfigureAwait(false);
        var lastActivity = row.LastActivityAtUtc;

        var lifecycle = scope.ServiceProvider.GetRequiredService<IRemoteSessionLifecycleService>();
        var detached = await lifecycle.DetachAsync(new DetachRemoteSessionCommand(
            administrator.UserId,
            CallerPackageId,
            new DetachRemoteSessionRequest(
                connected.SessionId,
                connected.Revision,
                RemoteWindowDetachBehaviors.KeepActive))).ConfigureAwait(false);

        Assert.AreEqual(RemoteSessionStates.Connected, detached.State);
        Assert.AreEqual(connected.Revision + 1, detached.Revision);
        Assert.IsNull(detached.Display);
        Assert.AreEqual(0, runtime.RemovalCount);
        Assert.AreEqual(runtimeId, row.RuntimeId);
        Assert.AreEqual(lastActivity, row.LastActivityAtUtc);
    }

    [TestMethod]
    public async Task DisconnectDetachUsesExistingRuntimeCleanup()
    {
        await using var database = await CreateMigratedDatabaseAsync().ConfigureAwait(false);
        var clock = new MutableTimeProvider(new DateTimeOffset(2026, 8, 4, 16, 45, 0, TimeSpan.Zero));
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
            "remote-detach-disconnect").ConfigureAwait(false);
        var lifecycle = scope.ServiceProvider.GetRequiredService<IRemoteSessionLifecycleService>();

        var detached = await lifecycle.DetachAsync(new DetachRemoteSessionCommand(
            administrator.UserId,
            CallerPackageId,
            new DetachRemoteSessionRequest(
                provisioned.SessionId,
                provisioned.Revision,
                RemoteWindowDetachBehaviors.Disconnect))).ConfigureAwait(false);

        Assert.AreEqual(RemoteSessionStates.Disconnected, detached.State);
        Assert.IsNotNull(detached.EndedAtUtc);
        Assert.IsNull(detached.Display);
        Assert.AreEqual(1, runtime.RemovalCount);
    }

    [TestMethod]
    public async Task ReconciliationExpiresIdleSession()
    {
        await using var database = await CreateMigratedDatabaseAsync().ConfigureAwait(false);
        var clock = new MutableTimeProvider(new DateTimeOffset(2026, 8, 4, 17, 0, 0, TimeSpan.Zero));
        using var host = new ServerHost(
            database.ConnectionString,
            new Dictionary<string, string?>(),
            services =>
            {
                services.RemoveAll<TimeProvider>();
                services.AddSingleton<TimeProvider>(clock);
            });
        using var client = host.CreateClient(ClientOptions);
        var administrator = await SetupAdministratorAsync(client).ConfigureAwait(false);

        Guid sessionId;
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var sessions = scope.ServiceProvider.GetRequiredService<IRemoteSessionService>();
            var created = await sessions.CreateAsync(new CreateRemoteSessionCommand(
                administrator.UserId,
                CallerPackageId,
                CreateRequest(
                    "remote-idle-expiry",
                    Guid.CreateVersion7(),
                    NetworkProfileId: null,
                    clock))).ConfigureAwait(false);
            sessionId = created.SessionId;
        }

        clock.Advance(TimeSpan.FromSeconds(121));

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var lifecycle = scope.ServiceProvider.GetRequiredService<IRemoteSessionLifecycleService>();
            var result = await lifecycle.ReconcileDueAsync(100).ConfigureAwait(false);
            Assert.AreEqual(1, result.Expired);
            Assert.AreEqual(0, result.CleanupFailures);

            var sessions = scope.ServiceProvider.GetRequiredService<IRemoteSessionService>();
            var expired = await sessions.ReadAsync(new ReadRemoteSessionCommand(
                administrator.UserId,
                CallerPackageId,
                new ReadRemoteSessionRequest(sessionId))).ConfigureAwait(false);
            Assert.AreEqual(RemoteSessionStates.Expired, expired.State);
            Assert.IsNotNull(expired.EndedAtUtc);
            Assert.IsNull(expired.Display);
        }
    }

    [TestMethod]
    public async Task CleanupFailureIsDeduplicatedAndResolvedAfterRetry()
    {
        await using var database = await CreateMigratedDatabaseAsync().ConfigureAwait(false);
        var clock = new MutableTimeProvider(new DateTimeOffset(2026, 8, 4, 18, 0, 0, TimeSpan.Zero));
        var runtime = new RecordingRuntimeManager { FailRemoval = true };
        var networkProfileId = Guid.Parse("44444444-4444-4444-8444-444444444444");
        using var host = CreateHost(database.ConnectionString, networkProfileId, runtime, clock);
        using var client = host.CreateClient(ClientOptions);
        var administrator = await SetupAdministratorAsync(client).ConfigureAwait(false);

        Guid sessionId;
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var provisioned = await CreateProvisionedSessionAsync(
                scope.ServiceProvider,
                administrator.UserId,
                networkProfileId,
                clock,
                "remote-cleanup-retry").ConfigureAwait(false);
            sessionId = provisioned.SessionId;
            var lifecycle = scope.ServiceProvider.GetRequiredService<IRemoteSessionLifecycleService>();
            var disconnecting = await lifecycle.DisconnectAsync(new DisconnectRemoteSessionCommand(
                administrator.UserId,
                CallerPackageId,
                new DisconnectRemoteSessionRequest(
                    sessionId,
                    provisioned.Revision,
                    Reason: null))).ConfigureAwait(false);
            Assert.AreEqual(RemoteSessionStates.Disconnecting, disconnecting.State);
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<CoreDbContext>();
            var firstProblem = await context.Problems.AsNoTracking()
                .SingleAsync(problem => problem.ProblemType == CleanupProblemType)
                .ConfigureAwait(false);
            Assert.AreEqual(1, firstProblem.ObservationCount);

            var lifecycle = scope.ServiceProvider.GetRequiredService<IRemoteSessionLifecycleService>();
            var retry = await lifecycle.ReconcileDueAsync(100).ConfigureAwait(false);
            Assert.AreEqual(1, retry.CleanupFailures);

            var repeatedProblem = await context.Problems.AsNoTracking()
                .SingleAsync(problem => problem.ProblemType == CleanupProblemType)
                .ConfigureAwait(false);
            Assert.AreEqual(2, repeatedProblem.ObservationCount);
            Assert.AreEqual(ProblemState.Active, repeatedProblem.State);
        }

        runtime.FailRemoval = false;

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var lifecycle = scope.ServiceProvider.GetRequiredService<IRemoteSessionLifecycleService>();
            var retry = await lifecycle.ReconcileDueAsync(100).ConfigureAwait(false);
            Assert.AreEqual(1, retry.Cleaned);
            Assert.AreEqual(0, retry.CleanupFailures);

            var sessions = scope.ServiceProvider.GetRequiredService<IRemoteSessionService>();
            var disconnected = await sessions.ReadAsync(new ReadRemoteSessionCommand(
                administrator.UserId,
                CallerPackageId,
                new ReadRemoteSessionRequest(sessionId))).ConfigureAwait(false);
            Assert.AreEqual(RemoteSessionStates.Disconnected, disconnected.State);
            Assert.IsNotNull(disconnected.EndedAtUtc);

            var context = scope.ServiceProvider.GetRequiredService<CoreDbContext>();
            var resolvedProblem = await context.Problems.AsNoTracking()
                .SingleAsync(problem => problem.ProblemType == CleanupProblemType)
                .ConfigureAwait(false);
            Assert.AreEqual(ProblemState.Resolved, resolvedProblem.State);
            Assert.IsNotNull(resolvedProblem.ResolvedAtUtc);
        }

        Assert.AreEqual(3, runtime.RemovalCount);
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
        var created = await sessions.CreateAsync(new CreateRemoteSessionCommand(
            userId,
            CallerPackageId,
            CreateRequest(operationKey, secret.SecretReferenceId, networkProfileId, clock))).ConfigureAwait(false);
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

    private static CreateRemoteSessionRequest CreateRequest(
        string operationKey,
        Guid secretReferenceId,
        Guid? NetworkProfileId,
        MutableTimeProvider clock)
    {
        var now = clock.GetUtcNow();
        return new CreateRemoteSessionRequest(
            operationKey,
            "rdp",
            new RemoteTargetContract("server.example.test", 3389),
            secretReferenceId,
            ProfileId: null,
            NetworkProfileId,
            new RemoteViewportContract(1440, 900, 1m),
            IdleTimeoutSeconds: 120,
            MaximumSessionSeconds: 600,
            RequestedAtUtc: now,
            DeadlineUtc: now.AddMinutes(2));
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
        internal bool FailRemoval { get; set; }

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
            if (this.FailRemoval)
            {
                throw new RemoteRuntimeManagerException(
                    "remote.runtime_remove_failed",
                    "Runtime cleanup failed.");
            }
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
