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
using JulOS.Infrastructure.Remote;
using JulOS.Integration.Tests.Persistence;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace JulOS.Integration.Tests.Remote;

[TestClass]
[DoNotParallelize]
public sealed class RemoteSessionServiceTests
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
    public async Task CreateIsExactlyIdempotentAndOwnershipScoped()
    {
        await using var database = await CreateMigratedDatabaseAsync().ConfigureAwait(false);
        using var host = new ServerHost(database.ConnectionString);
        using var client = host.CreateClient(ClientOptions);
        var administrator = await SetupAdministratorAsync(client).ConfigureAwait(false);

        await using var scope = host.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IRemoteSessionService>();
        var request = CreateRequest("remote-create-1", "server.example.test");
        var command = new CreateRemoteSessionCommand(
            administrator.UserId,
            CallerPackageId,
            request);

        var created = await service.CreateAsync(command).ConfigureAwait(false);
        var repeated = await service.CreateAsync(command).ConfigureAwait(false);

        Assert.AreEqual(created.SessionId, repeated.SessionId);
        Assert.AreEqual(created.RequestIdentity, repeated.RequestIdentity);
        Assert.AreEqual(RemoteSessionStates.Requested, created.State);
        Assert.AreEqual(1L, created.Revision);
        Assert.IsNull(created.Display);
        Assert.IsNull(created.Failure);

        var conflict = await Assert.ThrowsExactlyAsync<RemoteSessionServiceException>(() =>
            service.CreateAsync(command with
            {
                Request = request with
                {
                    Target = new RemoteTargetContract("other.example.test", 3389),
                },
            })).ConfigureAwait(false);
        Assert.AreEqual(RemoteSessionServiceFailureReason.IdempotencyConflict, conflict.Reason);

        var foreignPackage = await Assert.ThrowsExactlyAsync<RemoteSessionServiceException>(() =>
            service.ReadAsync(new ReadRemoteSessionCommand(
                administrator.UserId,
                "de.juloc.other",
                new ReadRemoteSessionRequest(created.SessionId)))).ConfigureAwait(false);
        Assert.AreEqual(RemoteSessionServiceFailureReason.NotFound, foreignPackage.Reason);

        var foreignUser = await Assert.ThrowsExactlyAsync<RemoteSessionServiceException>(() =>
            service.ReadAsync(new ReadRemoteSessionCommand(
                Guid.CreateVersion7(),
                CallerPackageId,
                new ReadRemoteSessionRequest(created.SessionId)))).ConfigureAwait(false);
        Assert.AreEqual(RemoteSessionServiceFailureReason.NotFound, foreignUser.Reason);
    }

    [TestMethod]
    public async Task ListCursorAndCancellationSurviveDurableReloads()
    {
        await using var database = await CreateMigratedDatabaseAsync().ConfigureAwait(false);
        using var host = new ServerHost(database.ConnectionString);
        using var client = host.CreateClient(ClientOptions);
        var administrator = await SetupAdministratorAsync(client).ConfigureAwait(false);

        Guid firstSessionId;
        Guid secondSessionId;
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<IRemoteSessionService>();
            firstSessionId = (await service.CreateAsync(new CreateRemoteSessionCommand(
                administrator.UserId,
                CallerPackageId,
                CreateRequest("remote-page-1", "one.example.test"))).ConfigureAwait(false)).SessionId;
            secondSessionId = (await service.CreateAsync(new CreateRemoteSessionCommand(
                administrator.UserId,
                CallerPackageId,
                CreateRequest("remote-page-2", "two.example.test"))).ConfigureAwait(false)).SessionId;
            _ = await service.CreateAsync(new CreateRemoteSessionCommand(
                administrator.UserId,
                CallerPackageId,
                CreateRequest("remote-page-3", "three.example.test"))).ConfigureAwait(false);
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<IRemoteSessionService>();
            var firstPage = await service.ListAsync(new ListRemoteSessionsCommand(
                administrator.UserId,
                CallerPackageId,
                new ListRemoteSessionsRequest([], 2, Cursor: null))).ConfigureAwait(false);
            Assert.AreEqual(2, firstPage.Sessions.Count);
            Assert.IsNotNull(firstPage.NextCursor);

            var secondPage = await service.ListAsync(new ListRemoteSessionsCommand(
                administrator.UserId,
                CallerPackageId,
                new ListRemoteSessionsRequest([], 2, firstPage.NextCursor))).ConfigureAwait(false);
            Assert.AreEqual(1, secondPage.Sessions.Count);
            Assert.IsNull(secondPage.NextCursor);
            var allIds = firstPage.Sessions.Select(session => session.SessionId)
                .Concat(secondPage.Sessions.Select(session => session.SessionId))
                .ToArray();
            Assert.AreEqual(3, allIds.Distinct().Count());

            var first = await service.ReadAsync(new ReadRemoteSessionCommand(
                administrator.UserId,
                CallerPackageId,
                new ReadRemoteSessionRequest(firstSessionId))).ConfigureAwait(false);
            var cancelled = await service.CancelAsync(new CancelRemoteSessionCommand(
                administrator.UserId,
                CallerPackageId,
                new CancelRemoteSessionRequest(
                    firstSessionId,
                    "remote-cancel-1",
                    first.Revision,
                    "No longer needed."))).ConfigureAwait(false);
            Assert.AreEqual(RemoteSessionStates.Cancelled, cancelled.State);
            Assert.AreEqual(2L, cancelled.Revision);
            Assert.IsNotNull(cancelled.EndedAtUtc);

            var repeated = await service.CancelAsync(new CancelRemoteSessionCommand(
                administrator.UserId,
                CallerPackageId,
                new CancelRemoteSessionRequest(
                    firstSessionId,
                    "remote-cancel-repeat",
                    ExpectedRevision: 1,
                    Reason: null))).ConfigureAwait(false);
            Assert.AreEqual(cancelled.Revision, repeated.Revision);
            Assert.AreEqual(RemoteSessionStates.Cancelled, repeated.State);

            var stale = await Assert.ThrowsExactlyAsync<RemoteSessionServiceException>(() =>
                service.CancelAsync(new CancelRemoteSessionCommand(
                    administrator.UserId,
                    CallerPackageId,
                    new CancelRemoteSessionRequest(
                        secondSessionId,
                        "remote-cancel-stale",
                        ExpectedRevision: 99,
                        Reason: null)))).ConfigureAwait(false);
            Assert.AreEqual(RemoteSessionServiceFailureReason.ConcurrencyConflict, stale.Reason);
        }
    }

    [TestMethod]
    public async Task ProvisionAuthorizesSecretAndAllocatesExactRuntimeOnce()
    {
        await using var database = await CreateMigratedDatabaseAsync().ConfigureAwait(false);
        var runtime = new RecordingRuntimeManager();
        var networkProfileId = Guid.Parse("11111111-1111-4111-8111-111111111111");
        using var host = new ServerHost(
            database.ConnectionString,
            RuntimeSettings(networkProfileId),
            services =>
            {
                services.RemoveAll<IRemoteRuntimeManager>();
                services.AddSingleton<IRemoteRuntimeManager>(runtime);
            });
        using var client = host.CreateClient(ClientOptions);
        var administrator = await SetupAdministratorAsync(client).ConfigureAwait(false);

        await using var scope = host.Services.CreateAsyncScope();
        var secretService = scope.ServiceProvider.GetRequiredService<ISecretReferenceService>();
        var secretBytes = Encoding.UTF8.GetBytes("test-only-remote-password");
        SecretReferenceSnapshot secret;
        try
        {
            secret = await secretService.CreateAsync(new CreateSecretReferenceCommand(
                administrator.UserId,
                SecretOwningScopeType.Package,
                CallerPackageId,
                "remote.password",
                secretBytes,
                "remote-provision-test",
                RemoteAddress: null)).ConfigureAwait(false);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(secretBytes);
        }

        var service = scope.ServiceProvider.GetRequiredService<IRemoteSessionService>();
        var request = CreateRequest("remote-provision-1", "server.example.test") with
        {
            SecretReferenceId = secret.SecretReferenceId,
            NetworkProfileId = networkProfileId,
        };
        var created = await service.CreateAsync(new CreateRemoteSessionCommand(
            administrator.UserId,
            CallerPackageId,
            request)).ConfigureAwait(false);

        var provisioner = scope.ServiceProvider.GetRequiredService<IRemoteSessionProvisioner>();
        var provisioned = await provisioner.ProvisionAsync(new ProvisionRemoteSessionCommand(
            administrator.UserId,
            CallerPackageId,
            created.SessionId,
            created.Revision)).ConfigureAwait(false);
        var repeated = await provisioner.ProvisionAsync(new ProvisionRemoteSessionCommand(
            administrator.UserId,
            CallerPackageId,
            created.SessionId,
            created.Revision)).ConfigureAwait(false);

        Assert.AreEqual(RemoteSessionStates.Connecting, provisioned.State);
        Assert.AreEqual(3L, provisioned.Revision);
        Assert.AreEqual(provisioned, repeated);
        Assert.AreEqual(1, runtime.AllocationCount);
        var allocated = runtime.LastRequest
            ?? throw new AssertFailedException("Runtime allocation request was not captured.");
        var runtimeId = $"remote-{created.SessionId:N}";
        Assert.AreEqual(runtimeId, allocated.InstanceId);
        Assert.AreEqual("de.juloc.julos.remote-provider", allocated.PackageId);
        Assert.AreEqual("julos-remote", allocated.Networks.Single());
        Assert.AreEqual(
            "https://localhost/api/v1/internal/remote/provider-events",
            allocated.Environment["JULOS_REMOTE_CALLBACK_ENDPOINT"]);
        Assert.AreEqual("3", allocated.Environment["JULOS_REMOTE_EXPECTED_REVISION"]);
        Assert.IsFalse(allocated.Environment.Keys.Any(key =>
            key.Contains("SECRET", StringComparison.OrdinalIgnoreCase)
            || key.Contains("PASSWORD", StringComparison.OrdinalIgnoreCase)
            || key.Contains("TOKEN", StringComparison.OrdinalIgnoreCase)));
        Assert.AreEqual(2, allocated.SecretEnvironment.Count);
        var callbackToken = allocated.SecretEnvironment["JULOS_REMOTE_CALLBACK_TOKEN"];
        var callbackAuthenticator = scope.ServiceProvider
            .GetRequiredService<RemoteProviderCallbackAuthenticator>();
        Assert.IsTrue(callbackAuthenticator.Authenticate(
            created.SessionId,
            runtimeId,
            callbackToken));
        var targetCredential = allocated.SecretEnvironment["JULOS_REMOTE_TARGET_CREDENTIAL"];
        Assert.AreEqual(
            "test-only-remote-password",
            Encoding.UTF8.GetString(Convert.FromBase64String(targetCredential)));
    }

    [TestMethod]
    public async Task ForeignSecretFailsBeforeRuntimeAllocation()
    {
        await using var database = await CreateMigratedDatabaseAsync().ConfigureAwait(false);
        var runtime = new RecordingRuntimeManager();
        var networkProfileId = Guid.Parse("22222222-2222-4222-8222-222222222222");
        using var host = new ServerHost(
            database.ConnectionString,
            RuntimeSettings(networkProfileId),
            services =>
            {
                services.RemoveAll<IRemoteRuntimeManager>();
                services.AddSingleton<IRemoteRuntimeManager>(runtime);
            });
        using var client = host.CreateClient(ClientOptions);
        var administrator = await SetupAdministratorAsync(client).ConfigureAwait(false);

        await using var scope = host.Services.CreateAsyncScope();
        var secretService = scope.ServiceProvider.GetRequiredService<ISecretReferenceService>();
        var secretBytes = Encoding.UTF8.GetBytes("test-only-foreign-password");
        SecretReferenceSnapshot secret;
        try
        {
            secret = await secretService.CreateAsync(new CreateSecretReferenceCommand(
                administrator.UserId,
                SecretOwningScopeType.Package,
                "de.juloc.other",
                "remote.password",
                secretBytes,
                "remote-foreign-secret-test",
                RemoteAddress: null)).ConfigureAwait(false);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(secretBytes);
        }

        var service = scope.ServiceProvider.GetRequiredService<IRemoteSessionService>();
        var request = CreateRequest("remote-provision-foreign", "server.example.test") with
        {
            SecretReferenceId = secret.SecretReferenceId,
            NetworkProfileId = networkProfileId,
        };
        var created = await service.CreateAsync(new CreateRemoteSessionCommand(
            administrator.UserId,
            CallerPackageId,
            request)).ConfigureAwait(false);
        var provisioner = scope.ServiceProvider.GetRequiredService<IRemoteSessionProvisioner>();
        var failed = await provisioner.ProvisionAsync(new ProvisionRemoteSessionCommand(
            administrator.UserId,
            CallerPackageId,
            created.SessionId,
            created.Revision)).ConfigureAwait(false);

        Assert.AreEqual(RemoteSessionStates.Failed, failed.State);
        Assert.AreEqual(RemoteSessionFailureCodes.CredentialUnavailable, failed.Failure?.Code);
        Assert.AreEqual(0, runtime.AllocationCount);
    }

    private static Dictionary<string, string?> RuntimeSettings(Guid networkProfileId) =>
        new Dictionary<string, string?>
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

    private sealed class RecordingRuntimeManager : IRemoteRuntimeManager
    {
        internal int AllocationCount { get; private set; }

        internal CreatePackageRuntimeRequest? LastRequest { get; private set; }

        public Task<PackageRuntimeResponse> AllocateAndStartAsync(
            CreatePackageRuntimeRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            this.AllocationCount++;
            this.LastRequest = request;
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
            return Task.CompletedTask;
        }
    }

    private static CreateRemoteSessionRequest CreateRequest(string operationKey, string host)
    {
        var now = DateTimeOffset.UtcNow;
        return new CreateRemoteSessionRequest(
            operationKey,
            "rdp",
            new RemoteTargetContract(host, 3389),
            Guid.CreateVersion7(),
            ProfileId: null,
            NetworkProfileId: null,
            new RemoteViewportContract(1440, 900, 1m),
            IdleTimeoutSeconds: 120,
            MaximumSessionSeconds: 600,
            RequestedAtUtc: now,
            DeadlineUtc: now.AddMinutes(2));
    }

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
}
