using JulOS.Application.Concurrency;
using JulOS.Application.Operations;
using JulOS.Application.Secrets;
using JulOS.Contracts.Secrets;
using JulOS.Domain;
using JulOS.Domain.Observability;
using JulOS.Domain.Packages;
using JulOS.Infrastructure.Persistence.Core;

using Microsoft.EntityFrameworkCore;

namespace JulOS.Infrastructure.Secrets;

/// <summary>Stores authenticated ciphertext and issues operation-scoped in-memory leases.</summary>
internal sealed class PostgresSecretReferenceService : ISecretReferenceService, ISecretLeaseService
{
    private const int MaximumPurposeLength = 128;
    private const int MaximumSecretLengthBytes = 65_536;
    private const int MaximumCorrelationIdLength = 64;
    private const int MaximumRemoteAddressLength = 64;
    private readonly CoreDbContext context;
    private readonly ISecretProtector protector;
    private readonly TimeProvider timeProvider;
    private readonly SecretLeasePolicy leasePolicy;

    public PostgresSecretReferenceService(
        CoreDbContext context,
        ISecretProtector protector,
        TimeProvider timeProvider,
        SecretLeasePolicy leasePolicy)
    {
        this.context = context ?? throw new ArgumentNullException(nameof(context));
        this.protector = protector ?? throw new ArgumentNullException(nameof(protector));
        this.timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        this.leasePolicy = leasePolicy ?? throw new ArgumentNullException(nameof(leasePolicy));
    }

    /// <inheritdoc />
    public async Task<SecretReferenceSnapshot> CreateAsync(
        CreateSecretReferenceCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ValidateActorAndRequest(
            command.ActorUserId,
            command.CorrelationId,
            command.RemoteAddress,
            command.SecretValue);
        ValidateScope(command.OwningScopeType, command.OwningScopeId);
        ValidatePurpose(command.Purpose);

        var now = this.timeProvider.GetUtcNow();
        var id = Guid.CreateVersion7(now);
        var protectedValue = this.protector.Protect(
            id,
            command.OwningScopeType,
            command.OwningScopeId,
            command.Purpose,
            command.SecretValue.Span);

        var row = new SecretReferenceRow
        {
            Id = id,
            OwningScopeType = command.OwningScopeType,
            OwningScopeId = command.OwningScopeId,
            Purpose = command.Purpose,
            StorageProvider = SecretStorageProviders.CoreAesGcmV1,
            EncryptionKeyId = protectedValue.KeyId,
            Nonce = protectedValue.Nonce,
            Ciphertext = protectedValue.Ciphertext,
            AuthenticationTag = protectedValue.AuthenticationTag,
            CreatedAtUtc = now,
            Revision = 1,
        };

        this.context.SecretReferences.Add(row);
        this.AddAudit(
            row,
            command.ActorUserId,
            "secret_reference.create",
            command.CorrelationId,
            command.RemoteAddress,
            now);
        await this.context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return ToSnapshot(row);
    }

    /// <inheritdoc />
    public async Task<SecretReferenceSnapshot> ReadAsync(
        Guid secretReferenceId,
        CancellationToken cancellationToken = default)
    {
        EnsureIdentifier(secretReferenceId);
        var row = await this.context.SecretReferences
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == secretReferenceId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new SecretReferenceFailureException(SecretReferenceFailureReason.NotFound);
        return ToSnapshot(row);
    }

    /// <inheritdoc />
    public async Task<SecretReferenceSnapshot> RotateAsync(
        RotateSecretReferenceCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        EnsureIdentifier(command.SecretReferenceId);
        ValidateActorAndRequest(
            command.ActorUserId,
            command.CorrelationId,
            command.RemoteAddress,
            command.SecretValue);
        EnsureRevision(command.Revision);

        var row = await this.context.SecretReferences
            .SingleOrDefaultAsync(candidate => candidate.Id == command.SecretReferenceId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new SecretReferenceFailureException(SecretReferenceFailureReason.NotFound);

        if (row.DeletedAtUtc is not null)
        {
            throw new SecretReferenceFailureException(SecretReferenceFailureReason.Deleted);
        }

        EnsureRevision(row, command.Revision);
        var protectedValue = this.protector.Protect(
            row.Id,
            row.OwningScopeType,
            row.OwningScopeId,
            row.Purpose,
            command.SecretValue.Span);
        var now = this.timeProvider.GetUtcNow();

        row.EncryptionKeyId = protectedValue.KeyId;
        row.Nonce = protectedValue.Nonce;
        row.Ciphertext = protectedValue.Ciphertext;
        row.AuthenticationTag = protectedValue.AuthenticationTag;
        row.RotatedAtUtc = now;
        row.Revision = checked(row.Revision + 1);

        this.AddAudit(
            row,
            command.ActorUserId,
            "secret_reference.rotate",
            command.CorrelationId,
            command.RemoteAddress,
            now);
        await this.context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return ToSnapshot(row);
    }

    /// <inheritdoc />
    public async Task<SecretReferenceSnapshot> DeleteAsync(
        DeleteSecretReferenceCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        EnsureIdentifier(command.SecretReferenceId);
        ValidateActor(command.ActorUserId, command.CorrelationId, command.RemoteAddress);
        EnsureRevision(command.Revision);

        var row = await this.context.SecretReferences
            .SingleOrDefaultAsync(candidate => candidate.Id == command.SecretReferenceId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new SecretReferenceFailureException(SecretReferenceFailureReason.NotFound);

        if (row.DeletedAtUtc is not null)
        {
            return ToSnapshot(row);
        }

        EnsureRevision(row, command.Revision);
        var now = this.timeProvider.GetUtcNow();
        row.EncryptionKeyId = null;
        row.Nonce = null;
        row.Ciphertext = null;
        row.AuthenticationTag = null;
        row.DeletedAtUtc = now;
        row.Revision = checked(row.Revision + 1);

        this.AddAudit(
            row,
            command.ActorUserId,
            "secret_reference.delete",
            command.CorrelationId,
            command.RemoteAddress,
            now);
        await this.context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return ToSnapshot(row);
    }

    /// <inheritdoc />
    public async Task<SecretLease> AcquireAsync(
        Guid secretReferenceId,
        Guid operationId,
        CancellationToken cancellationToken = default)
    {
        EnsureIdentifier(secretReferenceId);
        EnsureIdentifier(operationId);

        var operation = await this.context.Operations
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == operationId, cancellationToken)
            .ConfigureAwait(false);

        if (operation is null
            || operation.State != OperationState.Running
            || operation.CancellationRequestedAtUtc is not null)
        {
            throw new SecretReferenceFailureException(SecretReferenceFailureReason.LeaseDenied);
        }

        var row = await this.context.SecretReferences
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == secretReferenceId, cancellationToken)
            .ConfigureAwait(false);

        if (row is null || row.DeletedAtUtc is not null || !ScopeOwnsOperation(row, operation))
        {
            throw new SecretReferenceFailureException(SecretReferenceFailureReason.LeaseDenied);
        }

        if (row.EncryptionKeyId is null
            || row.Nonce is null
            || row.Ciphertext is null
            || row.AuthenticationTag is null)
        {
            throw new SecretReferenceFailureException(SecretReferenceFailureReason.Unavailable);
        }

        var value = this.protector.Unprotect(
            row.Id,
            row.OwningScopeType,
            row.OwningScopeId,
            row.Purpose,
            row.EncryptionKeyId,
            row.Nonce,
            row.Ciphertext,
            row.AuthenticationTag);
        var expiresAt = this.timeProvider.GetUtcNow() + this.leasePolicy.Lifetime;

        return new SecretLease(
            row.Id,
            operation.Id,
            row.Purpose,
            value,
            expiresAt,
            this.timeProvider);
    }

    private static bool ScopeOwnsOperation(SecretReferenceRow row, OperationRow operation) =>
        row.OwningScopeType switch
        {
            SecretOwningScopeType.System => operation.SourcePackageId is null,
            SecretOwningScopeType.Package => string.Equals(
                row.OwningScopeId,
                operation.SourcePackageId,
                StringComparison.Ordinal),
            _ => false,
        };

    private void AddAudit(
        SecretReferenceRow row,
        Guid actorUserId,
        string action,
        string correlationId,
        string? remoteAddress,
        DateTimeOffset occurredAtUtc)
    {
        this.context.AuditEvents.Add(new AuditEventRow
        {
            Id = Guid.CreateVersion7(occurredAtUtc),
            OccurredAtUtc = occurredAtUtc,
            UserId = actorUserId,
            SourcePackageId = row.OwningScopeType == SecretOwningScopeType.Package
                ? row.OwningScopeId
                : null,
            Action = action,
            TargetType = "secret_reference",
            TargetId = row.Id.ToString("D", System.Globalization.CultureInfo.InvariantCulture),
            Outcome = AuditOutcome.Succeeded,
            CorrelationId = correlationId,
            RemoteAddress = remoteAddress,
            Summary = "Secret-reference metadata changed.",
            SafeDetails = "Secret value omitted.",
        });
    }

    private static SecretReferenceSnapshot ToSnapshot(SecretReferenceRow row) => new(
        row.Id,
        row.OwningScopeType,
        row.OwningScopeId,
        row.Purpose,
        row.StorageProvider,
        row.CreatedAtUtc,
        row.RotatedAtUtc,
        row.DeletedAtUtc,
        row.Revision);

    private static void ValidateActorAndRequest(
        Guid actorUserId,
        string correlationId,
        string? remoteAddress,
        ReadOnlyMemory<byte> secretValue)
    {
        ValidateActor(actorUserId, correlationId, remoteAddress);
        if (secretValue.IsEmpty || secretValue.Length > MaximumSecretLengthBytes)
        {
            throw new SecretReferenceFailureException(SecretReferenceFailureReason.Invalid);
        }
    }

    private static void ValidateActor(Guid actorUserId, string correlationId, string? remoteAddress)
    {
        EnsureIdentifier(actorUserId);
        if (!IsSafeText(correlationId, MaximumCorrelationIdLength)
            || (remoteAddress is not null && !IsSafeText(remoteAddress, MaximumRemoteAddressLength)))
        {
            throw new SecretReferenceFailureException(SecretReferenceFailureReason.Invalid);
        }
    }

    private static void ValidateScope(SecretOwningScopeType scopeType, string? scopeId)
    {
        if (scopeType == SecretOwningScopeType.System)
        {
            if (scopeId is not null)
            {
                throw new SecretReferenceFailureException(SecretReferenceFailureReason.Invalid);
            }

            return;
        }

        if (scopeType != SecretOwningScopeType.Package || string.IsNullOrWhiteSpace(scopeId))
        {
            throw new SecretReferenceFailureException(SecretReferenceFailureReason.Invalid);
        }

        try
        {
            _ = PackageId.Parse(scopeId);
        }
        catch (DomainRuleViolationException exception)
        {
            throw new SecretReferenceFailureException(SecretReferenceFailureReason.Invalid, exception);
        }
    }

    private static void ValidatePurpose(string purpose)
    {
        if (!IsSafeText(purpose, MaximumPurposeLength)
            || purpose.Any(character => !(char.IsAsciiLetterOrDigit(character)
                || character is '.' or '-' or '_')))
        {
            throw new SecretReferenceFailureException(SecretReferenceFailureReason.Invalid);
        }
    }

    private static bool IsSafeText(string value, int maximumLength) =>
        !string.IsNullOrWhiteSpace(value)
        && value.Length <= maximumLength
        && string.Equals(value, value.Trim(), StringComparison.Ordinal)
        && value.All(character => !char.IsControl(character));

    private static void EnsureIdentifier(Guid identifier)
    {
        if (identifier == Guid.Empty)
        {
            throw new SecretReferenceFailureException(SecretReferenceFailureReason.NotFound);
        }
    }

    private static void EnsureRevision(int revision)
    {
        if (revision < 1)
        {
            throw new SecretReferenceFailureException(SecretReferenceFailureReason.Invalid);
        }
    }

    private static void EnsureRevision(SecretReferenceRow row, int expectedRevision)
    {
        if (row.Revision != expectedRevision)
        {
            throw new ConcurrencyConflictException(
                row.Revision,
                new InvalidOperationException("The secret-reference revision is stale."));
        }
    }
}
