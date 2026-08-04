using System.Text.Json;

using JulOS.Application.Concurrency;
using JulOS.Application.Events;
using JulOS.Application.Remote;
using JulOS.Contracts.Remote;
using JulOS.Domain;
using JulOS.Domain.Observability;
using JulOS.Domain.Packages;
using JulOS.Infrastructure.Persistence.Core;

using Microsoft.EntityFrameworkCore;

namespace JulOS.Infrastructure.Remote;

/// <summary>Enforces Remote session deadlines and removes terminal provider runtimes.</summary>
public sealed partial class PostgresRemoteSessionLifecycleService : IRemoteSessionLifecycleService
{
    private const string CoreRemotePackageId = "julos.core.remote";
    private const string CleanupProblemType = "remote.runtime_cleanup_failed";
    private const string CleanupProblemTitleKey = "problems.remote.runtime_cleanup_failed";
    private const int MaximumReconciliationLimit = 200;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly CoreDbContext context;
    private readonly IRemoteRuntimeManager runtimeManager;
    private readonly IRealtimeEventPublisher events;
    private readonly TimeProvider timeProvider;

    /// <summary>Creates the PostgreSQL-backed lifecycle service.</summary>
    public PostgresRemoteSessionLifecycleService(
        CoreDbContext context,
        IRemoteRuntimeManager runtimeManager,
        IRealtimeEventPublisher events,
        TimeProvider timeProvider)
    {
        this.context = context ?? throw new ArgumentNullException(nameof(context));
        this.runtimeManager = runtimeManager ?? throw new ArgumentNullException(nameof(runtimeManager));
        this.events = events ?? throw new ArgumentNullException(nameof(events));
        this.timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    /// <inheritdoc />
    public async Task<RemoteSessionResponse> DisconnectAsync(
        DisconnectRemoteSessionCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var callerPackageId = ValidateCaller(command.OwnerUserId, command.CallerPackageId);
        var request = ValidateDisconnect(command.Request);
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
        if (row.State is not (RemoteSessionStates.Connecting
            or RemoteSessionStates.Connected
            or RemoteSessionStates.Disconnecting))
        {
            throw new RemoteSessionServiceException(RemoteSessionServiceFailureReason.InvalidTransition);
        }

        if (!string.Equals(row.State, RemoteSessionStates.Disconnecting, StringComparison.Ordinal))
        {
            Transition(row, RemoteSessionStates.Disconnecting, this.timeProvider.GetUtcNow());
            await this.SaveAsync(cancellationToken).ConfigureAwait(false);
            await this.PublishChangedAsync(row, cancellationToken).ConfigureAwait(false);
        }

        var cleanupSucceeded = await this.TryCleanupRuntimeAsync(row, cancellationToken).ConfigureAwait(false);
        if (!cleanupSucceeded)
        {
            return ToResponse(row);
        }

        var now = this.timeProvider.GetUtcNow();
        Transition(row, RemoteSessionStates.Disconnected, now);
        row.EndedAtUtc = now;
        await this.SaveAsync(cancellationToken).ConfigureAwait(false);
        await this.PublishChangedAsync(row, cancellationToken).ConfigureAwait(false);
        return ToResponse(row);
    }

    /// <inheritdoc />
    public async Task<RemoteLifecycleReconciliationResult> ReconcileDueAsync(
        int limit,
        CancellationToken cancellationToken = default)
    {
        if (limit is < 1 or > MaximumReconciliationLimit)
        {
            throw new ArgumentOutOfRangeException(
                nameof(limit),
                limit,
                $"Remote reconciliation limit must be from 1 through {MaximumReconciliationLimit}.");
        }

        var now = this.timeProvider.GetUtcNow();
        var earliestIdle = now.AddSeconds(-60);
        var rows = await this.context.RemoteSessions
            .Where(row =>
                row.RuntimeId != null && (row.State == RemoteSessionStates.Disconnecting
                    || row.State == RemoteSessionStates.Disconnected
                    || row.State == RemoteSessionStates.Cancelled
                    || row.State == RemoteSessionStates.Expired
                    || row.State == RemoteSessionStates.Failed)
                || row.State != RemoteSessionStates.Disconnected
                    && row.State != RemoteSessionStates.Cancelled
                    && row.State != RemoteSessionStates.Expired
                    && row.State != RemoteSessionStates.Failed
                    && (row.ExpiresAtUtc <= now
                        || row.LastActivityAtUtc <= earliestIdle))
            .OrderBy(row => row.UpdatedAtUtc)
            .ThenBy(row => row.Id)
            .Take(limit)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var expired = 0;
        var cleaned = 0;
        var failures = 0;
        foreach (var row in rows)
        {
            if (!RemoteSessionStates.IsTerminal(row.State) && IsDue(row, now))
            {
                Transition(row, RemoteSessionStates.Expired, now);
                row.EndedAtUtc = now;
                expired++;
                await this.SaveAsync(cancellationToken).ConfigureAwait(false);
                await this.PublishChangedAsync(row, cancellationToken).ConfigureAwait(false);
            }

            if (row.RuntimeId is null)
            {
                continue;
            }
            if (!await this.TryCleanupRuntimeAsync(row, cancellationToken).ConfigureAwait(false))
            {
                failures++;
                continue;
            }

            cleaned++;
            if (string.Equals(row.State, RemoteSessionStates.Disconnecting, StringComparison.Ordinal))
            {
                Transition(row, RemoteSessionStates.Disconnected, now);
                row.EndedAtUtc = now;
                await this.SaveAsync(cancellationToken).ConfigureAwait(false);
                await this.PublishChangedAsync(row, cancellationToken).ConfigureAwait(false);
            }
        }

        return new RemoteLifecycleReconciliationResult(rows.Count, expired, cleaned, failures);
    }

    private async Task<bool> TryCleanupRuntimeAsync(
        RemoteSessionRow row,
        CancellationToken cancellationToken)
    {
        var runtimeId = row.RuntimeId;
        if (runtimeId is null)
        {
            return true;
        }

        try
        {
            await this.runtimeManager.RemoveAsync(runtimeId, cancellationToken).ConfigureAwait(false);
            row.RuntimeId = null;
            row.UpdatedAtUtc = this.timeProvider.GetUtcNow();
            row.Revision = checked(row.Revision + 1);
            ResolveCleanupProblem(runtimeId, this.context, row.UpdatedAtUtc);
            await this.SaveAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (RemoteRuntimeManagerException)
        {
            ObserveCleanupProblem(runtimeId, this.context, this.timeProvider.GetUtcNow());
            await this.SaveAsync(cancellationToken).ConfigureAwait(false);
            return false;
        }
    }

    private async Task PublishChangedAsync(
        RemoteSessionRow row,
        CancellationToken cancellationToken)
    {
        var response = ToResponse(row);
        await this.events.PublishAsync(
            new RealtimeEventNotification(
                "remote.session.changed",
                $"remote-{row.Id:N}",
                row.Id.ToString("D"),
                row.Revision,
                JsonSerializer.SerializeToElement(response, JsonOptions)),
            cancellationToken).ConfigureAwait(false);
    }

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

    private static bool IsDue(RemoteSessionRow row, DateTimeOffset now) =>
        row.ExpiresAtUtc <= now
        || row.LastActivityAtUtc.AddSeconds(row.IdleTimeoutSeconds) <= now;

    private static void Transition(RemoteSessionRow row, string state, DateTimeOffset now)
    {
        try
        {
            RemoteSessionContractValidator.ValidateTransition(row.State, state);
        }
        catch (RemoteSessionContractException exception)
        {
            throw new RemoteSessionServiceException(
                RemoteSessionServiceFailureReason.InvalidTransition,
                exception);
        }

        row.State = state;
        row.DisplayKind = null;
        row.DisplayContractVersion = null;
        row.DisplayEndpoint = null;
        row.DisplayExpiresAtUtc = null;
        row.UpdatedAtUtc = now;
        row.LastActivityAtUtc = now;
        row.Revision = checked(row.Revision + 1);
    }

    private static DisconnectRemoteSessionRequest ValidateDisconnect(DisconnectRemoteSessionRequest request)
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
        var reason = request.Reason?.Trim();
        if (reason is not null
            && (reason.Length is < 1 or > 256 || reason.Any(char.IsControl)))
        {
            throw new RemoteSessionContractException(
                "remote.disconnect_reason_invalid",
                "Remote disconnect reason is invalid.");
        }
        return request with { Reason = reason };
    }

    private static string ValidateCaller(Guid ownerUserId, string callerPackageId)
    {
        if (ownerUserId == Guid.Empty)
        {
            throw new RemoteSessionServiceException(RemoteSessionServiceFailureReason.InvalidCaller);
        }
        try
        {
            return PackageId.Parse(callerPackageId).Value;
        }
        catch (DomainRuleViolationException exception)
        {
            throw new RemoteSessionServiceException(
                RemoteSessionServiceFailureReason.InvalidCaller,
                exception);
        }
    }

    private static void ObserveCleanupProblem(
        string runtimeId,
        CoreDbContext context,
        DateTimeOffset now)
    {
        var problem = context.Problems.Local.SingleOrDefault(row =>
            row.SourcePackageId == CoreRemotePackageId
            && row.ProblemType == CleanupProblemType
            && row.StableResourceIdentity == runtimeId)
            ?? context.Problems.SingleOrDefault(row =>
                row.SourcePackageId == CoreRemotePackageId
                && row.ProblemType == CleanupProblemType
                && row.StableResourceIdentity == runtimeId);
        if (problem is null)
        {
            context.Problems.Add(new ProblemRow
            {
                Id = Guid.CreateVersion7(now),
                SourcePackageId = CoreRemotePackageId,
                ProblemType = CleanupProblemType,
                StableResourceIdentity = runtimeId,
                Severity = ProblemSeverity.Error,
                State = ProblemState.Active,
                TitleKey = CleanupProblemTitleKey,
                FirstDetectedAtUtc = now,
                LastObservedAtUtc = now,
                ObservationCount = 1,
                Revision = 1,
            });
            return;
        }

        if (problem.State == ProblemState.Resolved)
        {
            problem.State = ProblemState.Active;
            problem.ResolvedAtUtc = null;
            problem.AcknowledgedAtUtc = null;
            problem.AcknowledgedByUserId = null;
        }
        problem.Severity = ProblemSeverity.Error;
        problem.LastObservedAtUtc = now;
        problem.ObservationCount = checked(problem.ObservationCount + 1);
        problem.Revision = checked(problem.Revision + 1);
    }

    private static void ResolveCleanupProblem(
        string runtimeId,
        CoreDbContext context,
        DateTimeOffset now)
    {
        var problem = context.Problems.Local.SingleOrDefault(row =>
            row.SourcePackageId == CoreRemotePackageId
            && row.ProblemType == CleanupProblemType
            && row.StableResourceIdentity == runtimeId)
            ?? context.Problems.SingleOrDefault(row =>
                row.SourcePackageId == CoreRemotePackageId
                && row.ProblemType == CleanupProblemType
                && row.StableResourceIdentity == runtimeId);
        if (problem is null || problem.State == ProblemState.Resolved)
        {
            return;
        }

        problem.State = ProblemState.Resolved;
        problem.ResolvedAtUtc = now;
        problem.Revision = checked(problem.Revision + 1);
    }

    private static RemoteSessionResponse ToResponse(RemoteSessionRow row)
    {
        var display = row.DisplayKind is not null
            && row.DisplayContractVersion is not null
            && row.DisplayEndpoint is not null
            && row.DisplayExpiresAtUtc is not null
            ? new RemoteDisplayTransportResponse(
                row.DisplayKind,
                row.DisplayContractVersion,
                row.DisplayEndpoint,
                row.DisplayExpiresAtUtc.Value)
            : null;
        var failure = row.FailureCode is not null
            && row.FailureDetail is not null
            && row.FailureRetryable is not null
            ? new RemoteSessionFailureResponse(
                row.FailureCode,
                row.FailureDetail,
                row.FailureRetryable.Value)
            : null;
        return new RemoteSessionResponse(
            row.Id,
            row.OperationKey,
            row.RequestIdentity,
            row.Protocol,
            new RemoteTargetContract(row.TargetHost, row.TargetPort),
            row.State,
            row.CreatedAtUtc,
            row.ConnectedAtUtc,
            row.EndedAtUtc,
            display,
            failure,
            row.Revision);
    }
}
