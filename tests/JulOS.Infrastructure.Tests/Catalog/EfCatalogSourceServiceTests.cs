using JulOS.Application.Auditing;
using JulOS.Application.Catalog;
using JulOS.Application.Concurrency;
using JulOS.Contracts.Catalog;
using JulOS.Domain.Catalog;
using JulOS.Infrastructure.Catalog;
using JulOS.Infrastructure.Identifiers;
using JulOS.Infrastructure.Persistence.Core;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace JulOS.Infrastructure.Tests.Catalog;

/// <summary>
/// CAT-002: catalog-source administration and publisher-key trust decisions against a real
/// SQLite database, so the constraints the migration installs are part of what is tested.
/// </summary>
[TestClass]
public sealed class EfCatalogSourceServiceTests : IDisposable
{
    private static readonly Guid Administrator = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-09-16T12:00:00Z", null);

    private readonly List<string> directories = [];
    private CoreDbContext context = null!;
    private RecordingAuditService auditService = null!;
    private EfCatalogSourceService service = null!;

    [TestInitialize]
    public async Task InitializeAsync()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "julos-catalog-tests",
            Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(directory);
        this.directories.Add(directory);

        var database = new CoreDatabaseConfiguration(
            CoreDatabaseProvider.Sqlite,
            $"Data Source={Path.Combine(directory, "julos.db")}");
        await CoreDatabaseMigrator.MigrateAsync(database);

        var options = new DbContextOptionsBuilder<CoreDbContext>();
        CorePersistenceServiceCollectionExtensions.Configure(options, database);
        this.context = new CoreDbContext(options.Options);
        this.auditService = new RecordingAuditService();
        this.service = new EfCatalogSourceService(
            this.context,
            new TimeOrderedIdentifierGenerator(TimeProvider.System),
            this.auditService,
            new FixedTimeProvider(Now));
    }

    /// <summary>Releases the database context the test opened.</summary>
    public void Dispose() => this.context?.Dispose();

    [TestCleanup]
    public void Cleanup()
    {
        this.Dispose();
        SqliteConnection.ClearAllPools();
        foreach (var directory in this.directories)
        {
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch (IOException)
            {
                // A file the operating system still holds open is test litter, not a failure.
            }
        }
    }

    [TestMethod]
    public async Task AnAddedSourceIsReadableAndAudited()
    {
        var added = await this.AddAsync();

        var read = await this.service.ReadAsync(added.CatalogSourceId);

        Assert.AreEqual(CatalogSourceKindNames.Https, read.Kind);
        Assert.AreEqual(CatalogSourceTrustLevelNames.Custom, read.TrustLevel);
        Assert.AreEqual(CatalogRefreshStateNames.Never, read.LastRefreshState);
        Assert.IsTrue(read.Enabled);
        Assert.IsNull(read.RemovedAtUtc);
        Assert.AreEqual(1, read.Revision);

        var record = this.auditService.Records.Single();
        Assert.AreEqual("catalog.source.add", record.Action);
        Assert.AreEqual(Administrator, record.UserId);
    }

    [TestMethod]
    public async Task ASecondLiveSourceForTheSameLocationIsRefused()
    {
        _ = await this.AddAsync();

        var failure = await Assert.ThrowsExactlyAsync<CatalogFailureException>(
            () => this.AddAsync(displayName: "Duplicate"));

        Assert.AreEqual(CatalogErrorCodes.SourceDuplicate, failure.Code);
        Assert.AreEqual(CatalogFailureReason.Duplicate, failure.Reason);
    }

    [TestMethod]
    public async Task ARemovedSourceFreesItsLocationAndStaysReadable()
    {
        var added = await this.AddAsync();

        var removed = await this.service.RemoveAsync(
            added.CatalogSourceId,
            added.Revision,
            Administrator,
            "correlation",
            null);

        Assert.AreEqual(Now, removed.RemovedAtUtc);
        Assert.IsFalse(removed.Enabled);

        var readded = await this.AddAsync(displayName: "Replacement");
        Assert.AreNotEqual(
            added.CatalogSourceId,
            readded.CatalogSourceId,
            "Re-adding a location produces a new source rather than reviving the tombstone.");

        var listed = await this.service.ListAsync(includeRemoved: false);
        Assert.AreEqual(1, listed.Count);
        var all = await this.service.ListAsync(includeRemoved: true);
        Assert.AreEqual(2, all.Count, "A tombstone stays resolvable for installed applications.");
    }

    [TestMethod]
    public async Task ARemovedSourceIsNotChangedFurther()
    {
        var added = await this.AddAsync();
        var removed = await this.service.RemoveAsync(
            added.CatalogSourceId,
            added.Revision,
            Administrator,
            "correlation",
            null);

        var failure = await Assert.ThrowsExactlyAsync<CatalogFailureException>(
            () => this.service.UpdateAsync(
                removed.CatalogSourceId,
                new UpdateCatalogSourceRequest(
                    "Renamed",
                    "https://example.test/other",
                    null,
                    CatalogSourceTrustLevelNames.Custom,
                    Enabled: true,
                    removed.Revision),
                Administrator,
                "correlation",
                null));

        Assert.AreEqual(CatalogErrorCodes.SourceRemoved, failure.Code);
        Assert.AreEqual(CatalogFailureReason.Removed, failure.Reason);
    }

    [TestMethod]
    public async Task AStaleRevisionIsRefused()
    {
        var added = await this.AddAsync();

        _ = await Assert.ThrowsExactlyAsync<ConcurrencyConflictException>(
            () => this.service.UpdateAsync(
                added.CatalogSourceId,
                new UpdateCatalogSourceRequest(
                    "Renamed",
                    added.Location,
                    null,
                    CatalogSourceTrustLevelNames.Custom,
                    Enabled: true,
                    added.Revision + 7),
                Administrator,
                "correlation",
                null));
    }

    [TestMethod]
    public async Task NeitherAnOfficialKindNorAnOfficialTrustLevelCanBeAdded()
    {
        // Otherwise an administrator could mint a second "official" source and inherit the
        // pinned key set that belongs to the real one.
        var kind = await Assert.ThrowsExactlyAsync<CatalogFailureException>(
            () => this.AddAsync(kind: CatalogSourceKindNames.Official));
        Assert.AreEqual(CatalogErrorCodes.SourceInvalid, kind.Code);

        var trustLevel = await Assert.ThrowsExactlyAsync<CatalogFailureException>(
            () => this.AddAsync(trustLevel: CatalogSourceTrustLevelNames.Official));
        Assert.AreEqual(CatalogErrorCodes.SourceInvalid, trustLevel.Code);
    }

    [TestMethod]
    public async Task AnUnknownSourceOrKeyIsNotFound()
    {
        var source = await Assert.ThrowsExactlyAsync<CatalogFailureException>(
            () => this.service.ReadAsync(Guid.CreateVersion7()));
        Assert.AreEqual(CatalogFailureReason.NotFound, source.Reason);

        var key = await Assert.ThrowsExactlyAsync<CatalogFailureException>(
            () => this.service.ReadPublisherKeyAsync(Guid.CreateVersion7()));
        Assert.AreEqual(CatalogErrorCodes.PublisherKeyNotFound, key.Code);
    }

    [TestMethod]
    public async Task ATrustDecisionRecordsItsAuthorAndClearingForgetsThem()
    {
        var source = await this.AddAsync();
        var key = await this.ObserveKeyAsync(source.CatalogSourceId);

        var trusted = await this.service.SetPublisherKeyTrustAsync(
            key.CatalogPublisherKeyId,
            new SetPublisherKeyTrustRequest(AdministratorTrustStateNames.Trusted, key.Revision),
            Administrator,
            "correlation",
            null);

        Assert.AreEqual(AdministratorTrustStateNames.Trusted, trusted.AdministratorTrustState);
        Assert.AreEqual(Administrator, trusted.AdministratorDecisionByUserId);
        Assert.AreEqual(Now, trusted.AdministratorDecisionAtUtc);

        var cleared = await this.service.SetPublisherKeyTrustAsync(
            key.CatalogPublisherKeyId,
            new SetPublisherKeyTrustRequest(AdministratorTrustStateNames.Unknown, trusted.Revision),
            Administrator,
            "correlation",
            null);

        Assert.AreEqual(AdministratorTrustStateNames.Unknown, cleared.AdministratorTrustState);
        Assert.IsNull(
            cleared.AdministratorDecisionByUserId,
            "An undecided key must not keep pointing at an administrator who no longer stands behind it.");
        Assert.IsNull(cleared.AdministratorDecisionAtUtc);

        Assert.AreEqual(
            2,
            this.auditService.Records.Count(record => record.Action == "catalog.publisher-key.decide"),
            "Every trust decision is attributable after the fact.");
    }

    [TestMethod]
    public async Task APublisherKeyResponseNeverCarriesTheKeyBytes()
    {
        var source = await this.AddAsync();
        var key = await this.ObserveKeyAsync(source.CatalogSourceId);

        var listed = await this.service.ListPublisherKeysAsync(source.CatalogSourceId);

        var single = listed.Single();
        Assert.AreEqual(key.PublicKeyFingerprint, single.PublicKeyFingerprint);
        Assert.IsFalse(
            single.GetType().GetProperties().Any(property => property.Name.Contains("Spki", StringComparison.Ordinal)),
            "The fingerprint is what an administrator compares; the key bytes are not part of the contract.");
    }

    [TestMethod]
    public async Task AnUnknownTrustDecisionNameIsRefused()
    {
        var source = await this.AddAsync();
        var key = await this.ObserveKeyAsync(source.CatalogSourceId);

        var failure = await Assert.ThrowsExactlyAsync<CatalogFailureException>(
            () => this.service.SetPublisherKeyTrustAsync(
                key.CatalogPublisherKeyId,
                new SetPublisherKeyTrustRequest("maybe", key.Revision),
                Administrator,
                "correlation",
                null));

        Assert.AreEqual(CatalogErrorCodes.PublisherKeyInvalid, failure.Code);
    }

    [TestMethod]
    public async Task AnUpdateChangesWhatAnAdministratorMayChange()
    {
        var added = await this.AddAsync();

        var updated = await this.service.UpdateAsync(
            added.CatalogSourceId,
            new UpdateCatalogSourceRequest(
                "Renamed",
                "https://example.test/other",
                null,
                CatalogSourceTrustLevelNames.AdministratorTrusted,
                Enabled: false,
                added.Revision),
            Administrator,
            "correlation",
            null);

        Assert.AreEqual("Renamed", updated.DisplayName);
        Assert.AreEqual("https://example.test/other", updated.Location);
        Assert.AreEqual(CatalogSourceTrustLevelNames.AdministratorTrusted, updated.TrustLevel);
        Assert.IsFalse(updated.Enabled);
        Assert.AreEqual(
            CatalogSourceKindNames.Https,
            updated.Kind,
            "The kind is not changeable: installed applications point at this identity.");
        Assert.AreEqual(added.Revision + 1, updated.Revision);
    }

    private Task<CatalogSourceResponse> AddAsync(
        string kind = CatalogSourceKindNames.Https,
        string displayName = "Example catalog",
        string location = "https://example.test/catalog",
        string trustLevel = CatalogSourceTrustLevelNames.Custom) =>
        this.service.AddAsync(
            new AddCatalogSourceRequest(kind, displayName, location, null, trustLevel),
            Administrator,
            "correlation",
            null);

    /// <summary>Writes one observed publisher key the way a refresh adapter will.</summary>
    private async Task<CatalogPublisherKeyResponse> ObserveKeyAsync(Guid catalogSourceId)
    {
        var key = CatalogPublisherKey.Observe(
            new CatalogPublisherKeyId(Guid.CreateVersion7()),
            catalogSourceId,
            "juloc-official",
            "official-2026-01",
            CatalogSignatureInput.Algorithm,
            "c3BraS1ieXRlcw==",
            "sha256:" + new string('a', 64),
            Now.AddYears(-1),
            null,
            sourceRevision: 4);

        _ = this.context.CatalogPublisherKeys.Add(CatalogPublisherKeyRow.FromDomain(key));
        _ = await this.context.SaveChangesAsync();
        return await this.service.ReadPublisherKeyAsync(key.Id.Value);
    }

    private sealed class RecordingAuditService : IAuditService
    {
        public List<AuditRecord> Records { get; } = [];

        public void Stage(AuditRecord record) => this.Records.Add(record);

        public Task AppendAsync(AuditRecord record, CancellationToken cancellationToken = default)
        {
            this.Records.Add(record);
            return Task.CompletedTask;
        }

        public Task<AuditPageSnapshot> QueryAsync(
            AuditQuery query,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new AuditPageSnapshot([], null));
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
