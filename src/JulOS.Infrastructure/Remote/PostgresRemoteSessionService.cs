using System.Text;

using JulOS.Application.Concurrency;
using JulOS.Application.Remote;
using JulOS.Contracts.Remote;
using JulOS.Domain;
using JulOS.Domain.Packages;
using JulOS.Infrastructure.Persistence.Core;

using Microsoft.EntityFrameworkCore;

using Npgsql;

namespace JulOS.Infrastructure.Remote;

/// <summary>Persists user- and package-owned protocol-neutral Remote sessions.</summary>
public sealed class PostgresRemoteSessionService : IRemoteSessionService
{
    private const string IdempotencyConstraint = "ux_remote_sessions_owner_package_operation";
    private readonly CoreDbContext context;
    private readonly RemoteSessionContractValidator validator;
    private readonly TimeProvider timeProvider;

    /// <summary>Creates the PostgreSQL-backed Remote session service.</summary>
    public PostgresRemoteSessionService(
        CoreDbContext context,
        RemoteSessionContractValidator validator,
        TimeProvider timeProvider)
    {
        this.context = context ?? throw new ArgumentNullException(nameof(context));
        this.validator = validator ?? throw new ArgumentNullException(nameof(validator));
        this.timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    /// <inheritdoc />
    public async Task<RemoteSessionResponse> CreateAsync(
        CreateRemoteSessionCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var callerPackageId = ValidateCaller(command.OwnerUserId, command.CallerPackageId);
        var request = this.validator.ValidateCreate(command.Request);
        var requestIdentity = RemoteSessionContractValidator.ComputeRequestIdentity(request);
        var existing = await this.FindByOperationAsync(
            command.OwnerUserId,
            callerPackageId,
            request.OperationKey,
            cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            return MatchCreate(existing, requestIdentity);
        }

        var now = this.timeProvider.GetUtcNow();
        var row = new RemoteSessionRow
        {
            Id = Guid.CreateVersion7(now),
            OwnerUserId = command.OwnerUserId,
            CallerPackageId = callerPackageId,
            OperationKey = request.OperationKey,
            RequestIdentity = requestIdentity,
            Protocol = request.Protocol,
            TargetHost = request.Target.Host,
            TargetPort = request.Target.Port,
            SecretReferenceId = request.SecretReferenceId,
            ProfileId = request.ProfileId,
            NetworkProfileId = request.NetworkProfileId,
            ViewportWidth = request.Viewport.Width,
            ViewportHeight = request.Viewport.Height,
            DeviceScaleFactor = request.Viewport.DeviceScaleFactor,
            IdleTimeoutSeconds = request.IdleTimeoutSeconds,
            MaximumSessionSeconds = request.MaximumSessionSeconds,
            State = RemoteSessionStates.Requested,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            LastActivityAtUtc = now,
            ExpiresAtUtc = now.AddSeconds(request.MaximumSessionSeconds),
            Revision = 1,
        };
        this.context.RemoteSessions.Add(row);
        try
        {
            await this.context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return ToResponse(row);
        }
        catch (DbUpdateException exception) when (IsIdempotencyConflict(exception))
        {
            this.context.ChangeTracker.Clear();
            existing = await this.FindByOperationAsync(
                command.OwnerUserId,
                callerPackageId,
                request.OperationKey,
                cancellationToken).ConfigureAwait(false);
            return existing is null
                ? throw new RemoteSessionServiceException(
                    RemoteSessionServiceFailureReason.IdempotencyConflict,
                    exception)
                : MatchCreate(existing, requestIdentity);
        }
    }

    /// <inheritdoc />
    public async Task<RemoteSessionResponse> ReadAsync(
        ReadRemoteSessionCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var callerPackageId = ValidateCaller(command.OwnerUserId, command.CallerPackageId);
        RemoteSessionContractValidator.ValidateRead(command.Request);
        var row = await this.context.RemoteSessions.AsNoTracking()
            .SingleOrDefaultAsync(
                candidate => candidate.Id == command.Request.SessionId
                    && candidate.OwnerUserId == command.OwnerUserId
                    && candidate.CallerPackageId == callerPackageId,
                cancellationToken)
            .ConfigureAwait(false)
            ?? throw new RemoteSessionServiceException(RemoteSessionServiceFailureReason.NotFound);
        return ToResponse(row);
    }

    /// <inheritdoc />
    public async Task<RemoteSessionListResponse> ListAsync(
        ListRemoteSessionsCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var callerPackageId = ValidateCaller(command.OwnerUserId, command.CallerPackageId);
        var request = RemoteSessionContractValidator.ValidateList(command.Request);
        var query = this.context.RemoteSessions.AsNoTracking()
            .Where(candidate => candidate.OwnerUserId == command.OwnerUserId
                && candidate.CallerPackageId == callerPackageId);
        if (request.States.Count > 0)
        {
            query = query.Where(candidate => request.States.Contains(candidate.State));
        }
        if (request.Cursor is not null)
        {
            var cursorId = DecodeCursor(request.Cursor);
            query = query.Where(candidate => candidate.Id.CompareTo(cursorId) < 0);
        }

        var rows = await query
            .OrderByDescending(candidate => candidate.Id)
            .Take(request.Limit + 1)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var hasMore = rows.Count > request.Limit;
        if (hasMore)
        {
            rows.RemoveAt(rows.Count - 1);
        }
        var nextCursor = hasMore && rows.Count > 0
            ? EncodeCursor(rows[^1].Id)
            : null;
        return new RemoteSessionListResponse(
            rows.Select(ToResponse).ToArray(),
            nextCursor);
    }

    /// <inheritdoc />
    public async Task<RemoteSessionResponse> CancelAsync(
        CancelRemoteSessionCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var callerPackageId = ValidateCaller(command.OwnerUserId, command.CallerPackageId);
        var request = RemoteSessionContractValidator.ValidateCancel(command.Request);
        var row = await this.context.RemoteSessions
            .SingleOrDefaultAsync(
                candidate => candidate.Id == request.SessionId
                    && candidate.OwnerUserId == command.OwnerUserId
                    && candidate.CallerPackageId == callerPackageId,
                cancellationToken)
            .ConfigureAwait(false)
            ?? throw new RemoteSessionServiceException(RemoteSessionServiceFailureReason.NotFound);
        if (string.Equals(row.State, RemoteSessionStates.Cancelled, StringComparison.Ordinal))
        {
            return ToResponse(row);
        }
        if (row.Revision != request.ExpectedRevision)
        {
            throw new RemoteSessionServiceException(RemoteSessionServiceFailureReason.ConcurrencyConflict);
        }
        if (row.RuntimeId is not null)
        {
            throw new RemoteSessionServiceException(RemoteSessionServiceFailureReason.InvalidTransition);
        }
        try
        {
            RemoteSessionContractValidator.ValidateTransition(row.State, RemoteSessionStates.Cancelled);
        }
        catch (RemoteSessionContractException exception)
        {
            throw new RemoteSessionServiceException(
                RemoteSessionServiceFailureReason.InvalidTransition,
                exception);
        }

        var now = this.timeProvider.GetUtcNow();
        row.State = RemoteSessionStates.Cancelled;
        row.CancellationOperationKey = request.OperationKey;
        row.CancellationReason = request.Reason;
        row.UpdatedAtUtc = now;
        row.LastActivityAtUtc = now;
        row.EndedAtUtc = now;
        row.Revision = checked(row.Revision + 1);
        try
        {
            await this.context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return ToResponse(row);
        }
        catch (ConcurrencyConflictException exception)
        {
            throw new RemoteSessionServiceException(
                RemoteSessionServiceFailureReason.ConcurrencyConflict,
                exception);
        }
    }

    private async Task<RemoteSessionRow?> FindByOperationAsync(
        Guid ownerUserId,
        string callerPackageId,
        string operationKey,
        CancellationToken cancellationToken) =>
        await this.context.RemoteSessions.AsNoTracking()
            .SingleOrDefaultAsync(
                candidate => candidate.OwnerUserId == ownerUserId
                    && candidate.CallerPackageId == callerPackageId
                    && candidate.OperationKey == operationKey,
                cancellationToken)
            .ConfigureAwait(false);

    private static RemoteSessionResponse MatchCreate(
        RemoteSessionRow row,
        string requestIdentity) =>
        string.Equals(row.RequestIdentity, requestIdentity, StringComparison.Ordinal)
            ? ToResponse(row)
            : throw new RemoteSessionServiceException(
                RemoteSessionServiceFailureReason.IdempotencyConflict);

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

    private static bool IsIdempotencyConflict(DbUpdateException exception) =>
        exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: IdempotencyConstraint,
        };

    private static string EncodeCursor(Guid sessionId)
    {
        var bytes = Encoding.UTF8.GetBytes(sessionId.ToString("N"));
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static Guid DecodeCursor(string cursor)
    {
        var encoded = cursor.Replace('-', '+').Replace('_', '/');
        encoded = encoded.PadRight(encoded.Length + (4 - encoded.Length % 4) % 4, '=');
        try
        {
            var value = Encoding.UTF8.GetString(Convert.FromBase64String(encoded));
            return Guid.TryParseExact(value, "N", out var sessionId) && sessionId != Guid.Empty
                ? sessionId
                : throw new RemoteSessionServiceException(
                    RemoteSessionServiceFailureReason.CursorInvalid);
        }
        catch (FormatException exception)
        {
            throw new RemoteSessionServiceException(
                RemoteSessionServiceFailureReason.CursorInvalid,
                exception);
        }
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
