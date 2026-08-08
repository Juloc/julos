using JulOS.Application.Remote;
using JulOS.Application.Secrets;
using JulOS.Contracts.Remote;
using JulOS.Infrastructure.Persistence.Core;

using Microsoft.EntityFrameworkCore;

namespace JulOS.Infrastructure.Packages;

/// <summary>Reconciles package-owned interactive runtimes and presentation secrets after terminal sessions.</summary>
internal sealed class InteractiveSessionCleanupService : IInteractiveSessionCleanupService
{
    private readonly CoreDbContext context;
    private readonly IRemoteRuntimeManager runtimeManager;
    private readonly ISecretReferenceService secrets;

    internal InteractiveSessionCleanupService(
        CoreDbContext context,
        IRemoteRuntimeManager runtimeManager,
        ISecretReferenceService secrets)
    {
        this.context = context ?? throw new ArgumentNullException(nameof(context));
        this.runtimeManager = runtimeManager ?? throw new ArgumentNullException(nameof(runtimeManager));
        this.secrets = secrets ?? throw new ArgumentNullException(nameof(secrets));
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
            }
            catch (Exception exception) when (
                exception is RemoteRuntimeManagerException or SecretReferenceFailureException)
            {
                failures++;
            }
        }

        return new InteractiveSessionCleanupResult(items.Count, cleaned, failures);
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
