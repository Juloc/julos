using JulOS.Application.Remote;
using JulOS.Application.Secrets;
using JulOS.Contracts.Remote;
using JulOS.Contracts.Runtime;
using JulOS.Domain.Observability;
using JulOS.Infrastructure.Packages;
using JulOS.Infrastructure.Persistence.Core;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace JulOS.Infrastructure.Tests.Packages;

[TestClass]
public sealed class InteractiveSessionCleanupServiceTests
{
    private const string CallerPackageId = "de.juloc.julos.browser";

    [TestMethod]
    public async Task RepeatedCleanupFailureCreatesOneDeduplicatedProblem()
    {
        await using var fixture = await CleanupFixture.CreateAsync(failuresBeforeSuccess: int.MaxValue);

        var first = await fixture.Service.ReconcileAsync(10);
        var second = await fixture.Service.ReconcileAsync(10);

        Assert.AreEqual(1, first.Failures);
        Assert.AreEqual(1, second.Failures);
        var problems = await fixture.Context.Problems.AsNoTracking().ToListAsync();
        Assert.HasCount(1, problems);
        Assert.AreEqual("session-cleanup-failed", problems[0].ProblemType);
        Assert.AreEqual(ProblemState.Active, problems[0].State);
        Assert.AreEqual(ProblemSeverity.Error, problems[0].Severity);
        Assert.AreEqual(2, problems[0].ObservationCount);
    }

    [TestMethod]
    public async Task CleanupRetryResolvesExistingProblemAfterRuntimeRecovers()
    {
        await using var fixture = await CleanupFixture.CreateAsync(failuresBeforeSuccess: 1);

        var failed = await fixture.Service.ReconcileAsync(10);
        Assert.AreEqual(1, failed.Failures);
        Assert.AreEqual(0, failed.Resolved);

        var recovered = await fixture.Service.ReconcileAsync(10);
        Assert.AreEqual(0, recovered.Failures);
        Assert.AreEqual(1, recovered.Resolved);

        var problem = await fixture.Context.Problems.AsNoTracking().SingleAsync();
        Assert.AreEqual(ProblemState.Resolved, problem.State);
        Assert.IsNotNull(problem.ResolvedAtUtc);
        Assert.AreEqual(1, problem.ObservationCount);
        Assert.AreEqual(1, fixture.Secrets.DeleteCount);
    }

    private sealed class CleanupFixture : IAsyncDisposable
    {
        private readonly string directory;

        private CleanupFixture(
            string directory,
            CoreDbContext context,
            InteractiveSessionCleanupService service,
            RecordingSecretService secrets)
        {
            this.directory = directory;
            this.Context = context;
            this.Service = service;
            this.Secrets = secrets;
        }

        internal CoreDbContext Context { get; }

        internal InteractiveSessionCleanupService Service { get; }

        internal RecordingSecretService Secrets { get; }

        internal static async Task<CleanupFixture> CreateAsync(int failuresBeforeSuccess)
        {
            var directory = Path.Combine(
                Path.GetTempPath(),
                "julos-interactive-cleanup-tests",
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

            var now = new DateTimeOffset(2026, 8, 20, 18, 0, 0, TimeSpan.Zero);
            var owner = Guid.Parse("11111111-1111-4111-8111-111111111111");
            var sessionId = Guid.Parse("22222222-2222-4222-8222-222222222222");
            var secretId = Guid.Parse("33333333-3333-4333-8333-333333333333");
            context.SecretReferences.Add(new SecretReferenceRow
            {
                Id = secretId,
                OwningScopeType = SecretOwningScopeType.Package,
                OwningScopeId = CallerPackageId,
                Purpose = InteractiveSessionCapabilityProvider.SecretPurpose,
                StorageProvider = "test",
                EncryptionKeyId = "test-key",
                Nonce = new byte[12],
                Ciphertext = [1],
                AuthenticationTag = new byte[16],
                CreatedAtUtc = now.AddMinutes(-2),
                Revision = 1,
            });
            context.RemoteSessions.Add(new RemoteSessionRow
            {
                Id = sessionId,
                OwnerUserId = owner,
                CallerPackageId = CallerPackageId,
                OperationKey = "cleanup-test",
                RequestIdentity = "cleanup-test-request",
                Protocol = "vnc",
                TargetHost = "julos-interactive-0123456789abcdef0123456789abcdef",
                TargetPort = 5900,
                SecretReferenceId = secretId,
                ViewportWidth = 1280,
                ViewportHeight = 800,
                DeviceScaleFactor = 1m,
                IdleTimeoutSeconds = 1800,
                MaximumSessionSeconds = 86400,
                State = RemoteSessionStates.Failed,
                CreatedAtUtc = now.AddMinutes(-2),
                UpdatedAtUtc = now.AddMinutes(-1),
                LastActivityAtUtc = now.AddMinutes(-1),
                ExpiresAtUtc = now.AddMinutes(30),
                EndedAtUtc = now.AddMinutes(-1),
                FailureCode = "interactive.test_failure",
                FailureDetail = "Test-only terminal session.",
                FailureRetryable = false,
                Revision = 1,
            });
            await context.SaveChangesAsync();

            var runtime = new RecordingRuntimeManager(failuresBeforeSuccess);
            var secrets = new RecordingSecretService(now);
            var clock = new FixedTimeProvider(now);
            var service = new InteractiveSessionCleanupService(context, runtime, secrets, clock);
            return new CleanupFixture(directory, context, service, secrets);
        }

        public async ValueTask DisposeAsync()
        {
            await this.Context.DisposeAsync();
            SqliteConnection.ClearAllPools();
            Directory.Delete(this.directory, recursive: true);
        }
    }

    private sealed class RecordingRuntimeManager(int failuresBeforeSuccess) : IRemoteRuntimeManager
    {
        private int failuresRemaining = failuresBeforeSuccess;

        public Task<PackageRuntimeResponse> AllocateAndStartAsync(
            CreatePackageRuntimeRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task RemoveAsync(string runtimeId, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (this.failuresRemaining > 0)
            {
                this.failuresRemaining--;
                throw new RemoteRuntimeManagerException(
                    "runtime.remove_failed",
                    "Test-only Runtime Manager failure.");
            }
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingSecretService(DateTimeOffset now) : ISecretReferenceService
    {
        internal int DeleteCount { get; private set; }

        public Task<SecretReferenceSnapshot> CreateAsync(
            CreateSecretReferenceCommand command,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<SecretReferenceSnapshot> ReadAsync(
            Guid secretReferenceId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

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
            return Task.FromResult(new SecretReferenceSnapshot(
                command.SecretReferenceId,
                SecretOwningScopeType.Package,
                CallerPackageId,
                InteractiveSessionCapabilityProvider.SecretPurpose,
                "test",
                now.AddMinutes(-2),
                RotatedAtUtc: null,
                DeletedAtUtc: now,
                command.Revision + 1));
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
