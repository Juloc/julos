using System.Security.Cryptography;
using System.Text;

using JulOS.Application.Operations;
using JulOS.Domain;
using JulOS.Domain.Packages;
using JulOS.Infrastructure.Persistence.Core;

using Microsoft.EntityFrameworkCore;

using Npgsql;

namespace JulOS.Infrastructure.Operations;

/// <summary>Persists durable operation resources and progress events in the Core PostgreSQL store.</summary>
public sealed class PostgresOperationService : IOperationService
{
    private const int MaximumOperationTypeLength = 128;
    private const int MaximumTargetReferenceLength = 512;
    private const int MaximumIdempotencyKeyLength = 128;
    private const int MaximumCorrelationIdLength = 64;
    private const int MaximumCurrentStepLength = 256;
    private const int MaximumFailureCodeLength = 128;
    private const int MaximumFailureDetailLength = 1024;
    private readonly CoreDbContext context;

    /// <summary>Creates the Core-backed operation service.</summary>
    public PostgresOperationService(CoreDbContext context)
    {
        this.context = context ?? throw new ArgumentNullException(nameof(context));
    }

    /// <inheritdoc />
    public async Task<OperationSnapshot> CreateAsync(CreateOperationCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ValidateCreate(command);
        var fingerprint = CreateFingerprint(command);
        var existing = await this.FindByIdempotencyKeyAsync(command.OwnerUserId, command.IdempotencyKey, cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            return MatchIdempotentRequest(existing, fingerprint);
        }

        var now = TimeProvider.System.GetUtcNow();
        var operation = new OperationRow
        {
            Id = Guid.CreateVersion7(now),
            OwnerUserId = command.OwnerUserId,
            OperationType = command.OperationType,
            SourcePackageId = command.SourcePackageId,
            TargetReference = command.TargetReference,
            IdempotencyKey = command.IdempotencyKey,
            RequestFingerprint = fingerprint,
            State = OperationState.Queued,
            CreatedAtUtc = now,
            CorrelationId = command.CorrelationId,
            Revision = 1,
        };
        this.context.Operations.Add(operation);
        try
        {
            await this.context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return ToSnapshot(operation);
        }
        catch (DbUpdateException exception) when (IsIdempotencyConflict(exception))
        {
            this.context.ChangeTracker.Clear();
            existing = await this.FindByIdempotencyKeyAsync(command.OwnerUserId, command.IdempotencyKey, cancellationToken).ConfigureAwait(false);
            return existing is null
                ? throw new OperationFailureException(OperationFailureReason.IdempotencyConflict, exception)
                : MatchIdempotentRequest(existing, fingerprint);
        }
    }

    /// <inheritdoc />
    public async Task<OperationSnapshot> ReadAsync(Guid operationId, Guid ownerUserId, CancellationToken cancellationToken = default)
    {
        EnsureIdentifier(operationId);
        EnsureIdentifier(ownerUserId);
        var operation = await this.context.Operations.AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == operationId && candidate.OwnerUserId == ownerUserId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new OperationFailureException(OperationFailureReason.NotFound);
        return ToSnapshot(operation);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<OperationProgressSnapshot>> ReadProgressAsync(Guid operationId, Guid ownerUserId, CancellationToken cancellationToken = default)
    {
        _ = await this.ReadAsync(operationId, ownerUserId, cancellationToken).ConfigureAwait(false);
        return await this.context.OperationProgressEvents.AsNoTracking()
            .Where(candidate => candidate.OperationId == operationId)
            .OrderBy(candidate => candidate.OccurredAtUtc)
            .ThenBy(candidate => candidate.Id)
            .Select(candidate => new OperationProgressSnapshot(candidate.Id, candidate.OperationId, candidate.ProgressPercent, candidate.CurrentStep, candidate.OccurredAtUtc))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<OperationSnapshot> RequestCancellationAsync(Guid operationId, Guid ownerUserId, CancellationToken cancellationToken = default)
    {
        var operation = await this.FindOwnedAsync(operationId, ownerUserId, cancellationToken).ConfigureAwait(false);
        var now = TimeProvider.System.GetUtcNow();
        switch (operation.State)
        {
            case OperationState.Queued:
                operation.State = OperationState.Cancelled;
                operation.CompletedAtUtc = now;
                operation.CancellationRequestedAtUtc = now;
                operation.Revision = checked(operation.Revision + 1);
                break;
            case OperationState.Running when operation.CancellationRequestedAtUtc is null:
                operation.CancellationRequestedAtUtc = now;
                operation.Revision = checked(operation.Revision + 1);
                break;
            case OperationState.Running:
                return ToSnapshot(operation);
            default:
                throw new OperationFailureException(OperationFailureReason.NotCancellable);
        }

        await this.context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return ToSnapshot(operation);
    }

    /// <inheritdoc />
    public Task<OperationSnapshot> MarkRunningAsync(Guid operationId, CancellationToken cancellationToken = default) =>
        this.AdvanceAsync(operationId, OperationState.Queued, operation =>
        {
            operation.State = OperationState.Running;
            operation.StartedAtUtc = TimeProvider.System.GetUtcNow();
        }, cancellationToken);

    /// <inheritdoc />
    public async Task<OperationSnapshot> ReportProgressAsync(Guid operationId, int? progressPercent, string currentStep, CancellationToken cancellationToken = default)
    {
        EnsureIdentifier(operationId);
        ValidateProgress(progressPercent, currentStep);
        var operation = await this.FindAsync(operationId, cancellationToken).ConfigureAwait(false);
        EnsureState(operation, OperationState.Running);
        var now = TimeProvider.System.GetUtcNow();
        operation.ProgressPercent = progressPercent;
        operation.CurrentStep = currentStep;
        operation.Revision = checked(operation.Revision + 1);
        this.context.OperationProgressEvents.Add(new OperationProgressEventRow
        {
            Id = Guid.CreateVersion7(now),
            OperationId = operation.Id,
            ProgressPercent = progressPercent,
            CurrentStep = currentStep,
            OccurredAtUtc = now,
        });
        await this.context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return ToSnapshot(operation);
    }

    /// <inheritdoc />
    public Task<OperationSnapshot> MarkSucceededAsync(Guid operationId, CancellationToken cancellationToken = default) =>
        this.AdvanceAsync(operationId, OperationState.Running, operation =>
        {
            operation.State = OperationState.Succeeded;
            operation.ProgressPercent = 100;
            operation.CompletedAtUtc = TimeProvider.System.GetUtcNow();
        }, cancellationToken);

    /// <inheritdoc />
    public async Task<OperationSnapshot> MarkFailedAsync(Guid operationId, string failureCode, string safeFailureDetail, CancellationToken cancellationToken = default)
    {
        EnsureIdentifier(operationId);
        ValidateSafeFailure(failureCode, safeFailureDetail);
        var operation = await this.FindAsync(operationId, cancellationToken).ConfigureAwait(false);
        EnsureState(operation, OperationState.Running);
        operation.State = OperationState.Failed;
        operation.FailureCode = failureCode;
        operation.FailureDetail = safeFailureDetail;
        operation.CompletedAtUtc = TimeProvider.System.GetUtcNow();
        operation.Revision = checked(operation.Revision + 1);
        await this.context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return ToSnapshot(operation);
    }

    /// <inheritdoc />
    public async Task<OperationSnapshot> MarkCancelledAsync(Guid operationId, CancellationToken cancellationToken = default)
    {
        EnsureIdentifier(operationId);
        var operation = await this.FindAsync(operationId, cancellationToken).ConfigureAwait(false);
        if (operation.State is not OperationState.Queued and not OperationState.Running)
        {
            throw new OperationFailureException(OperationFailureReason.InvalidTransition);
        }

        var now = TimeProvider.System.GetUtcNow();
        operation.State = OperationState.Cancelled;
        operation.CompletedAtUtc = now;
        operation.CancellationRequestedAtUtc ??= now;
        operation.Revision = checked(operation.Revision + 1);
        await this.context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return ToSnapshot(operation);
    }

    private async Task<OperationSnapshot> AdvanceAsync(Guid operationId, OperationState requiredState, Action<OperationRow> update, CancellationToken cancellationToken)
    {
        EnsureIdentifier(operationId);
        ArgumentNullException.ThrowIfNull(update);
        var operation = await this.FindAsync(operationId, cancellationToken).ConfigureAwait(false);
        EnsureState(operation, requiredState);
        update(operation);
        operation.Revision = checked(operation.Revision + 1);
        await this.context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return ToSnapshot(operation);
    }

    private async Task<OperationRow> FindAsync(Guid operationId, CancellationToken cancellationToken) =>
        await this.context.Operations.SingleOrDefaultAsync(candidate => candidate.Id == operationId, cancellationToken).ConfigureAwait(false)
        ?? throw new OperationFailureException(OperationFailureReason.NotFound);

    private async Task<OperationRow> FindOwnedAsync(Guid operationId, Guid ownerUserId, CancellationToken cancellationToken)
    {
        EnsureIdentifier(operationId);
        EnsureIdentifier(ownerUserId);
        return await this.context.Operations
            .SingleOrDefaultAsync(candidate => candidate.Id == operationId && candidate.OwnerUserId == ownerUserId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new OperationFailureException(OperationFailureReason.NotFound);
    }

    private async Task<OperationRow?> FindByIdempotencyKeyAsync(Guid ownerUserId, string idempotencyKey, CancellationToken cancellationToken) =>
        await this.context.Operations.AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.OwnerUserId == ownerUserId && candidate.IdempotencyKey == idempotencyKey, cancellationToken)
            .ConfigureAwait(false);

    private static OperationSnapshot MatchIdempotentRequest(OperationRow operation, string fingerprint) =>
        string.Equals(operation.RequestFingerprint, fingerprint, StringComparison.Ordinal)
            ? ToSnapshot(operation)
            : throw new OperationFailureException(OperationFailureReason.IdempotencyConflict);

    private static bool IsIdempotencyConflict(DbUpdateException exception) =>
        exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: "ux_operations_owner_idempotency",
        };

    private static string CreateFingerprint(CreateOperationCommand command)
    {
        var canonical = string.Join("\n", command.OperationType, command.SourcePackageId ?? string.Empty, command.TargetReference);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    private static void ValidateCreate(CreateOperationCommand command)
    {
        EnsureIdentifier(command.OwnerUserId);
        if (!IsSafeText(command.OperationType, MaximumOperationTypeLength)
            || !IsSafeText(command.TargetReference, MaximumTargetReferenceLength)
            || !IsSafeText(command.IdempotencyKey, MaximumIdempotencyKeyLength)
            || !IsSafeText(command.CorrelationId, MaximumCorrelationIdLength))
        {
            throw new OperationFailureException(OperationFailureReason.Invalid);
        }

        if (command.SourcePackageId is not null)
        {
            try
            {
                _ = PackageId.Parse(command.SourcePackageId);
            }
            catch (DomainRuleViolationException exception)
            {
                throw new OperationFailureException(OperationFailureReason.Invalid, exception);
            }
        }
    }

    private static void ValidateProgress(int? progressPercent, string currentStep)
    {
        if (progressPercent is < 0 or > 100 || !IsSafeText(currentStep, MaximumCurrentStepLength))
        {
            throw new OperationFailureException(OperationFailureReason.Invalid);
        }
    }

    private static void ValidateSafeFailure(string failureCode, string safeFailureDetail)
    {
        if (!IsSafeText(failureCode, MaximumFailureCodeLength) || !IsSafeText(safeFailureDetail, MaximumFailureDetailLength))
        {
            throw new OperationFailureException(OperationFailureReason.Invalid);
        }
    }

    private static bool IsSafeText(string? value, int maximumLength) =>
        !string.IsNullOrWhiteSpace(value)
        && value.Length <= maximumLength
        && value.All(character => !char.IsControl(character));

    private static void EnsureIdentifier(Guid identifier)
    {
        if (identifier == Guid.Empty)
        {
            throw new OperationFailureException(OperationFailureReason.NotFound);
        }
    }

    private static void EnsureState(OperationRow operation, OperationState requiredState)
    {
        if (operation.State != requiredState)
        {
            throw new OperationFailureException(OperationFailureReason.InvalidTransition);
        }
    }

    private static OperationSnapshot ToSnapshot(OperationRow operation) => new(
        operation.Id, operation.OperationType, operation.OwnerUserId, operation.SourcePackageId,
        operation.TargetReference, operation.State, operation.ProgressPercent, operation.CurrentStep,
        operation.CreatedAtUtc, operation.StartedAtUtc, operation.CompletedAtUtc, operation.FailureCode,
        operation.FailureDetail, operation.CorrelationId, operation.CancellationRequestedAtUtc, operation.Revision);
}
