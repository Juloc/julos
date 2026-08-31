using JulOS.Application.Remote;
using JulOS.Contracts.Remote;
using JulOS.Infrastructure.Persistence.Core;

using Microsoft.EntityFrameworkCore;

namespace JulOS.Infrastructure.Remote;

/// <summary>Resumes durable Remote runtime provisioning from persisted session state.</summary>
public sealed class PostgresRemoteSessionProvisioningReconciler : IRemoteSessionProvisioningReconciler
{
    private readonly CoreDbContext context;
    private readonly IRemoteSessionProvisioner provisioner;

    /// <summary>Creates the durable Remote provisioning reconciler.</summary>
    public PostgresRemoteSessionProvisioningReconciler(
        CoreDbContext context,
        IRemoteSessionProvisioner provisioner)
    {
        this.context = context ?? throw new ArgumentNullException(nameof(context));
        this.provisioner = provisioner ?? throw new ArgumentNullException(nameof(provisioner));
    }

    /// <inheritdoc />
    public async Task<RemoteProvisioningReconciliationResult> ReconcilePendingAsync(
        int limit,
        CancellationToken cancellationToken = default)
    {
        if (limit is < 1 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(limit), limit, "Remote provisioning batch size must be between 1 and 100.");
        }

        var pending = await this.context.RemoteSessions
            .AsNoTracking()
            .Where(row => row.State == RemoteSessionStates.Requested
                || row.State == RemoteSessionStates.Provisioning)
            .OrderBy(row => row.CreatedAtUtc)
            .ThenBy(row => row.Id)
            .Take(limit)
            .Select(row => new PendingRemoteSession(
                row.Id,
                row.OwnerUserId,
                row.CallerPackageId,
                row.Revision))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var progressed = 0;
        var skipped = 0;
        foreach (var session in pending)
        {
            try
            {
                _ = await this.provisioner.ProvisionAsync(
                    new ProvisionRemoteSessionCommand(
                        session.OwnerUserId,
                        session.CallerPackageId,
                        session.SessionId,
                        session.Revision),
                    cancellationToken).ConfigureAwait(false);
                progressed++;
            }
            catch (RemoteSessionServiceException exception) when (
                exception.Reason is RemoteSessionServiceFailureReason.NotFound
                    or RemoteSessionServiceFailureReason.ConcurrencyConflict
                    or RemoteSessionServiceFailureReason.InvalidTransition)
            {
                // Another request or reconciler already advanced or ended this session.
                skipped++;
            }
        }

        return new RemoteProvisioningReconciliationResult(pending.Count, progressed, skipped);
    }

    private sealed record PendingRemoteSession(
        Guid SessionId,
        Guid OwnerUserId,
        string CallerPackageId,
        int Revision);
}
