using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;

using JulOS.Application.Remote;
using JulOS.Application.Secrets;
using JulOS.Contracts.Authentication;
using JulOS.Contracts.Remote;
using JulOS.Contracts.Runtime;
using JulOS.Infrastructure.Persistence.Core;
using JulOS.Integration.Tests.Persistence;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace JulOS.Integration.Tests.Remote;

[TestClass]
[DoNotParallelize]
public sealed class RemoteSessionResumeTests
{
    private const string AdministratorPassword = "Valid-Initial-Password-42!";
    private const string CallerPackageId = "de.juloc.julos.remote";

    private static readonly WebApplicationFactoryClientOptions ClientOptions = new()
    {
        BaseAddress = new Uri("https://localhost"),
        AllowAutoRedirect = false,
        HandleCookies = true,
    };

    [TestMethod]
    public async Task ActiveResumeClearsDisplayAndKeepsRuntimeAndActivity()
    {
        await using var database = await CreateMigratedDatabaseAsync().ConfigureAwait(false);
        var clock = new MutableTimeProvider(new DateTimeOffset(2026, 8, 4, 21, 0, 0, TimeSpan.Zero));
        var runtime = new RecordingRuntimeManager();
        var networkProfileId = Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa");
        using var host = CreateHost(database.ConnectionString, networkProfileId, runtime, clock);
        using var client = host.CreateClient(ClientOptions);
        var administrator = await SetupAdministratorAsync(client).ConfigureAwait(false);

        await using var scope = host.Services.CreateAsyncScope();
        var provisioned = await CreateProvisionedSessionAsync(
            scope.ServiceProvider,
            administrator.UserId,
            networkProfileId,
            clock,
            "remote-resume-active").ConfigureAwait(false);
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

        clock.Advance(TimeSpan.FromSeconds(5));
        var lifecycle = scope.ServiceProvider.GetRequiredService<IRemoteSessionLifecycleService>();
        var resumed = await lifecycle.ResumeAsync(new ResumeRemoteSessionCommand(
            administrator.UserId,
            CallerPackageId,
            new ResumeRemoteSessionRequest(connected.SessionId, connected.Revision))).ConfigureAwait(false);

        Assert.AreEqual(RemoteSessionStates.Connected, resumed.State);
        Assert.AreEqual(connected.Revision + 1, resumed.Revision);
        Assert.IsNull(resumed.Display);
        Assert.AreEqual(0, runtime.RemovalCount);
        Assert.AreEqual(runtimeId, row.RuntimeId);
        Assert.AreEqual(lastActivity, row.LastActivityAtUtc);
        Assert.AreEqual(clock.GetUtcNow(), row.UpdatedAtUtc);
    }

    [TestMethod]
    public async Task TerminalSessionCannotResume()
    {
        await using var database = await CreateMigratedDatabaseAsync().ConfigureAwait(false);
        var clock = new MutableTimeProvider(new DateTimeOffset(2026, 8, 4, 22, 0, 0, TimeSpan.Zero));
        var runtime = new RecordingRuntimeManager();
        var networkProfileId = Guid.Parse("bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb");
        using var host = CreateHost(database.ConnectionString, networkProfileId, runtime, clock);
        using var client = host.CreateClient(ClientOptions);
        var administrator = await SetupAdministratorAsync(client).ConfigureAwait(false);

        await using var scope = host.Services.CreateAsyncScope();
        var provisioned = await CreateProvisionedSessionAsync(
            scope.ServiceProvider,
            administrator.UserId,
            networkProfileId,
            clock,
            "remote-resume-terminal").ConfigureAwait(false);
        var lifecycle = scope.ServiceProvider.GetRequiredService<IRemoteSessionLifecycleService>();
        var disconnected = await lifecycle.DisconnectAsync(new DisconnectRemoteSessionCommand(
            administrator.UserId,
            CallerPackageId,
            new DisconnectRemoteSessionRequest(
                provisioned.SessionId,
                provisioned.Revision,
                "Session finished."))).ConfigureAwait(false);

        var failure = await Assert.ThrowsExactlyAsync<RemoteSessionServiceException>(() =>
            lifecycle.ResumeAsync(new ResumeRemoteSessionCommand(
                administrator.UserId,
                CallerPackageId,
                new ResumeRemoteSessionRequest(
                    disconnected.SessionId,
                    disconnected.Revision)))).ConfigureAwait(false);

        Assert.AreEqual(RemoteSessionServiceFailureReason.InvalidTransition, failure.Reason);
        Assert.AreEqual(1, runtime.RemovalCount);
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
