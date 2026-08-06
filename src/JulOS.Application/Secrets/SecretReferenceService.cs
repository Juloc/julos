using System.Security.Cryptography;

namespace JulOS.Application.Secrets;

/// <summary>Owning boundaries that can receive an operation-scoped secret lease.</summary>
public enum SecretOwningScopeType
{
    /// <summary>The value belongs to Core and may be leased only to a Core operation.</summary>
    System = 1,

    /// <summary>The value belongs to one package and may be leased only to that package's operation.</summary>
    Package = 2,
}

/// <summary>Input for creating one encrypted secret reference.</summary>
public sealed record CreateSecretReferenceCommand(
    Guid ActorUserId,
    SecretOwningScopeType OwningScopeType,
    string? OwningScopeId,
    string Purpose,
    ReadOnlyMemory<byte> SecretValue,
    string CorrelationId,
    string? RemoteAddress);

/// <summary>Input for rotating one encrypted secret value.</summary>
public sealed record RotateSecretReferenceCommand(
    Guid SecretReferenceId,
    Guid ActorUserId,
    ReadOnlyMemory<byte> SecretValue,
    int Revision,
    string CorrelationId,
    string? RemoteAddress);

/// <summary>Input for destroying one encrypted secret value.</summary>
public sealed record DeleteSecretReferenceCommand(
    Guid SecretReferenceId,
    Guid ActorUserId,
    int Revision,
    string CorrelationId,
    string? RemoteAddress);

/// <summary>Persistence-independent non-secret metadata.</summary>
public sealed record SecretReferenceSnapshot(
    Guid SecretReferenceId,
    SecretOwningScopeType OwningScopeType,
    string? OwningScopeId,
    string Purpose,
    string StorageProvider,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? RotatedAtUtc,
    DateTimeOffset? DeletedAtUtc,
    int Revision)
{
    /// <summary>Whether encrypted value material is still present.</summary>
    public bool IsPresent => this.DeletedAtUtc is null;
}

/// <summary>Creates, reads, rotates and deletes opaque secret references.</summary>
public interface ISecretReferenceService
{
    /// <summary>Encrypts a submitted value and returns metadata only.</summary>
    Task<SecretReferenceSnapshot> CreateAsync(
        CreateSecretReferenceCommand command,
        CancellationToken cancellationToken = default);

    /// <summary>Reads non-secret metadata for one reference.</summary>
    Task<SecretReferenceSnapshot> ReadAsync(
        Guid secretReferenceId,
        CancellationToken cancellationToken = default);

    /// <summary>Replaces encrypted value material without changing the opaque reference.</summary>
    Task<SecretReferenceSnapshot> RotateAsync(
        RotateSecretReferenceCommand command,
        CancellationToken cancellationToken = default);

    /// <summary>Destroys encrypted value material while retaining a metadata tombstone.</summary>
    Task<SecretReferenceSnapshot> DeleteAsync(
        DeleteSecretReferenceCommand command,
        CancellationToken cancellationToken = default);
}

/// <summary>Issues short-lived in-memory credentials to the operation that owns the work.</summary>
public interface ISecretLeaseService
{
    /// <summary>Acquires a lease only when the operation is running and its scope owns the reference.</summary>
    Task<SecretLease> AcquireAsync(
        Guid secretReferenceId,
        Guid operationId,
        CancellationToken cancellationToken = default);
}

/// <summary>A short-lived in-memory secret value that zeroes its buffer when disposed or expired.</summary>
public sealed class SecretLease : IDisposable
{
    private const int ActiveState = 0;
    private const int DisposedState = 1;
    private const int ExpiredState = 2;

    private readonly TimeProvider timeProvider;
    private ITimer? expiryTimer;
    private byte[]? value;
    private int state;

    /// <summary>Creates one lease over an owned value buffer.</summary>
    public SecretLease(
        Guid secretReferenceId,
        Guid operationId,
        string purpose,
        byte[] value,
        DateTimeOffset expiresAtUtc,
        TimeProvider timeProvider)
    {
        if (secretReferenceId == Guid.Empty)
        {
            throw new ArgumentException("A secret-reference identifier is required.", nameof(secretReferenceId));
        }

        if (operationId == Guid.Empty)
        {
            throw new ArgumentException("An operation identifier is required.", nameof(operationId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(purpose);
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(timeProvider);

        if (value.Length == 0)
        {
            throw new ArgumentException("A leased value cannot be empty.", nameof(value));
        }

        var now = timeProvider.GetUtcNow();
        if (expiresAtUtc <= now)
        {
            throw new ArgumentOutOfRangeException(nameof(expiresAtUtc), expiresAtUtc, "A lease must expire in the future.");
        }

        this.SecretReferenceId = secretReferenceId;
        this.OperationId = operationId;
        this.Purpose = purpose;
        this.value = value;
        this.ExpiresAtUtc = expiresAtUtc;
        this.timeProvider = timeProvider;
        this.expiryTimer = timeProvider.CreateTimer(
            static state => ((SecretLease)state!).Expire(),
            this,
            expiresAtUtc - now,
            Timeout.InfiniteTimeSpan);
    }

    /// <summary>The opaque reference that was leased.</summary>
    public Guid SecretReferenceId { get; }

    /// <summary>The running operation authorized to use this lease.</summary>
    public Guid OperationId { get; }

    /// <summary>The non-secret purpose of the value.</summary>
    public string Purpose { get; }

    /// <summary>When access to the in-memory value expires.</summary>
    public DateTimeOffset ExpiresAtUtc { get; }

    /// <summary>The leased bytes while the lease is active.</summary>
    /// <exception cref="ObjectDisposedException">The lease was disposed.</exception>
    /// <exception cref="SecretReferenceFailureException">The lease expired.</exception>
    public ReadOnlyMemory<byte> Value
    {
        get
        {
            var currentState = Volatile.Read(ref this.state);
            if (currentState == ExpiredState)
            {
                throw new SecretReferenceFailureException(SecretReferenceFailureReason.LeaseExpired);
            }

            ObjectDisposedException.ThrowIf(currentState == DisposedState, this);

            if (this.timeProvider.GetUtcNow() >= this.ExpiresAtUtc)
            {
                this.Expire();
                throw new SecretReferenceFailureException(SecretReferenceFailureReason.LeaseExpired);
            }

            return Volatile.Read(ref this.value)
                ?? throw new ObjectDisposedException(nameof(SecretLease));
        }
    }

    /// <summary>Zeros the owned value buffer.</summary>
    public void Dispose()
    {
        if (Interlocked.CompareExchange(ref this.state, DisposedState, ActiveState) == ActiveState)
        {
            this.ReleaseValue();
        }

        GC.SuppressFinalize(this);
    }

    private void Expire()
    {
        if (Interlocked.CompareExchange(ref this.state, ExpiredState, ActiveState) == ActiveState)
        {
            this.ReleaseValue();
        }
    }

    private void ReleaseValue()
    {
        var timer = Interlocked.Exchange(ref this.expiryTimer, null);
        timer?.Dispose();

        var current = Interlocked.Exchange(ref this.value, null);
        if (current is not null)
        {
            CryptographicOperations.ZeroMemory(current);
        }
    }
}
