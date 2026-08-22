using System.Text.Json;

using JulOS.Application.Remote;
using JulOS.Application.Secrets;
using JulOS.Contracts.Remote;
using JulOS.Contracts.Runtime;
using JulOS.Infrastructure.Packages;
using JulOS.Infrastructure.Persistence.Core;
using JulOS.PackageSdk;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace JulOS.Infrastructure.Tests.Packages;

[TestClass]
public sealed class InteractiveSessionCapabilityProviderTests
{
    private const string CallerPackageId = "de.juloc.julos.browser";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [TestMethod]
    public async Task CreateReadAndTerminateUseTheInteractiveBoundaryEndToEnd()
    {
        await using var fixture = await Fixture.CreateAsync();

        var created = await fixture.Provider.InvokeAsync(
            Request(
                InteractiveSessionCapabilityContract.CreateOperation,
                new CreateInteractiveSessionRequest(
                    "open-1",
                    Element(new { startUrl = "https://example.test" })),
                fixture.UserId));

        Assert.IsTrue(created.Succeeded, created.ErrorCode);
        var createdSession = created.Payload.Deserialize<InteractiveSessionResponse>(JsonOptions);
        Assert.IsNotNull(createdSession);
        Assert.AreEqual(RemoteSessionStates.Connecting, createdSession!.State);
        Assert.AreEqual(1, fixture.Runtime.AllocationCount);
        Assert.AreEqual(1, fixture.Secrets.CreateCount);

        await fixture.Sessions.SetStateAsync(createdSession.SessionId, RemoteSessionStates.Connected, 3);
        var read = await fixture.Provider.InvokeAsync(
            Request(
                InteractiveSessionCapabilityContract.ReadOperation,
                new ReadInteractiveSessionRequest(createdSession.SessionId),
                fixture.UserId));

        Assert.IsTrue(read.Succeeded, read.ErrorCode);
        var readSession = read.Payload.Deserialize<InteractiveSessionResponse>(JsonOptions);
        Assert.IsNotNull(readSession);
        Assert.AreEqual(RemoteSessionStates.Connected, readSession!.State);
        Assert.IsNotNull(readSession.Display);
        Assert.StartsWith("/api/v1/remote/display/", readSession.Display!.Endpoint, StringComparison.Ordinal);

        var terminated = await fixture.Provider.InvokeAsync(
            Request(
                InteractiveSessionCapabilityContract.TerminateOperation,
                new TerminateInteractiveSessionRequest(readSession.SessionId, readSession.Revision),
                fixture.UserId));

        Assert.IsTrue(terminated.Succeeded, terminated.ErrorCode);
        var terminatedSession = terminated.Payload.Deserialize<InteractiveSessionResponse>(JsonOptions);
        Assert.IsNotNull(terminatedSession);
        Assert.AreEqual(RemoteSessionStates.Disconnected, terminatedSession!.State);
        Assert.AreEqual(1, fixture.Secrets.DeleteCount);
        Assert.AreEqual(2, fixture.Runtime.RemoveCount);
    }

    [TestMethod]
    public async Task ReusedOperationKeyWithDifferentRequestIsRejectedBeforeAnotherAllocation()
    {
        await using var fixture = await Fixture.CreateAsync();

        var first = await fixture.Provider.InvokeAsync(
            Request(
                InteractiveSessionCapabilityContract.CreateOperation,
                new CreateInteractiveSessionRequest(
                    "same-key",
                    Element(new { startUrl = "https://one.example" })),
                fixture.UserId));
        Assert.IsTrue(first.Succeeded, first.ErrorCode);

        var conflicting = await fixture.Provider.InvokeAsync(
            Request(
                InteractiveSessionCapabilityContract.CreateOperation,
                new CreateInteractiveSessionRequest(
                    "same-key",
                    Element(new { startUrl = "https://two.example" })),
                fixture.UserId));

        Assert.IsFalse(conflicting.Succeeded);
        Assert.AreEqual("interactive.idempotency_conflict", conflicting.ErrorCode);
        Assert.AreEqual(1, fixture.Runtime.AllocationCount);
        Assert.AreEqual(1, fixture.Secrets.CreateCount);
    }

    [TestMethod]
    public async Task TerminateRejectsARegularRemoteTargetWithoutCleanup()
    {
        await using var fixture = await Fixture.CreateAsync();
        var sessionId = await fixture.Sessions.SeedAsync(
            fixture.UserId,
            "regular-remote-session",
            "server.lan",
            RemoteSessionStates.Connected,
            revision: 4);

        var response = await fixture.Provider.InvokeAsync(
            Request(
                InteractiveSessionCapabilityContract.TerminateOperation,
                new TerminateInteractiveSessionRequest(sessionId, 4),
                fixture.UserId));

        Assert.IsFalse(response.Succeeded);
        Assert.AreEqual("interactive.session_not_found", response.ErrorCode);
        Assert.AreEqual(0, fixture.Runtime.RemoveCount);
        Assert.AreEqual(0, fixture.Secrets.DeleteCount);
    }

    private static CapabilityRequest Request(string operation, object payload, Guid userId) =>
        new(
            InteractiveSessionCapabilityContract.Name,
            InteractiveSessionCapabilityContract.Version,
            operation,
            "interactive-test",
            Element(payload),
            DateTimeOffset.UtcNow.AddMinutes(1),
            new CapabilityCallerContext(CallerPackageId, userId));

    private static JsonElement Element(object value) =>
        JsonSerializer.SerializeToElement(value, JsonOptions);

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly string directory;

        private Fixture(
            string directory,
            CoreDbContext context,
            InteractiveSessionCapabilityProvider provider,
            FakeRemoteSessionService sessions,
            RecordingRuntimeManager runtime,
            RecordingSecretService secrets,
            Guid userId)
        {
            this.directory = directory;
            this.Context = context;
            this.Provider = provider;
            this.Sessions = sessions;
            this.Runtime = runtime;
            this.Secrets = secrets;
            this.UserId = userId;
        }

        internal CoreDbContext Context { get; }
        internal InteractiveSessionCapabilityProvider Provider { get; }
        internal FakeRemoteSessionService Sessions { get; }
        internal RecordingRuntimeManager Runtime { get; }
        internal RecordingSecretService Secrets { get; }
        internal Guid UserId { get; }

        internal static async Task<Fixture> CreateAsync()
        {
            var directory = Path.Combine(
                Path.GetTempPath(),
                "julos-interactive-provider-tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            var database = new CoreDatabaseConfiguration(
                CoreDatabaseProvider.Sqlite,
                $"Data Source={Path.Combine(directory, "julos.db")};Cache=Shared");
            await CoreDatabaseMigrator.MigrateAsync(database);

            var options = new DbContextOptionsBuilder<CoreDbContext>();
            CorePersistenceServiceCollectionExtensions.Configure(options, database);
            var context = new CoreDbContext(options.Options);
            await context.Database.OpenConnectionAsync();
            await context.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys=OFF;");

            var now = new DateTimeOffset(2026, 8, 20, 18, 30, 0, TimeSpan.Zero);
            var userId = Guid.Parse("11111111-1111-4111-8111-111111111111");
            var sessions = new FakeRemoteSessionService(context, now);
            var runtime = new RecordingRuntimeManager(now);
            var secrets = new RecordingSecretService(now);
            var lifecycle = new FakeLifecycleService(sessions);
            var provider = new InteractiveSessionCapabilityProvider(
                context,
                new StaticPlanDispatcher(Plan()),
                new InteractiveSessionCoordinator(),
                runtime,
                new StaticRuntimePolicy(),
                sessions,
                new FakeProvisioner(sessions),
                lifecycle,
                secrets,
                new FixedTimeProvider(now));
            return new Fixture(directory, context, provider, sessions, runtime, secrets, userId);
        }

        public async ValueTask DisposeAsync()
        {
            await this.Context.DisposeAsync();
            SqliteConnection.ClearAllPools();
            Directory.Delete(this.directory, recursive: true);
        }

        private static InteractiveSessionRuntimePlan Plan() =>
            new(
                "1.0.3",
                "ghcr.io/juloc/julos-browser-runtime@sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
                new RuntimeResourceLimits(1024, 1m, 256),
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["JULOS_START_URL"] = "https://example.test",
                },
                "browser-net",
                [],
                "vnc",
                5900,
                new InteractiveSessionCredential("JULOS_VNC_PASSWORD", "12345678"),
                new RemoteViewportContract(1280, 800, 1m),
                1800,
                86400);
    }

    private sealed class StaticPlanDispatcher(InteractiveSessionRuntimePlan plan) : IPackageWorkerCommandDispatcher
    {
        public Task<PackageWorkerCommandResult> InvokeAsync(
            string packageId,
            PackageWorkerCommand command,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Assert.AreEqual(CallerPackageId, packageId);
            Assert.AreEqual(InteractiveSessionWorkerCommands.ResolvePlan, command.Name);
            return Task.FromResult(new PackageWorkerCommandResult(
                true,
                null,
                null,
                JsonSerializer.SerializeToElement(plan, JsonOptions)));
        }
    }

    private sealed class StaticRuntimePolicy : IRemoteRuntimePolicy
    {
        private static readonly Guid NetworkProfileId =
            Guid.Parse("44444444-4444-4444-8444-444444444444");

        public RemoteRuntimeSelection Resolve(
            string protocol,
            Guid? networkProfileId,
            RemoteTargetContract target)
        {
            Assert.AreEqual("vnc", protocol);
            Assert.IsNull(networkProfileId);
            Assert.StartsWith("julos-interactive-", target.Host, StringComparison.Ordinal);
            Assert.AreEqual(5900, target.Port);
            return new RemoteRuntimeSelection(
                new RemoteProviderRuntimeDefinition(
                    "vnc",
                    "de.juloc.julos.remote",
                    "1.0.0",
                    "ghcr.io/juloc/julos-remote-provider@sha256:bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
                    new RuntimeResourceLimits(512, 1m, 128)),
                new RemoteNetworkProfileDefinition(
                    NetworkProfileId,
                    true,
                    ["browser-net"],
                    ["julos-interactive-*"],
                    [5900]));
        }
    }

    private sealed class RecordingRuntimeManager(DateTimeOffset now) : IRemoteRuntimeManager
    {
        internal int AllocationCount { get; private set; }
        internal int RemoveCount { get; private set; }

        public Task<PackageRuntimeResponse> AllocateAndStartAsync(
            CreatePackageRuntimeRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            this.AllocationCount++;
            Assert.AreEqual(CallerPackageId, request.PackageId);
            Assert.HasCount(1, request.Networks);
            Assert.AreEqual("browser-net", request.Networks[0]);
            Assert.AreEqual("12345678", request.SecretEnvironment["JULOS_VNC_PASSWORD"]);
            return Task.FromResult(new PackageRuntimeResponse(
                request.InstanceId,
                request.PackageId,
                request.PackageVersion,
                request.InstanceId,
                request.Image,
                "running",
                now));
        }

        public Task RemoveAsync(string runtimeId, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            this.RemoveCount++;
            Assert.StartsWith("interactive-", runtimeId, StringComparison.Ordinal);
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingSecretService(DateTimeOffset now) : ISecretReferenceService
    {
        private SecretReferenceSnapshot? snapshot;

        internal int CreateCount { get; private set; }
        internal int DeleteCount { get; private set; }

        public Task<SecretReferenceSnapshot> CreateAsync(
            CreateSecretReferenceCommand command,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            this.CreateCount++;
            this.snapshot = new SecretReferenceSnapshot(
                Guid.Parse("33333333-3333-4333-8333-333333333333"),
                SecretOwningScopeType.Package,
                CallerPackageId,
                InteractiveSessionCapabilityProvider.SecretPurpose,
                "test",
                now,
                RotatedAtUtc: null,
                DeletedAtUtc: null,
                Revision: 1);
            return Task.FromResult(this.snapshot);
        }

        public Task<SecretReferenceSnapshot> ReadAsync(
            Guid secretReferenceId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (this.snapshot is null || this.snapshot.SecretReferenceId != secretReferenceId)
            {
                throw new SecretReferenceFailureException(SecretReferenceFailureReason.NotFound);
            }
            return Task.FromResult(this.snapshot);
        }

        public Task<SecretReferenceSnapshot> RotateAsync(
            RotateSecretReferenceCommand command,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<SecretReferenceSnapshot> DeleteAsync(
            DeleteSecretReferenceCommand command,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            this.DeleteCount++;
            Assert.IsNotNull(this.snapshot);
            this.snapshot = this.snapshot! with
            {
                DeletedAtUtc = now,
                Revision = this.snapshot.Revision + 1,
            };
            return Task.FromResult(this.snapshot);
        }
    }

    private sealed class FakeRemoteSessionService(CoreDbContext context, DateTimeOffset now) : IRemoteSessionService
    {
        private readonly Dictionary<Guid, RemoteSessionResponse> sessions = [];

        public async Task<RemoteSessionResponse> CreateAsync(
            CreateRemoteSessionCommand command,
            CancellationToken cancellationToken = default)
        {
            var sessionId = Guid.CreateVersion7();
            var response = new RemoteSessionResponse(
                sessionId,
                command.Request.OperationKey,
                $"request-{sessionId:N}",
                command.Request.Protocol,
                command.Request.Target,
                RemoteSessionStates.Requested,
                now,
                null,
                null,
                null,
                null,
                1);
            this.sessions.Add(sessionId, response);
            context.RemoteSessions.Add(ToRow(command.OwnerUserId, command.CallerPackageId, command.Request, response));
            await context.SaveChangesAsync(cancellationToken);
            return response;
        }

        public Task<RemoteSessionResponse> ReadAsync(
            ReadRemoteSessionCommand command,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(this.sessions[command.Request.SessionId]);
        }

        public Task<RemoteSessionListResponse> ListAsync(
            ListRemoteSessionsCommand command,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public async Task<RemoteSessionResponse> CancelAsync(
            CancelRemoteSessionCommand command,
            CancellationToken cancellationToken = default)
        {
            var current = this.sessions[command.Request.SessionId];
            if (current.Revision != command.Request.ExpectedRevision)
            {
                throw new RemoteSessionServiceException(RemoteSessionServiceFailureReason.ConcurrencyConflict);
            }
            var updated = current with
            {
                State = RemoteSessionStates.Cancelled,
                EndedAtUtc = now,
                Revision = current.Revision + 1,
            };
            await this.UpdateAsync(updated, cancellationToken);
            return updated;
        }

        internal async Task SetStateAsync(Guid sessionId, string state, long revision)
        {
            var current = this.sessions[sessionId];
            var updated = current with
            {
                State = state,
                ConnectedAtUtc = state == RemoteSessionStates.Connected ? now : current.ConnectedAtUtc,
                Revision = revision,
            };
            await this.UpdateAsync(updated, CancellationToken.None);
        }

        internal async Task<Guid> SeedAsync(
            Guid ownerUserId,
            string operationKey,
            string targetHost,
            string state,
            long revision)
        {
            var secretId = Guid.Parse("55555555-5555-4555-8555-555555555555");
            var request = new CreateRemoteSessionRequest(
                operationKey,
                "vnc",
                new RemoteTargetContract(targetHost, 5900),
                secretId,
                null,
                null,
                new RemoteViewportContract(1280, 800, 1m),
                1800,
                86400,
                now,
                now.AddMinutes(1));
            var sessionId = Guid.CreateVersion7();
            var response = new RemoteSessionResponse(
                sessionId,
                operationKey,
                $"request-{sessionId:N}",
                "vnc",
                request.Target,
                state,
                now,
                state == RemoteSessionStates.Connected ? now : null,
                null,
                null,
                null,
                revision);
            this.sessions.Add(sessionId, response);
            context.RemoteSessions.Add(ToRow(ownerUserId, CallerPackageId, request, response));
            await context.SaveChangesAsync();
            return sessionId;
        }

        internal async Task<RemoteSessionResponse> DisconnectAsync(
            Guid sessionId,
            long expectedRevision,
            CancellationToken cancellationToken)
        {
            var current = this.sessions[sessionId];
            if (current.Revision != expectedRevision)
            {
                throw new RemoteSessionServiceException(RemoteSessionServiceFailureReason.ConcurrencyConflict);
            }
            var updated = current with
            {
                State = RemoteSessionStates.Disconnected,
                EndedAtUtc = now,
                Revision = current.Revision + 1,
            };
            await this.UpdateAsync(updated, cancellationToken);
            return updated;
        }

        internal RemoteSessionResponse Resume(Guid sessionId, long expectedRevision)
        {
            var current = this.sessions[sessionId];
            if (current.Revision != expectedRevision)
            {
                throw new RemoteSessionServiceException(RemoteSessionServiceFailureReason.ConcurrencyConflict);
            }
            return current with
            {
                Display = new RemoteDisplayTransportResponse(
                    "graphical",
                    "1.0.0",
                    $"/api/v1/remote/display/{sessionId:D}",
                    now.AddMinutes(2)),
            };
        }

        private async Task UpdateAsync(RemoteSessionResponse response, CancellationToken cancellationToken)
        {
            this.sessions[response.SessionId] = response;
            var row = await context.RemoteSessions.SingleAsync(
                candidate => candidate.Id == response.SessionId,
                cancellationToken);
            row.State = response.State;
            row.ConnectedAtUtc = response.ConnectedAtUtc;
            row.EndedAtUtc = response.EndedAtUtc;
            row.Revision = (int)response.Revision;
            row.UpdatedAtUtc = now;
            await context.SaveChangesAsync(cancellationToken);
        }

        private static RemoteSessionRow ToRow(
            Guid ownerUserId,
            string callerPackageId,
            CreateRemoteSessionRequest request,
            RemoteSessionResponse response) =>
            new()
            {
                Id = response.SessionId,
                OwnerUserId = ownerUserId,
                CallerPackageId = callerPackageId,
                OperationKey = response.OperationKey,
                RequestIdentity = response.RequestIdentity,
                Protocol = response.Protocol,
                TargetHost = response.Target.Host,
                TargetPort = response.Target.Port,
                SecretReferenceId = request.SecretReferenceId,
                ProfileId = request.ProfileId,
                NetworkProfileId = request.NetworkProfileId,
                ViewportWidth = request.Viewport.Width,
                ViewportHeight = request.Viewport.Height,
                DeviceScaleFactor = request.Viewport.DeviceScaleFactor,
                IdleTimeoutSeconds = request.IdleTimeoutSeconds,
                MaximumSessionSeconds = request.MaximumSessionSeconds,
                State = response.State,
                CreatedAtUtc = response.CreatedAtUtc,
                UpdatedAtUtc = response.CreatedAtUtc,
                LastActivityAtUtc = response.CreatedAtUtc,
                ExpiresAtUtc = response.CreatedAtUtc.AddSeconds(request.MaximumSessionSeconds),
                ConnectedAtUtc = response.ConnectedAtUtc,
                EndedAtUtc = response.EndedAtUtc,
                Revision = (int)response.Revision,
            };
    }

    private sealed class FakeProvisioner(FakeRemoteSessionService sessions) : IRemoteSessionProvisioner
    {
        public async Task<RemoteSessionResponse> ProvisionAsync(
            ProvisionRemoteSessionCommand command,
            CancellationToken cancellationToken = default)
        {
            var current = await sessions.ReadAsync(
                new ReadRemoteSessionCommand(
                    command.OwnerUserId,
                    command.CallerPackageId,
                    new ReadRemoteSessionRequest(command.SessionId)),
                cancellationToken);
            await sessions.SetStateAsync(command.SessionId, RemoteSessionStates.Connecting, current.Revision + 1);
            return await sessions.ReadAsync(
                new ReadRemoteSessionCommand(
                    command.OwnerUserId,
                    command.CallerPackageId,
                    new ReadRemoteSessionRequest(command.SessionId)),
                cancellationToken);
        }
    }

    private sealed class FakeLifecycleService(FakeRemoteSessionService sessions)
        : IRemoteSessionLifecycleService
    {
        public Task<RemoteSessionResponse> DisconnectAsync(
            DisconnectRemoteSessionCommand command,
            CancellationToken cancellationToken = default) =>
            sessions.DisconnectAsync(
                command.Request.SessionId,
                command.Request.ExpectedRevision,
                cancellationToken);

        public Task<RemoteSessionResponse> DetachAsync(
            DetachRemoteSessionCommand command,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<RemoteSessionResponse> ResumeAsync(
            ResumeRemoteSessionCommand command,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(sessions.Resume(
                command.Request.SessionId,
                command.Request.ExpectedRevision));
        }

        public Task<RemoteLifecycleReconciliationResult> ReconcileDueAsync(
            int limit,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new RemoteLifecycleReconciliationResult(0, 0, 0, 0));
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
