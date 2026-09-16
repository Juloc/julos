using System.Security.Cryptography;
using System.Text;

using JulOS.Application.Auditing;
using JulOS.Application.Catalog;
using JulOS.Application.Operations;
using JulOS.Application.Secrets;
using JulOS.Contracts.Catalog;
using JulOS.Domain.Catalog;
using JulOS.Infrastructure.Catalog;
using JulOS.Infrastructure.Identifiers;
using JulOS.Infrastructure.Persistence.Core;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace JulOS.Infrastructure.Tests.Catalog;

/// <summary>
/// CAT-002: what a refresh does to the cached catalog, and what a failed one may not do.
/// </summary>
/// <remarks>
/// A real local catalog directory and a real SQLite database, because the behaviour under
/// test is exactly the interaction between what a source publishes and what is persisted.
/// </remarks>
[TestClass]
public sealed class EfCatalogRefreshServiceTests : IDisposable
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-09-16T12:00:00Z", null);

    private readonly List<string> directories = [];
    private CoreDbContext context = null!;
    private EfCatalogRefreshService service = null!;
    private string root = null!;

    [TestInitialize]
    public async Task InitializeAsync()
    {
        var directory = this.CreateDirectory("julos-catalog-refresh");
        this.root = this.CreateDirectory("julos-catalog-source");

        var database = new CoreDatabaseConfiguration(
            CoreDatabaseProvider.Sqlite,
            $"Data Source={Path.Combine(directory, "julos.db")}");
        await CoreDatabaseMigrator.MigrateAsync(database);

        var options = new DbContextOptionsBuilder<CoreDbContext>();
        CorePersistenceServiceCollectionExtensions.Configure(options, database);
        this.context = new CoreDbContext(options.Options);
        this.service = new EfCatalogRefreshService(
            this.context,
            [new LocalCatalogSourceReader()],
            new RefusingLeaseService(),
            new UnusedOperationService(),
            new UnusedDispatcher(),
            new TimeOrderedIdentifierGenerator(TimeProvider.System),
            new DiscardingAuditService(),
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
    public async Task AFirstRefreshCachesEveryEntryAndMarksTheSourceFresh()
    {
        this.PublishCatalog(("home-assistant", "2026.8.0"));
        var source = await this.AddSourceAsync();

        var result = await this.service.RefreshAsync(source.Id, Guid.CreateVersion7());

        Assert.IsTrue(result.Succeeded);
        Assert.AreEqual(1, result.SourceRevision);
        Assert.AreEqual(1, result.EntryCount);

        var cached = await this.context.CatalogEntryCache.SingleAsync();
        Assert.AreEqual("home-assistant", cached.AppId);
        Assert.AreEqual(
            CatalogSignatureState.NotSigned,
            cached.SignatureState,
            "A definition with no signature beside it is unsigned, not invalid.");
        Assert.AreEqual(64, cached.DefinitionDigest.Length);
        Assert.IsTrue(cached.Definition.StartsWith('{'));

        var reloaded = await this.ReadSourceAsync(source.Id);
        Assert.AreEqual(CatalogRefreshState.Fresh, reloaded.LastRefreshState);
        Assert.AreEqual("community.example", reloaded.SourceIdentity);
        Assert.IsNull(reloaded.LastFailureCode);
    }

    [TestMethod]
    public async Task AFailedRefreshKeepsTheLastValidCatalogAndMarksItStale()
    {
        this.PublishCatalog(("home-assistant", "2026.8.0"));
        var source = await this.AddSourceAsync();
        _ = await this.service.RefreshAsync(source.Id, Guid.CreateVersion7());

        // The index still declares the old digest, so the definition no longer matches it.
        var definitionPath = Path.Combine(this.root, "apps", "home-assistant", "2026.8.0", "app.json");
        await File.WriteAllTextAsync(definitionPath, Definition("home-assistant", "2026.8.1"));

        var result = await this.service.RefreshAsync(source.Id, Guid.CreateVersion7());

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual(CatalogRefreshException.IntegrityMismatch, result.FailureCode);

        var reloaded = await this.ReadSourceAsync(source.Id);
        Assert.AreEqual(CatalogRefreshState.Stale, reloaded.LastRefreshState);
        Assert.AreEqual(1, reloaded.LastSuccessfulRevision, "A failed refresh never advances the revision.");

        var cached = await this.context.CatalogEntryCache.AsNoTracking().SingleAsync();
        Assert.AreEqual(
            "2026.8.0",
            cached.Version,
            "The previous catalog is still served; a partially read source never replaces it.");
    }

    [TestMethod]
    public async Task AnUnreadableEntryLeavesNoPartialCache()
    {
        // The second entry is declared but never published, so the refresh fails after the
        // first one was already read and verified.
        this.PublishCatalog(("home-assistant", "2026.8.0"), ("missing", "1.0.0"));
        Directory.Delete(Path.Combine(this.root, "apps", "missing"), recursive: true);
        var source = await this.AddSourceAsync();

        var result = await this.service.RefreshAsync(source.Id, Guid.CreateVersion7());

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual(0, await this.context.CatalogEntryCache.CountAsync());

        var reloaded = await this.ReadSourceAsync(source.Id);
        Assert.AreEqual(
            CatalogRefreshState.Never,
            reloaded.LastRefreshState,
            "There is no previous catalog to call stale.");
    }

    [TestMethod]
    public async Task ASourceThatChangesTheIdentityItClaimsIsRefused()
    {
        this.PublishCatalog(("home-assistant", "2026.8.0"));
        var source = await this.AddSourceAsync();
        _ = await this.service.RefreshAsync(source.Id, Guid.CreateVersion7());

        this.PublishCatalog(sourceId: "someone.else", entries: [("home-assistant", "2026.8.0")]);

        var result = await this.service.RefreshAsync(source.Id, Guid.CreateVersion7());

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual(
            "catalog.definition_invalid",
            result.FailureCode,
            "Adopting a new identity would rewrite the meaning of everything installed from the source.");
    }

    [TestMethod]
    public async Task AnUnchangedSourceKeepsItsRevisionAndItsCache()
    {
        this.PublishCatalog(("home-assistant", "2026.8.0"));
        var source = await this.AddSourceAsync();
        _ = await this.service.RefreshAsync(source.Id, Guid.CreateVersion7());
        var cachedId = (await this.context.CatalogEntryCache.AsNoTracking().SingleAsync()).Id;

        var result = await this.service.RefreshAsync(source.Id, Guid.CreateVersion7());

        Assert.IsTrue(result.Succeeded);
        Assert.AreEqual(1, result.SourceRevision, "Identical content is not a new generation.");
        Assert.AreEqual(
            cachedId,
            (await this.context.CatalogEntryCache.AsNoTracking().SingleAsync()).Id,
            "Rewriting identical rows would only churn their revisions.");
    }

    [TestMethod]
    public async Task APublishedKeySetIsObservedButNotTrusted()
    {
        this.PublishCatalog(entries: [("home-assistant", "2026.8.0")], withKeySet: true);
        var source = await this.AddSourceAsync();

        var result = await this.service.RefreshAsync(source.Id, Guid.CreateVersion7());

        Assert.IsTrue(result.Succeeded);
        var key = await this.context.CatalogPublisherKeys.AsNoTracking().SingleAsync();
        Assert.AreEqual("official-2026-01", key.KeyId);
        Assert.AreEqual(
            AdministratorTrustState.Unknown,
            key.AdministratorTrustState,
            "Publishing a key is not a decision about it; reading a catalog over TLS is not publisher trust.");
        Assert.AreEqual(1, key.FirstObservedSourceRevision);
    }

    [TestMethod]
    public async Task ARemovedOrDisabledSourceIsNotRefreshed()
    {
        this.PublishCatalog(("home-assistant", "2026.8.0"));
        var source = await this.AddSourceAsync();
        source.Enabled = false;
        _ = await this.context.SaveChangesAsync();

        var failure = await Assert.ThrowsExactlyAsync<CatalogFailureException>(
            () => this.service.RefreshAsync(source.Id, Guid.CreateVersion7()));

        Assert.AreEqual(CatalogErrorCodes.SourceInvalid, failure.Code);
    }

    [TestMethod]
    public async Task AnIndexNamingAPathOutsideTheSourceIsRefused()
    {
        this.PublishCatalog(("home-assistant", "2026.8.0"));
        var index = File.ReadAllText(Path.Combine(this.root, "catalog.json"))
            .Replace(
                "apps/home-assistant/2026.8.0/app.json",
                "../escape.json",
                StringComparison.Ordinal);
        await File.WriteAllTextAsync(Path.Combine(this.root, "catalog.json"), index);
        var source = await this.AddSourceAsync();

        var result = await this.service.RefreshAsync(source.Id, Guid.CreateVersion7());

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual("catalog.definition_invalid", result.FailureCode);
    }

    [TestMethod]
    public async Task TheCachedCatalogIsReadableAndCarriesTheRefreshState()
    {
        this.PublishCatalog(("home-assistant", "2026.8.0"), ("hermes", "1.0.0"));
        var source = await this.AddSourceAsync();
        _ = await this.service.RefreshAsync(source.Id, Guid.CreateVersion7());
        var applications = new EfCatalogApplicationService(this.context);

        var listed = await applications.ListAsync(catalogSourceId: null);

        Assert.AreEqual(2, listed.Count);
        Assert.AreEqual("community.example", listed[0].SourceIdentity);
        Assert.AreEqual(CatalogRefreshStateNames.Fresh, listed[0].SourceRefreshState);
        Assert.AreEqual(
            CatalogSignatureStateNames.NotSigned,
            listed[0].Versions.Single().SignatureState);

        var detail = await applications.ReadAsync(source.Id, "hermes", version: null);
        Assert.AreEqual("1.0.0", detail.Version);
        Assert.AreEqual(
            "hermes",
            detail.Definition.GetProperty("appId").GetString(),
            "The served definition is the canonical one the digest was taken over.");
    }

    [TestMethod]
    public async Task AStaleCatalogSaysSoRatherThanLookingCurrent()
    {
        this.PublishCatalog(("home-assistant", "2026.8.0"));
        var source = await this.AddSourceAsync();
        _ = await this.service.RefreshAsync(source.Id, Guid.CreateVersion7());

        var definitionPath = Path.Combine(this.root, "apps", "home-assistant", "2026.8.0", "app.json");
        await File.WriteAllTextAsync(definitionPath, Definition("home-assistant", "2026.8.1"));
        _ = await this.service.RefreshAsync(source.Id, Guid.CreateVersion7());

        var listed = await new EfCatalogApplicationService(this.context).ListAsync(catalogSourceId: null);

        Assert.AreEqual(
            CatalogRefreshStateNames.Stale,
            listed.Single().SourceRefreshState,
            "A user looking at a catalog is entitled to know it is not current.");
    }

    [TestMethod]
    public async Task ARemovedSourceIsNotOfferedToInstallFrom()
    {
        this.PublishCatalog(("home-assistant", "2026.8.0"));
        var source = await this.AddSourceAsync();
        _ = await this.service.RefreshAsync(source.Id, Guid.CreateVersion7());

        var domain = source.ToDomain();
        domain.Remove(Now);
        source.DeletedAtUtc = domain.DeletedAtUtc;
        source.Enabled = domain.Enabled;
        source.Revision = domain.Revision.Value;
        _ = await this.context.SaveChangesAsync();

        var applications = new EfCatalogApplicationService(this.context);
        Assert.AreEqual(0, (await applications.ListAsync(catalogSourceId: null)).Count);

        var failure = await Assert.ThrowsExactlyAsync<CatalogFailureException>(
            () => applications.ReadAsync(source.Id, "home-assistant", version: null));
        Assert.AreEqual(CatalogErrorCodes.SourceNotFound, failure.Code);
    }

    [TestMethod]
    public async Task AnHttpsSourceRefusesPlainHttpAndARelativeLocation()
    {
        // Plain HTTP is refused rather than downgraded to: a catalog read over it can be
        // rewritten in flight by anything on the path.
        using var client = HttpsCatalogSourceReader.CreateClient();
        var reader = new HttpsCatalogSourceReader(client);

        foreach (var location in new[] { "http://catalog.example.test/", "catalog.example.test", "file:///tmp" })
        {
            var failure = await Assert.ThrowsExactlyAsync<CatalogRefreshException>(
                () => reader.OpenAsync(
                    new CatalogSourceLocation(CatalogSourceKind.Https, location, null),
                    null));
            Assert.AreEqual(CatalogRefreshException.SourceUnavailable, failure.Code);
        }
    }

    [TestMethod]
    public async Task ALocalSourceRefusesALinkAndAPathOutsideItsDirectory()
    {
        var reader = new LocalCatalogSourceReader();
        await using var snapshot = await reader.OpenAsync(
            new CatalogSourceLocation(CatalogSourceKind.Local, this.root, null),
            null);

        var escape = await Assert.ThrowsExactlyAsync<CatalogRefreshException>(
            () => snapshot.ReadAsync("../escape.json"));
        Assert.AreEqual(CatalogRefreshException.SourceUnavailable, escape.Code);

        Assert.IsNull(
            await snapshot.TryReadAsync("catalog.json"),
            "A file the source does not publish is absent, not a failure.");
    }

    private async Task<CatalogSourceRow> AddSourceAsync()
    {
        var source = CatalogSource.Add(
            new CatalogSourceId(Guid.CreateVersion7()),
            CatalogSourceKind.Local,
            "Example catalog",
            this.root,
            CatalogSourceTrustLevel.Custom);
        var row = CatalogSourceRow.FromDomain(source);
        _ = this.context.CatalogSources.Add(row);
        _ = await this.context.SaveChangesAsync();
        return row;
    }

    private async Task<CatalogSourceRow> ReadSourceAsync(Guid catalogSourceId)
    {
        this.context.ChangeTracker.Clear();
        return await this.context.CatalogSources.AsNoTracking().SingleAsync(row => row.Id == catalogSourceId);
    }

    /// <summary>Writes a complete catalog bundle into the source directory.</summary>
    private void PublishCatalog(params (string AppId, string Version)[] entries) =>
        this.PublishCatalog("community.example", entries, withKeySet: false);

    private void PublishCatalog(
        string sourceId = "community.example",
        (string AppId, string Version)[]? entries = null,
        bool withKeySet = false)
    {
        entries ??= [];
        var declared = new List<string>();
        foreach (var (appId, version) in entries)
        {
            var directory = Path.Combine(this.root, "apps", appId, version);
            _ = Directory.CreateDirectory(directory);
            var definition = Definition(appId, version);
            File.WriteAllText(Path.Combine(directory, "app.json"), definition);
            declared.Add(
                $$"""
                  {
                    "appId": "{{appId}}",
                    "version": "{{version}}",
                    "path": "apps/{{appId}}/{{version}}/app.json",
                    "sha256": "{{Sha256(definition)}}"
                  }
                  """);
        }

        var keySet = "null";
        if (withKeySet)
        {
            var keys = KeySet();
            File.WriteAllText(Path.Combine(this.root, "keys.json"), keys);
            keySet = $$"""{ "path": "keys.json", "sha256": "{{Sha256(keys)}}" }""";
        }

        File.WriteAllText(
            Path.Combine(this.root, "catalog.json"),
            $$"""
              {
                "schema": "app-catalog-index.v1",
                "sourceId": "{{sourceId}}",
                "generatedAtUtc": "2026-09-16T10:00:00Z",
                "keySet": {{keySet}},
                "entries": [{{string.Join(",", declared)}}]
              }
              """);
    }

    private static string Definition(string appId, string version) =>
        $$"""
          {
            "schema": "app-manifest.v1",
            "appId": "{{appId}}",
            "version": "{{version}}",
            "name": { "en": "Example", "de": "Beispiel" },
            "description": { "en": "Example", "de": "Beispiel" },
            "architectures": ["linux/amd64"],
            "deliveryOptions": [
              {
                "key": "connect",
                "kind": "connection",
                "connectionKind": "example-api",
                "endpointPolicy": "https-or-private-http"
              }
            ]
          }
          """;

    private static string KeySet() =>
        $$"""
          {
            "schema": "app-catalog-keyset.v1",
            "publisherId": "juloc-official",
            "keys": [
              {
                "keyId": "official-2026-01",
                "algorithm": "ecdsa-p256-sha256-p1363",
                "publicKeySpkiBase64": "c3BraS1ieXRlcw==",
                "publicKeyFingerprint": "sha256:{{new string('a', 64)}}",
                "validFromUtc": "2026-01-01T00:00:00Z",
                "validUntilUtc": null,
                "revokedAtUtc": null
              }
            ]
          }
          """;

    private static string Sha256(string text) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    private string CreateDirectory(string prefix)
    {
        var directory = Path.Combine(Path.GetTempPath(), prefix, Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(directory);
        this.directories.Add(directory);
        return directory;
    }

    /// <summary>A source with no credential never reaches the lease service.</summary>
    private sealed class RefusingLeaseService : ISecretLeaseService
    {
        public Task<SecretLease> AcquireAsync(
            Guid secretReferenceId,
            Guid operationId,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("These tests configure no source credential.");
    }

    /// <summary>These tests drive the refresh directly, without the request path.</summary>
    private sealed class UnusedOperationService : IOperationService
    {
        public Task<OperationSnapshot> CreateAsync(
            CreateOperationCommand command,
            CancellationToken cancellationToken = default) => throw Unused();

        public Task<OperationPage> ListAsync(
            OperationQuery query,
            CancellationToken cancellationToken = default) => throw Unused();

        public Task<OperationSnapshot> ReadAsync(
            Guid operationId,
            Guid ownerUserId,
            CancellationToken cancellationToken = default) => throw Unused();

        public Task<IReadOnlyList<OperationProgressSnapshot>> ReadProgressAsync(
            Guid operationId,
            Guid ownerUserId,
            CancellationToken cancellationToken = default) => throw Unused();

        public Task<OperationSnapshot> RequestCancellationAsync(
            Guid operationId,
            Guid ownerUserId,
            CancellationToken cancellationToken = default) => throw Unused();

        public Task<OperationSnapshot> MarkRunningAsync(
            Guid operationId,
            CancellationToken cancellationToken = default) => throw Unused();

        public Task<OperationSnapshot> ReportProgressAsync(
            Guid operationId,
            int? progressPercent,
            string currentStep,
            CancellationToken cancellationToken = default) => throw Unused();

        public Task<OperationSnapshot> MarkSucceededAsync(
            Guid operationId,
            CancellationToken cancellationToken = default) => throw Unused();

        public Task<OperationSnapshot> MarkFailedAsync(
            Guid operationId,
            string failureCode,
            string safeFailureDetail,
            CancellationToken cancellationToken = default) => throw Unused();

        public Task<OperationSnapshot> MarkCancelledAsync(
            Guid operationId,
            CancellationToken cancellationToken = default) => throw Unused();

        private static InvalidOperationException Unused() =>
            new("These tests drive the refresh directly.");
    }

    private sealed class UnusedDispatcher : ICatalogRefreshDispatcher
    {
        public ValueTask EnqueueAsync(
            CatalogRefreshRequest request,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("These tests drive the refresh directly.");

        public IAsyncEnumerable<CatalogRefreshRequest> ReadAllAsync(CancellationToken cancellationToken) =>
            throw new InvalidOperationException("These tests drive the refresh directly.");
    }

    private sealed class DiscardingAuditService : IAuditService
    {
        public void Stage(AuditRecord record)
        {
        }

        public Task AppendAsync(AuditRecord record, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

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
