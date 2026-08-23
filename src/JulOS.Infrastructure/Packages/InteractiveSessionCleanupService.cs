using JulOS.Application.Remote;
using JulOS.Application.Secrets;
using JulOS.Contracts.Remote;
using JulOS.Domain.Observability;
using JulOS.Domain.Packages;
using JulOS.Infrastructure.Persistence.Core;

using Microsoft.EntityFrameworkCore;

namespace JulOS.Infrastructure.Packages;

/// <summary>Reconciles package-owned interactive runtimes and presentation secrets after terminal sessions.</summary>
internal sealed class InteractiveSessionCleanupService : IInteractiveSessionCleanupService
{
    private const string BrowserPackageId = "de.juloc.julos.browser";
    private const string CleanupProblemType = "session-cleanup-failed";
    private const string CleanupProblemTitleKey = "problem.browser.session_cleanup_failed";

    private readonly CoreDbContext context;
    private readonly IRemoteRuntimeManager runtimeManager;
    private readonly ISecretReferenceService secrets;
    private readonly TimeProvider timeProvider;

    internal InteractiveSessionCleanupService(
        CoreDbContext context,
        IRemoteRuntimeManager runtimeManager,
        ISecretReferenceService secrets,
        TimeProvider timeProvider)
    {
        this.context = context ?? throw new ArgumentNullException(nameof(context));
        this.runtimeManager = runtimeManager ?? throw new ArgumentNullException(nameof(runtimeManager));
        this.secrets = secrets ?? throw new ArgumentNullException(nameof(secrets));
        this.timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public async Task<InteractiveSessionCleanupResult> ReconcileAsync(
        int limit,
        CancellationToken cancellationToken = default)
    {
        if (limit is < 1 or > 200)
        {
            throw new ArgumentOutOfRangeException(nameof(limit));
        }

        var items = await (
            from session in this.context.RemoteSessions.AsNoTracking()
            join secret in this.context.SecretReferences.AsNoTracking()
                on session.SecretReferenceId equals secret.Id
            where session.TargetHost.StartsWith("julos-interactive-")
                && (session.State == RemoteSessionStates.Disconnected
                    || session.State == RemoteSessionStates.Cancelled
                    || session.State == RemoteSessionStates.Expired
                    || session.State == RemoteSessionStates.Failed)
                && secret.Purpose == InteractiveSessionCapabilityProvider.SecretPurpose
                && secret.DeletedAtUtc == null
            orderby session.UpdatedAtUtc, session.Id
            select new CleanupItem(
                session.Id,
                session.OwnerUserId,
                session.CallerPackageId,
                session.TargetHost,
                secret.Id,
                secret.OwningScopeType,
                secret.OwningScopeId,
                secret.Revision))
            .Take(limit)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var cleaned = 0;
        var failures = 0;
        foreach (var item in items)
        {
            if (item.ScopeType != SecretOwningScopeType.Package
                || !string.Equals(item.ScopeId, item.CallerPackageId, StringComparison.Ordinal))
            {
                failures++;
                await this.ObserveCleanupProblemAsync(item.SessionId, cancellationToken).ConfigureAwait(false);
                continue;
            }

            try
            {
                await this.runtimeManager.RemoveAsync(
                    InteractiveSessionCapabilityProvider.RuntimeIdFromHost(item.TargetHost),
                    cancellationToken).ConfigureAwait(false);
                _ = await this.secrets.DeleteAsync(
                    new DeleteSecretReferenceCommand(
                        item.SecretReferenceId,
                        item.OwnerUserId,
                        item.SecretRevision,
                        $"interactive-cleanup-{item.SessionId:N}",
                        RemoteAddress: null),
                    cancellationToken).ConfigureAwait(false);
                cleaned++;
                await this.ResolveCleanupProblemAsync(item.SessionId, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (
                exception is RemoteRuntimeManagerException or SecretReferenceFailureException)
            {
                failures++;
                await this.ObserveCleanupProblemAsync(item.SessionId, cancellationToken).ConfigureAwait(false);
            }
        }

        if (this.context.ChangeTracker.HasChanges())
        {
            await this.context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        return new InteractiveSessionCleanupResult(items.Count, cleaned, failures);
    }

    private async Task ObserveCleanupProblemAsync(Guid sessionId, CancellationToken cancellationToken)
    {
        var resourceIdentity = sessionId.ToString("D");
        var existing = await this.context.Problems.SingleOrDefaultAsync(
            row => row.SourcePackageId == BrowserPackageId
                && row.ProblemType == CleanupProblemType
                && row.StableResourceIdentity == resourceIdentity,
            cancellationToken).ConfigureAwait(false);
        var now = this.timeProvider.GetUtcNow();
        if (existing is null)
        {
            var problem = Problem.Detect(
                new ProblemId(Guid.NewGuid()),
                new ProblemIdentity(
                    PackageId.Parse(BrowserPackageId),
                    CleanupProblemType,
                    resourceIdentity),
                ProblemSeverity.Error,
                CleanupProblemTitleKey,
                this.timeProvider);
            this.context.Problems.Add(ProblemRow.FromDomain(problem));
            return;
        }

        if (existing.State == ProblemState.Resolved)
        {
            existing.State = ProblemState.Active;
            existing.AcknowledgedAtUtc = null;
            existing.AcknowledgedByUserId = null;
            existing.ResolvedAtUtc = null;
        }
        existing.Severity = ProblemSeverity.Error;
        existing.LastObservedAtUtc = now;
        existing.ObservationCount = checked(existing.ObservationCount + 1);
        existing.Revision = checked(existing.Revision + 1);
    }

    private async Task ResolveCleanupProblemAsync(Guid sessionId, CancellationToken cancellationToken)
    {
        var resourceIdentity = sessionId.ToString("D");
        var existing = await this.context.Problems.SingleOrDefaultAsync(
            row => row.SourcePackageId == BrowserPackageId
                && row.ProblemType == CleanupProblemType
                && row.StableResourceIdentity == resourceIdentity,
            cancellationToken).ConfigureAwait(false);
        if (existing is null || existing.State == ProblemState.Resolved)
        {
            return;
        }

        existing.State = ProblemState.Resolved;
        existing.ResolvedAtUtc = this.timeProvider.GetUtcNow();
        existing.Revision = checked(existing.Revision + 1);
    }

    private sealed record CleanupItem(
        Guid SessionId,
        Guid OwnerUserId,
        string CallerPackageId,
        string TargetHost,
        Guid SecretReferenceId,
        SecretOwningScopeType ScopeType,
        string? ScopeId,
        int SecretRevision);
}
