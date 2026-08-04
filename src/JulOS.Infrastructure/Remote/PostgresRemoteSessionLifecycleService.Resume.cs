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
        if (!string.Equals(row.State, RemoteSessionStates.Connected, StringComparison.Ordinal)
            || row.RuntimeId is null)
        {
            throw new RemoteSessionServiceException(RemoteSessionServiceFailureReason.InvalidTransition);
        }

        var nextRevision = checked(row.Revision + 1);
        RemoteDisplayTransportResponse display;
        try
        {
            display = this.displayGateway.Issue(
                row.Id,
                row.OwnerUserId,
                row.CallerPackageId,
                row.RuntimeId,
                nextRevision,
                row.ExpiresAtUtc);
        }
        catch (RemoteDisplayGatewayException exception)
        {
            throw new RemoteSessionServiceException(
                RemoteSessionServiceFailureReason.InvalidTransition,
                exception);
        }

        row.DisplayKind = display.Kind;
        row.DisplayContractVersion = display.ContractVersion;
        row.DisplayEndpoint = display.Endpoint;
        row.DisplayExpiresAtUtc = display.ExpiresAtUtc;
        row.UpdatedAtUtc = this.timeProvider.GetUtcNow();
        row.Revision = nextRevision;
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
