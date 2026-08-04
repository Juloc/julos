using System.Text.Json;

using JulOS.Application.Concurrency;
using JulOS.Application.Events;
using JulOS.Application.Remote;
using JulOS.Contracts.Remote;
using JulOS.Infrastructure.Persistence.Core;

using Microsoft.EntityFrameworkCore;

namespace JulOS.Infrastructure.Remote;

/// <summary>Applies trusted provider results to durable Remote sessions.</summary>
public sealed class PostgresRemoteSessionConnectionService : IRemoteSessionConnectionService
{
    private static readonly TimeSpan MinimumActivityWriteInterval = TimeSpan.FromSeconds(15);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly CoreDbContext context;
    private readonly IRemoteSessionService sessions;
    private readonly IRealtimeEventPublisher events;
    private readonly TimeProvider timeProvider;

    /// <summary>Creates the PostgreSQL-backed provider result service.</summary>
    public PostgresRemoteSessionConnectionService(
        CoreDbContext context,
        IRemoteSessionService sessions,
        IRealtimeEventPublisher events,
        TimeProvider timeProvider)
    {
        this.context = context ?? throw new ArgumentNullException(nameof(context));
        this.sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
        this.events = events ?? throw new ArgumentNullException(nameof(events));
        this.timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    /// <inheritdoc />
    public async Task<RemoteSessionResponse> ConnectAsync(
        ConnectRemoteSessionCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ValidateIdentity(command.SessionId, command.RuntimeId);
        ValidateRevision(command.ExpectedRevision);
        var row = await this.FindAsync(command.SessionId, cancellationToken).ConfigureAwait(false);
        ValidateRuntime(row, command.RuntimeId);
        if (string.Equals(row.State, RemoteSessionStates.Connected, StringComparison.Ordinal))
        {
            return await this.ReadAsync(row, cancellationToken).ConfigureAwait(false);
        }
        ValidateRevision(row, command.ExpectedRevision);
        ValidateTransition(row.State, RemoteSessionStates.Connected);

        var now = this.timeProvider.GetUtcNow();
        row.State = RemoteSessionStates.Connected;
        row.ConnectedAtUtc = now;
        row.UpdatedAtUtc = now;
        row.LastActivityAtUtc = now;
        row.Revision = checked(row.Revision + 1);
        await this.SaveAsync(cancellationToken).ConfigureAwait(false);
        return await this.ReadAndPublishAsync(row, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<RemoteSessionResponse> FailAsync(
        FailRemoteSessionCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ValidateIdentity(command.SessionId, command.RuntimeId);
        ValidateRevision(command.ExpectedRevision);
        var failure = ValidateFailure(command.Code, command.Detail, command.Retryable);
        var row = await this.FindAsync(command.SessionId, cancellationToken).ConfigureAwait(false);
        if (string.Equals(row.State, RemoteSessionStates.Failed, StringComparison.Ordinal))
        {
            if (MatchesFailure(row, command.RuntimeId, failure))
            {
                return await this.ReadAsync(row, cancellationToken).ConfigureAwait(false);
            }
            throw new RemoteSessionServiceException(RemoteSessionServiceFailureReason.InvalidTransition);
        }

        ValidateRuntime(row, command.RuntimeId);
        ValidateRevision(row, command.ExpectedRevision);
        ValidateTransition(row.State, RemoteSessionStates.Failed);

        var now = this.timeProvider.GetUtcNow();
        row.State = RemoteSessionStates.Failed;
        row.DisplayKind = null;
        row.DisplayContractVersion = null;
        row.DisplayEndpoint = null;
        row.DisplayExpiresAtUtc = null;
        row.FailureCode = failure.Code;
        row.FailureDetail = failure.Detail;
        row.FailureRetryable = failure.Retryable;
        row.UpdatedAtUtc = now;
        row.LastActivityAtUtc = now;
        row.EndedAtUtc = now;
        row.Revision = checked(row.Revision + 1);
        await this.SaveAsync(cancellationToken).ConfigureAwait(false);
        return await this.ReadAndPublishAsync(row, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task RecordActivityAsync(
        RecordRemoteSessionActivityCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ValidateIdentity(command.SessionId, command.RuntimeId);
        var row = await this.FindAsync(command.SessionId, cancellationToken).ConfigureAwait(false);
        ValidateRuntime(row, command.RuntimeId);
        if (!string.Equals(row.State, RemoteSessionStates.Connected, StringComparison.Ordinal))
        {
            throw new RemoteSessionServiceException(RemoteSessionServiceFailureReason.InvalidTransition);
        }

        var now = this.timeProvider.GetUtcNow();
        if (now - row.LastActivityAtUtc < MinimumActivityWriteInterval)
        {
            return;
        }

        row.LastActivityAtUtc = now;
        row.UpdatedAtUtc = now;
        row.Revision = checked(row.Revision + 1);
        await this.SaveAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<RemoteSessionRow> FindAsync(Guid sessionId, CancellationToken cancellationToken) =>
        await this.context.RemoteSessions
            .SingleOrDefaultAsync(row => row.Id == sessionId, cancellationToken)
            .ConfigureAwait(false)
        ?? throw new RemoteSessionServiceException(RemoteSessionServiceFailureReason.NotFound);

    private async Task<RemoteSessionResponse> ReadAndPublishAsync(
        RemoteSessionRow row,
        CancellationToken cancellationToken)
    {
        var response = await this.ReadAsync(row, cancellationToken).ConfigureAwait(false);
        await this.events.PublishAsync(
            new RealtimeEventNotification(
                "remote.session.changed",
                $"remote-{row.Id:N}",
                row.Id.ToString("D"),
                row.Revision,
                JsonSerializer.SerializeToElement(response, JsonOptions)),
            cancellationToken).ConfigureAwait(false);
        return response;
    }

    private Task<RemoteSessionResponse> ReadAsync(
        RemoteSessionRow row,
        CancellationToken cancellationToken) =>
        this.sessions.ReadAsync(
            new ReadRemoteSessionCommand(
                row.OwnerUserId,
                row.CallerPackageId,
                new ReadRemoteSessionRequest(row.Id)),
            cancellationToken);

    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await this.context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (ConcurrencyConflictException exception)
        {
            throw new RemoteSessionServiceException(
                RemoteSessionServiceFailureReason.ConcurrencyConflict,
                exception);
        }
    }

    private static void ValidateIdentity(Guid sessionId, string runtimeId)
    {
        if (sessionId == Guid.Empty
            || string.IsNullOrWhiteSpace(runtimeId)
            || runtimeId != runtimeId.Trim()
            || runtimeId.Length > 128
            || runtimeId.Any(char.IsControl))
        {
            throw new RemoteSessionServiceException(RemoteSessionServiceFailureReason.InvalidCaller);
        }
    }

    private static void ValidateRuntime(RemoteSessionRow row, string runtimeId)
    {
        if (!string.Equals(row.RuntimeId, runtimeId, StringComparison.Ordinal))
        {
            throw new RemoteSessionServiceException(RemoteSessionServiceFailureReason.NotFound);
        }
    }

    private static void ValidateRevision(long revision)
    {
        if (revision < 1)
        {
            throw new RemoteSessionServiceException(RemoteSessionServiceFailureReason.ConcurrencyConflict);
        }
    }

    private static void ValidateRevision(RemoteSessionRow row, long expectedRevision)
    {
        if (row.Revision != expectedRevision)
        {
            throw new RemoteSessionServiceException(RemoteSessionServiceFailureReason.ConcurrencyConflict);
        }
    }

    private static void ValidateTransition(string currentState, string nextState)
    {
        try
        {
            RemoteSessionContractValidator.ValidateTransition(currentState, nextState);
        }
        catch (RemoteSessionContractException exception)
        {
            throw new RemoteSessionServiceException(
                RemoteSessionServiceFailureReason.InvalidTransition,
                exception);
        }
    }

    private static RemoteSessionFailureResponse ValidateFailure(
        string code,
        string detail,
        bool retryable)
    {
        if (code is not (RemoteSessionFailureCodes.RuntimeUnavailable
            or RemoteSessionFailureCodes.TrustRequired
            or RemoteSessionFailureCodes.AuthenticationFailed
            or RemoteSessionFailureCodes.ConnectionLost))
        {
            throw new RemoteSessionContractException(
                "remote.provider_failure_invalid",
                "Remote provider failure code is invalid.");
        }

        detail = detail.Trim();
        if (detail.Length is < 1 or > 1024 || detail.Any(char.IsControl))
        {
            throw new RemoteSessionContractException(
                "remote.provider_failure_invalid",
                "Remote provider failure detail is invalid.");
        }
        return new RemoteSessionFailureResponse(code, detail, retryable);
    }

    private static bool MatchesFailure(
        RemoteSessionRow row,
        string runtimeId,
        RemoteSessionFailureResponse failure) =>
        (string.Equals(row.RuntimeId, runtimeId, StringComparison.Ordinal)
            || row.RuntimeId is null
                && string.Equals(runtimeId, $"remote-{row.Id:N}", StringComparison.Ordinal))
        && string.Equals(row.FailureCode, failure.Code, StringComparison.Ordinal)
        && string.Equals(row.FailureDetail, failure.Detail, StringComparison.Ordinal)
        && row.FailureRetryable == failure.Retryable;
}
