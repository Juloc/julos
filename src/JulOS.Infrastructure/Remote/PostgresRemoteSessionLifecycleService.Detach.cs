using JulOS.Application.Remote;
using JulOS.Contracts.Remote;

using Microsoft.EntityFrameworkCore;

namespace JulOS.Infrastructure.Remote;

public sealed partial class PostgresRemoteSessionLifecycleService
{
    /// <inheritdoc />
    public async Task<RemoteSessionResponse> DetachAsync(
        DetachRemoteSessionCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var callerPackageId = ValidateCaller(command.OwnerUserId, command.CallerPackageId);
        var request = ValidateDetach(command.Request);
        if (string.Equals(request.Behavior, RemoteWindowDetachBehaviors.Disconnect, StringComparison.Ordinal))
        {
            return await this.DisconnectAsync(
                new DisconnectRemoteSessionCommand(
                    command.OwnerUserId,
                    callerPackageId,
                    new DisconnectRemoteSessionRequest(
                        request.SessionId,
                        request.ExpectedRevision,
                        "Window detached.")),
                cancellationToken).ConfigureAwait(false);
        }

        var row = await this.context.RemoteSessions
            .SingleOrDefaultAsync(
                candidate => candidate.Id == request.SessionId
                    && candidate.OwnerUserId == command.OwnerUserId
                    && candidate.CallerPackageId == callerPackageId,
                cancellationToken)
            .ConfigureAwait(false)
            ?? throw new RemoteSessionServiceException(RemoteSessionServiceFailureReason.NotFound);
        if (RemoteSessionStates.IsTerminal(row.State))
        {
            return ToResponse(row);
        }
        if (row.Revision != request.ExpectedRevision)
        {
            throw new RemoteSessionServiceException(RemoteSessionServiceFailureReason.ConcurrencyConflict);
        }
        if (row.State is not (RemoteSessionStates.Connecting or RemoteSessionStates.Connected))
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

    private static DetachRemoteSessionRequest ValidateDetach(DetachRemoteSessionRequest request)
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
        if (request.Behavior is not (RemoteWindowDetachBehaviors.KeepActive
            or RemoteWindowDetachBehaviors.Disconnect))
        {
            throw new RemoteSessionContractException(
                "remote.detach_behavior_invalid",
                "Remote window detach behavior is invalid.");
        }
        return request;
    }
}
