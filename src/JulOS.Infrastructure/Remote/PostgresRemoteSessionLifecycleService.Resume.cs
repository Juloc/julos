using JulOS.Application.Remote;
using JulOS.Contracts.Remote;

using Microsoft.EntityFrameworkCore;

namespace JulOS.Infrastructure.Remote;

/// <summary>Adds explicit active-session resume authorization.</summary>
public sealed partial class PostgresRemoteSessionLifecycleService
{
    /// <inheritdoc />
    public async Task<RemoteSessionResponse> ResumeAsync(
        ResumeRemoteSessionCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var callerPackageId = ValidateCaller(command.OwnerUserId, command.CallerPackageId);
        var request = ValidateResume(command.Request);
        var row = await this.context.RemoteSessions
            .SingleOrDefaultAsync(
                candidate => candidate.Id == request.SessionId
                    && candidate.OwnerUserId == command.OwnerUserId
                    && candidate.CallerPackageId == callerPackageId,
                cancellationToken)
            .ConfigureAwait(false)
            ?? throw new RemoteSessionServiceException(RemoteSessionServiceFailureReason.NotFound);

        if (row.Revision != request.ExpectedRevision)
        {
            throw new RemoteSessionServiceException(RemoteSessionServiceFailureReason.ConcurrencyConflict);
        }
        if (row.State is not (RemoteSessionStates.Connecting or RemoteSessionStates.Connected))
        {
            throw new RemoteSessionServiceException(RemoteSessionServiceFailureReason.InvalidTransition);
        }
        if (row.RuntimeId is null)
        {
            throw new RemoteSessionServiceException(RemoteSessionServiceFailureReason.InvalidTransition);
        }

        row.DisplayKind = null;
        row.DisplayContractVersion = null;
        row.DisplayEndpoint = null;
        row.DisplayExpiresAtUtc = null;
        row.UpdatedAtUtc = this.timeProvider.GetUtcNow();
        row.Revision = checked(row.Revision + 1);
        await this.SaveAsync(cancellationToken).ConfigureAwait(false);
        await this.PublishChangedAsync(row, cancellationToken).ConfigureAwait(false);
        return ToResponse(row);
    }

    private static ResumeRemoteSessionRequest ValidateResume(ResumeRemoteSessionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.SessionId == Guid.Empty)
        {
            throw new RemoteSessionContractException(
                "remote.session_id_invalid",
                "Remote session identity is invalid.");
        }
        if (request.ExpectedRevision < 1)
        {
            throw new RemoteSessionContractException(
                "remote.revision_invalid",
                "Remote session revision must be positive.");
        }
        return request;
    }
}
