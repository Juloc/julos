using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using JulOS.Application.Auditing;
using JulOS.Application.Catalog;
using JulOS.Application.Secrets;
using JulOS.Contracts.Catalog;
using JulOS.Domain;
using JulOS.Domain.Catalog;
using JulOS.Application.Operations;
using JulOS.Domain.Observability;
using JulOS.Domain.Primitives;
using JulOS.Infrastructure.Persistence.Core;

using Microsoft.EntityFrameworkCore;

namespace JulOS.Infrastructure.Catalog;

/// <summary>Reads one catalog source and replaces its cached entries atomically.</summary>
/// <remarks>
/// <para>
/// Everything is read, digest-checked and evaluated into memory before a single row is
/// touched. That ordering is the whole mechanism behind "a failed refresh never replaces a
/// valid cache": there is no point at which a partially read source has been written.
/// </para>
/// <para>
/// Nothing here decides trust. It observes the keys a source published, asks the verifier
/// whether a signature holds, and records the answer; whether a key is trusted was decided
/// separately, by an administrator, and is only read.
/// </para>
/// </remarks>
internal sealed class EfCatalogRefreshService : ICatalogRefreshService
{
    /// <summary>The index every source root publishes.</summary>
    private const string IndexPath = "catalog.json";

    /// <summary>The optional detached signature beside a definition.</summary>
    private const string SignatureFileName = "signature.json";

    /// <summary>The stable operation type an administrator sees in the Operation Center.</summary>
    internal const string OperationType = "catalog.source.refresh";

    private readonly CoreDbContext context;
    private readonly Dictionary<CatalogSourceKind, ICatalogSourceReader> readers;
    private readonly ISecretLeaseService leases;
    private readonly IOperationService operations;
    private readonly ICatalogRefreshDispatcher queue;
    private readonly IIdentifierGenerator identifiers;
    private readonly IAuditService audit;
    private readonly TimeProvider timeProvider;

    public EfCatalogRefreshService(
        CoreDbContext context,
        IEnumerable<ICatalogSourceReader> readers,
        ISecretLeaseService leases,
        IOperationService operations,
        ICatalogRefreshDispatcher queue,
        IIdentifierGenerator identifiers,
        IAuditService audit,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(readers);

        this.context = context ?? throw new ArgumentNullException(nameof(context));
        this.readers = readers.ToDictionary(reader => reader.Kind);
        this.leases = leases ?? throw new ArgumentNullException(nameof(leases));
        this.operations = operations ?? throw new ArgumentNullException(nameof(operations));
        this.queue = queue ?? throw new ArgumentNullException(nameof(queue));
        this.identifiers = identifiers ?? throw new ArgumentNullException(nameof(identifiers));
        this.audit = audit ?? throw new ArgumentNullException(nameof(audit));
        this.timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public async Task<OperationSnapshot> RequestAsync(
        Guid catalogSourceId,
        Guid ownerUserId,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        var sourceRow = await this.RequireRefreshableAsync(catalogSourceId, cancellationToken)
            .ConfigureAwait(false);

        // The idempotency key names the source rather than the moment, so a retried request
        // joins the refresh that is already queued instead of racing a second one against
        // the same cache.
        var operation = await this.operations.CreateAsync(
            new CreateOperationCommand(
                ownerUserId,
                OperationType,
                SourcePackageId: null,
                sourceRow.Id.ToString("D"),
                $"catalog-refresh-{sourceRow.Id:D}",
                correlationId),
            cancellationToken).ConfigureAwait(false);

        if (operation.State == OperationState.Queued)
        {
            await this.queue
                .EnqueueAsync(new CatalogRefreshRequest(sourceRow.Id, operation.OperationId), cancellationToken)
                .ConfigureAwait(false);
        }

        return operation;
    }

    public async Task<CatalogRefreshResult> RefreshAsync(
        Guid catalogSourceId,
        Guid operationId,
        CancellationToken cancellationToken = default)
    {
        var sourceRow = await this.RequireRefreshableAsync(catalogSourceId, cancellationToken)
            .ConfigureAwait(false);
        var reader = this.readers[sourceRow.Kind];

        try
        {
            return await this.RunAsync(sourceRow, reader, operationId, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is CatalogRefreshException or DomainRuleViolationException)
        {
            var code = exception is CatalogRefreshException refresh
                ? refresh.Code
                : ((DomainRuleViolationException)exception).Code;
            return await this.RecordFailureAsync(sourceRow, code, operationId, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    /// <summary>Loads a source that may actually be refreshed, or says why it may not.</summary>
    private async Task<CatalogSourceRow> RequireRefreshableAsync(
        Guid catalogSourceId,
        CancellationToken cancellationToken)
    {
        var sourceRow = await this.context.CatalogSources
            .SingleOrDefaultAsync(row => row.Id == catalogSourceId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new CatalogFailureException(
                CatalogErrorCodes.SourceNotFound,
                CatalogFailureReason.NotFound,
                "No catalog source with that identity is configured.");

        if (sourceRow.DeletedAtUtc is not null || !sourceRow.Enabled)
        {
            throw new CatalogFailureException(
                sourceRow.DeletedAtUtc is not null
                    ? CatalogErrorCodes.SourceRemoved
                    : CatalogErrorCodes.SourceInvalid,
                sourceRow.DeletedAtUtc is not null
                    ? CatalogFailureReason.Removed
                    : CatalogFailureReason.Invalid,
                "A removed or disabled catalog source takes no part in refreshes.");
        }

        if (!this.readers.ContainsKey(sourceRow.Kind))
        {
            // A kind this build cannot read is a configuration error rather than a source
            // failure, so it is refused instead of being recorded as an unreachable source.
            throw new CatalogFailureException(
                CatalogErrorCodes.SourceInvalid,
                CatalogFailureReason.Invalid,
                $"This release cannot read a '{sourceRow.Kind}' catalog source.");
        }

        return sourceRow;
    }

    private async Task<CatalogRefreshResult> RunAsync(
        CatalogSourceRow sourceRow,
        ICatalogSourceReader reader,
        Guid operationId,
        CancellationToken cancellationToken)
    {
        using var credential = await this.AcquireAsync(sourceRow, operationId, cancellationToken)
            .ConfigureAwait(false);
        await using var snapshot = await reader
            .OpenAsync(
                new CatalogSourceLocation(
                    sourceRow.Kind,
                    sourceRow.Location,
                    sourceRow.AuthenticationSecretReferenceId),
                credential,
                cancellationToken)
            .ConfigureAwait(false);

        var indexBytes = await snapshot.ReadAsync(IndexPath, cancellationToken).ConfigureAwait(false);
        var index = CatalogIndexDocument.Parse(indexBytes);
        RequireStableIdentity(sourceRow, index);

        var digest = snapshot.NativeContentIdentity ?? $"sha256:{Sha256(indexBytes)}";
        var unchanged = string.Equals(digest, sourceRow.LastSuccessfulDigest, StringComparison.Ordinal);

        if (unchanged && snapshot.NativeContentIdentity is not null)
        {
            // A commit or an artifact digest covers the whole source, so an unchanged one
            // means every file is unchanged and the cache is already the answer.
            return await this.RecordUnchangedAsync(sourceRow, digest, cancellationToken).ConfigureAwait(false);
        }

        var keys = await this.ObserveKeysAsync(sourceRow, index, snapshot, cancellationToken).ConfigureAwait(false);
        var revision = (sourceRow.LastSuccessfulRevision ?? 0) + 1;
        var now = this.timeProvider.GetUtcNow();
        var entries = new List<CatalogEntryCacheRow>(index.Entries.Count);

        foreach (var entry in index.Entries)
        {
            entries.Add(await this
                .ReadEntryAsync(sourceRow, snapshot, entry, keys, revision, digest, now, cancellationToken)
                .ConfigureAwait(false));
        }

        if (unchanged)
        {
            // An index digest binds the entries but is not itself a whole-source identity,
            // so every file was read and checked before this point. Now that they are known
            // to be identical, rewriting the rows would only churn their revisions.
            return await this.RecordUnchangedAsync(sourceRow, digest, cancellationToken).ConfigureAwait(false);
        }

        return await this
            .CommitAsync(sourceRow, entries, index.SourceId, revision, digest, now, operationId, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Refuses a source that changed the identity it claims.
    /// </summary>
    /// <remarks>
    /// The identity is what installed applications resolve through. A source that starts
    /// claiming a different one is either misconfigured or replaced, and in both cases
    /// silently adopting the new name would rewrite the meaning of everything already
    /// installed from it.
    /// </remarks>
    private static void RequireStableIdentity(CatalogSourceRow sourceRow, CatalogIndexDocument index)
    {
        if (sourceRow.SourceIdentity is string recorded
            && !string.Equals(recorded, index.SourceId, StringComparison.Ordinal))
        {
            throw new CatalogRefreshException(
                "catalog.definition_invalid",
                $"The catalog source now claims identity '{index.SourceId}' where it claimed '{recorded}'.");
        }
    }

    private async Task<SecretLease?> AcquireAsync(
        CatalogSourceRow sourceRow,
        Guid operationId,
        CancellationToken cancellationToken)
    {
        if (sourceRow.AuthenticationSecretReferenceId is not Guid secretReferenceId)
        {
            return null;
        }

        try
        {
            return await this.leases
                .AcquireAsync(secretReferenceId, operationId, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (SecretReferenceFailureException exception)
        {
            // The credential itself never reaches this message; only that one was needed and
            // could not be leased for this operation.
            throw new CatalogRefreshException(
                CatalogRefreshException.SourceUnavailable,
                "The catalog source credential could not be leased for this refresh.",
                exception);
        }
    }

    /// <summary>Records every key the source publishes, so a decision can be made about it.</summary>
    /// <remarks>
    /// Observation is not trust. A published key becomes a row an administrator may later
    /// decide about; until then it is `unknown`, and a definition signed with it is
    /// `unknown-signed`.
    /// </remarks>
    private async Task<List<CatalogVerificationKey>> ObserveKeysAsync(
        CatalogSourceRow sourceRow,
        CatalogIndexDocument index,
        CatalogSourceSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        var stored = await this.context.CatalogPublisherKeys
            .Where(row => row.CatalogSourceId == sourceRow.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (index.KeySet is CatalogFileReference reference)
        {
            var bytes = await ReadVerifiedAsync(snapshot, reference, cancellationToken).ConfigureAwait(false);
            var keySet = CatalogKeySetDocument.Parse(bytes);
            var revision = (sourceRow.LastSuccessfulRevision ?? 0) + 1;

            foreach (var published in keySet.Keys)
            {
                this.ObserveKey(sourceRow, stored, keySet.PublisherId, published, revision);
            }
        }

        return stored
            .Select(row => new CatalogVerificationKey(
                row.PublisherId,
                row.KeyId,
                row.Algorithm,
                row.PublicKeySpki,
                // The pinned official fingerprint set is release configuration that
                // `REL-CAT-001` delivers. Until it exists, no key is pinned, which is the
                // safe direction: a key is at most `unknown-signed` rather than trusted.
                OfficialPinned: false,
                row.AdministratorTrustState,
                row.ValidFromUtc,
                row.ValidUntilUtc,
                row.RevokedAtUtc))
            .ToList();
    }

    private void ObserveKey(
        CatalogSourceRow sourceRow,
        List<CatalogPublisherKeyRow> stored,
        string publisherId,
        CatalogKeySetKey published,
        int sourceRevision)
    {
        var existing = stored.SingleOrDefault(row =>
            string.Equals(row.PublisherId, publisherId, StringComparison.Ordinal)
            && string.Equals(row.KeyId, published.KeyId, StringComparison.Ordinal));

        if (existing is null)
        {
            var observed = CatalogPublisherKey.Observe(
                new CatalogPublisherKeyId(this.identifiers.Create()),
                sourceRow.Id,
                publisherId,
                published.KeyId,
                published.Algorithm,
                published.PublicKeySpkiBase64,
                published.PublicKeyFingerprint,
                published.ValidFromUtc,
                published.ValidUntilUtc,
                sourceRevision);

            if (published.RevokedAtUtc is DateTimeOffset revoked)
            {
                observed.Revoke(revoked);
            }

            var row = CatalogPublisherKeyRow.FromDomain(observed);
            _ = this.context.CatalogPublisherKeys.Add(row);
            stored.Add(row);
            return;
        }

        var key = existing.ToDomain();

        // A reused key identity carrying different bytes fails the whole refresh: accepting
        // it would replace the key that vouches for everything the source ever published.
        key.ObserveAgain(published.PublicKeySpkiBase64, published.PublicKeyFingerprint, sourceRevision);

        if (published.RevokedAtUtc is DateTimeOffset revokedAt)
        {
            key.Revoke(revokedAt);
        }

        existing.LastObservedSourceRevision = key.LastObservedSourceRevision;
        existing.RevokedAtUtc = key.RevokedAtUtc;
        existing.Revision = key.Revision.Value;
    }

    private async Task<CatalogEntryCacheRow> ReadEntryAsync(
        CatalogSourceRow sourceRow,
        CatalogSourceSnapshot snapshot,
        CatalogIndexEntry entry,
        IReadOnlyCollection<CatalogVerificationKey> keys,
        int revision,
        string sourceDigest,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var bytes = await ReadVerifiedAsync(snapshot, entry.File, cancellationToken).ConfigureAwait(false);
        using var document = JsonDocument.Parse(StripByteOrderMark(bytes).ToArray());
        var definitionDigest = CatalogCanonicalJson.DefinitionDigest(document.RootElement);
        var definition = Encoding.UTF8.GetString(CatalogCanonicalJson.Canonicalize(document.RootElement));

        var envelope = await ReadEnvelopeAsync(snapshot, entry.File.Path, cancellationToken)
            .ConfigureAwait(false);
        var evaluation = CatalogSignatureVerifier.Evaluate(
            envelope,
            definitionDigest,
            keys,
            officialSource: sourceRow.Kind == CatalogSourceKind.Official);
        var assessment = Assess(envelope, evaluation, keys);

        return new CatalogEntryCacheRow
        {
            Id = this.identifiers.Create(),
            CatalogSourceId = sourceRow.Id,
            AppId = entry.AppId,
            Version = entry.Version,
            SourceRevision = revision,
            SourceDigest = sourceDigest,
            DefinitionDigest = definitionDigest,
            Definition = definition,
            PublisherId = envelope?.PublisherId,
            SignatureKeyId = envelope?.KeyId,
            PublicKeyFingerprint = envelope?.PublicKeyFingerprint,
            SignatureState = evaluation.State,
            TrustAssessmentDigest = assessment.Digest(),
            CachedAtUtc = now,
            Revision = Revision.Initial.Value,
        };
    }

    private static async Task<CatalogSignatureEnvelope?> ReadEnvelopeAsync(
        CatalogSourceSnapshot snapshot,
        string definitionPath,
        CancellationToken cancellationToken)
    {
        var separator = definitionPath.LastIndexOf('/');
        var path = separator < 0
            ? SignatureFileName
            : $"{definitionPath[..separator]}/{SignatureFileName}";

        var bytes = await snapshot.TryReadAsync(path, cancellationToken).ConfigureAwait(false);
        return bytes is null ? null : CatalogSignatureDocument.Parse(bytes);
    }

    /// <summary>Reads a referenced file and refuses it unless it matches its declared digest.</summary>
    /// <remarks>
    /// The check happens before the bytes are parsed, so a file that is not what the index
    /// says never reaches a parser at all.
    /// </remarks>
    private static async Task<byte[]> ReadVerifiedAsync(
        CatalogSourceSnapshot snapshot,
        CatalogFileReference reference,
        CancellationToken cancellationToken)
    {
        var bytes = await snapshot.ReadAsync(reference.Path, cancellationToken).ConfigureAwait(false);
        var actual = Sha256(bytes);

        return string.Equals(actual, reference.Sha256, StringComparison.Ordinal)
            ? bytes
            : throw new CatalogRefreshException(
                CatalogRefreshException.IntegrityMismatch,
                $"'{reference.Path}' does not match the digest the catalog index declares for it.");
    }

    private static CatalogTrustAssessment Assess(
        CatalogSignatureEnvelope? envelope,
        CatalogTrustEvaluation evaluation,
        IReadOnlyCollection<CatalogVerificationKey> keys)
    {
        if (envelope is null)
        {
            return CatalogTrustAssessment.ForUnsignedDefinition(evaluation);
        }

        var key = keys.SingleOrDefault(candidate =>
            string.Equals(candidate.PublisherId, envelope.PublisherId, StringComparison.Ordinal)
            && string.Equals(candidate.KeyId, envelope.KeyId, StringComparison.Ordinal));

        return new CatalogTrustAssessment(
            evaluation.State,
            evaluation.Policy,
            evaluation.Expired,
            envelope.PublisherId,
            envelope.KeyId,
            envelope.PublicKeyFingerprint,
            key?.OfficialPinned ?? false,
            key?.AdministratorTrust ?? AdministratorTrustState.Unknown);
    }

    private async Task<CatalogRefreshResult> CommitAsync(
        CatalogSourceRow sourceRow,
        List<CatalogEntryCacheRow> entries,
        string sourceIdentity,
        int revision,
        string digest,
        DateTimeOffset now,
        Guid operationId,
        CancellationToken cancellationToken)
    {
        var previous = await this.context.CatalogEntryCache
            .Where(row => row.CatalogSourceId == sourceRow.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        // One SaveChanges is one transaction, so the previous catalog is replaced completely
        // or not at all. Nothing above this line has written a row.
        this.context.CatalogEntryCache.RemoveRange(previous);
        this.context.CatalogEntryCache.AddRange(entries);

        var source = sourceRow.ToDomain();
        source.RecordRefreshSucceeded(revision, digest, now);
        sourceRow.SourceIdentity = sourceIdentity;
        sourceRow.LastSuccessfulRevision = source.LastSuccessfulRevision;
        sourceRow.LastSuccessfulDigest = source.LastSuccessfulDigest;
        sourceRow.LastRefreshAtUtc = source.LastRefreshAtUtc;
        sourceRow.LastRefreshState = source.LastRefreshState;
        sourceRow.LastFailureCode = source.LastFailureCode;
        sourceRow.Revision = source.Revision.Value;

        this.StageAudit(
            sourceRow,
            operationId,
            AuditOutcome.Succeeded,
            $"Refreshed catalog source '{sourceRow.DisplayName}'.",
            $"revision={revision}; digest={digest}; entries={entries.Count}");

        _ = await this.context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return new CatalogRefreshResult(sourceRow.Id, Succeeded: true, revision, digest, entries.Count, null);
    }

    private async Task<CatalogRefreshResult> RecordUnchangedAsync(
        CatalogSourceRow sourceRow,
        string digest,
        CancellationToken cancellationToken)
    {
        var count = await this.context.CatalogEntryCache
            .CountAsync(row => row.CatalogSourceId == sourceRow.Id, cancellationToken)
            .ConfigureAwait(false);

        var source = sourceRow.ToDomain();
        source.RecordRefreshSucceeded(
            sourceRow.LastSuccessfulRevision ?? 1,
            digest,
            this.timeProvider.GetUtcNow());
        sourceRow.LastRefreshAtUtc = source.LastRefreshAtUtc;
        sourceRow.LastRefreshState = source.LastRefreshState;
        sourceRow.LastFailureCode = source.LastFailureCode;
        sourceRow.Revision = source.Revision.Value;

        _ = await this.context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return new CatalogRefreshResult(
            sourceRow.Id,
            Succeeded: true,
            sourceRow.LastSuccessfulRevision,
            digest,
            count,
            null);
    }

    /// <summary>
    /// Records a failed refresh without touching the cached catalog.
    /// </summary>
    /// <remarks>
    /// The previous catalog keeps being served and is marked stale, because a user looking at
    /// a catalog is entitled to know it is not current — and because a partially parsed
    /// source is not a better answer than a complete older one.
    /// </remarks>
    private async Task<CatalogRefreshResult> RecordFailureAsync(
        CatalogSourceRow sourceRow,
        string code,
        Guid operationId,
        CancellationToken cancellationToken)
    {
        this.context.ChangeTracker.Clear();
        var reloaded = await this.context.CatalogSources
            .SingleAsync(row => row.Id == sourceRow.Id, cancellationToken)
            .ConfigureAwait(false);

        var source = reloaded.ToDomain();
        source.RecordRefreshFailed(code, this.timeProvider.GetUtcNow());
        reloaded.LastRefreshAtUtc = source.LastRefreshAtUtc;
        reloaded.LastRefreshState = source.LastRefreshState;
        reloaded.LastFailureCode = source.LastFailureCode;
        reloaded.Revision = source.Revision.Value;

        this.StageAudit(
            reloaded,
            operationId,
            AuditOutcome.Failed,
            $"A refresh of catalog source '{reloaded.DisplayName}' failed.",
            $"code={code}; state={source.LastRefreshState}");

        _ = await this.context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return new CatalogRefreshResult(
            reloaded.Id,
            Succeeded: false,
            reloaded.LastSuccessfulRevision,
            reloaded.LastSuccessfulDigest,
            EntryCount: 0,
            code);
    }

    private void StageAudit(
        CatalogSourceRow sourceRow,
        Guid operationId,
        AuditOutcome outcome,
        string summary,
        string safeDetails) =>
        this.audit.Stage(new AuditRecord(
            // A refresh is the platform acting on a schedule or on request; the requesting
            // administrator is recorded on the Operation that owns it.
            UserId: null,
            AgentId: null,
            SourcePackageId: null,
            "catalog.source.refresh",
            "catalog-source",
            sourceRow.Id.ToString(),
            outcome,
            operationId.ToString(),
            RemoteAddress: null,
            summary,
            safeDetails));

    private static ReadOnlySpan<byte> StripByteOrderMark(byte[] bytes) =>
        bytes.AsSpan().StartsWith<byte>([0xEF, 0xBB, 0xBF]) ? bytes.AsSpan(3) : bytes;

    private static string Sha256(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
}
