using JulOS.Application.Remote;
using JulOS.Application.Secrets;
using JulOS.Contracts.Remote;
using JulOS.Infrastructure.Persistence.Core;

using Microsoft.EntityFrameworkCore;

namespace JulOS.Infrastructure.Browser;

/// <summary>Reconciles Chromium runtimes and Browser-owned VNC secrets after terminal sessions.</summary>
public sealed class BrowserSessionCleanupService
{
    private const string BrowserPackageId = "de.juloc.julos.browser";
    private const string VncSecretPurposePrefix = "remote.browser.";
    private readonly CoreDbContext context;
    private readonly IRemoteRuntimeManager runtimeManager;
    private readonly ISecretReferenceService secrets;

    /// <summary>Creates Browser terminal-resource cleanup.</summary>
    public BrowserSessionCleanupService(
        CoreDbContext context,
        IRemoteRuntimeManager runtimeManager,
        ISecretReferenceService secrets)
    {
        this.context = context ?? throw new ArgumentNullException(nameof(context));
        this.runtimeManager = runtimeManager ?? throw new ArgumentNullException(nameof(runtimeManager));
        this.secrets = secrets ?? throw new ArgumentNullException(nameof(secrets));
    }

    /// <summary>Reconciles a bounded number of terminal Browser sessions.</summary>
    public async Task<BrowserSessionCleanupResult> ReconcileAsync(
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
            where session.CallerPackageId == BrowserPackageId
                && session.TargetPort == 5900
                && session.TargetHost.StartsWith("julos-browser-")
                && (session.State == RemoteSessionStates.Disconnected
                    || session.State == RemoteSessionStates.Cancelled
                    || session.State == RemoteSessionStates.Expired
                    || session.State == RemoteSessionStates.Failed)
                && secret.DeletedAtUtc == null
            orderby session.UpdatedAtUtc, session.Id
            select new CleanupItem(
                session.Id,
                session.OwnerUserId,
                session.TargetHost,
                secret.Id,
                secret.OwningScopeType,
                secret.OwningScopeId,
                secret.Purpose,
                secret.Revision))
            .Take(limit)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var cleaned = 0;
        var failures = 0;
        foreach (var item in items)
        {
            if (item.ScopeType != SecretOwningScopeType.Package
                || !string.Equals(item.ScopeId, BrowserPackageId, StringComparison.Ordinal)
                || !item.Purpose.StartsWith(VncSecretPurposePrefix, StringComparison.Ordinal))
            {
                failures++;
                continue;
            }

            try
            {
                await this.runtimeManager.RemoveAsync(
                    BrowserSessionCapabilityProvider.RuntimeIdFromHost(item.TargetHost),
                    cancellationToken).ConfigureAwait(false);
                _ = await this.secrets.DeleteAsync(
                    new DeleteSecretReferenceCommand(
                        item.SecretReferenceId,
                        item.OwnerUserId,
                        item.SecretRevision,
                        $"browser-cleanup-{item.SessionId:N}",
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

        return new BrowserSessionCleanupResult(items.Count, cleaned, failures);
    }

    private sealed record CleanupItem(
        Guid SessionId,
        Guid OwnerUserId,
        string TargetHost,
        Guid SecretReferenceId,
        SecretOwningScopeType ScopeType,
        string? ScopeId,
        string Purpose,
        int SecretRevision);
}

/// <summary>Summary of one bounded Browser cleanup reconciliation pass.</summary>
/// <param name="Examined">Terminal Browser sessions examined.</param>
/// <param name="Cleaned">Chromium runtime and secret pairs removed.</param>
/// <param name="Failures">Pairs that require a later retry.</param>
public sealed record BrowserSessionCleanupResult(int Examined, int Cleaned, int Failures);
