using JulOS.Contracts.Catalog;

namespace JulOS.Application.Catalog;

/// <summary>Why a catalog-source or publisher-key request was refused.</summary>
public enum CatalogFailureReason
{
    /// <summary>A field or a requested transition is not acceptable.</summary>
    Invalid = 1,

    /// <summary>The addressed source or key does not exist.</summary>
    NotFound = 2,

    /// <summary>The source is a tombstone and is not changed further.</summary>
    Removed = 3,

    /// <summary>Another live source already reads from the same location.</summary>
    Duplicate = 4,
}

/// <summary>A refused catalog-source or publisher-key request.</summary>
public sealed class CatalogFailureException : Exception
{
    /// <summary>Creates a failure.</summary>
    /// <param name="code">A stable code from <see cref="CatalogErrorCodes"/>.</param>
    /// <param name="reason">What kind of refusal this is.</param>
    /// <param name="message">Sanitized text safe to return to the caller.</param>
    public CatalogFailureException(string code, CatalogFailureReason reason, string message)
        : base(message)
    {
        this.Code = code;
        this.Reason = reason;
    }

    /// <summary>Creates a failure without a message.</summary>
    public CatalogFailureException()
        : this(CatalogErrorCodes.SourceInvalid, CatalogFailureReason.Invalid, "The catalog request is invalid.")
    {
    }

    /// <summary>Creates a failure with a message.</summary>
    /// <param name="message">Sanitized text safe to return to the caller.</param>
    public CatalogFailureException(string message)
        : this(CatalogErrorCodes.SourceInvalid, CatalogFailureReason.Invalid, message)
    {
    }

    /// <summary>Creates a failure with a message and an inner cause.</summary>
    /// <param name="message">Sanitized text safe to return to the caller.</param>
    /// <param name="innerException">The cause.</param>
    public CatalogFailureException(string message, Exception innerException)
        : base(message, innerException)
    {
        this.Code = CatalogErrorCodes.SourceInvalid;
        this.Reason = CatalogFailureReason.Invalid;
    }

    /// <summary>The stable public failure code.</summary>
    public string Code { get; } = CatalogErrorCodes.SourceInvalid;

    /// <summary>What kind of refusal this is.</summary>
    public CatalogFailureReason Reason { get; } = CatalogFailureReason.Invalid;
}

/// <summary>
/// Administers the catalog sources this installation reads application definitions from,
/// and the administrator decisions about the publisher keys those sources publish.
/// </summary>
/// <remarks>
/// <para>
/// Managing sources and deciding trust are separate permissions and therefore separate
/// concerns on this interface. Adding a source says where to look; trusting a key says
/// whose signature is enough to install from.
/// </para>
/// <para>
/// This service administers source configuration only. It never fetches a source: the
/// refresh adapters call into it to record what a refresh achieved.
/// </para>
/// </remarks>
public interface ICatalogSourceService
{
    /// <summary>Lists the configured sources.</summary>
    /// <param name="includeRemoved">Whether tombstones are included.</param>
    /// <param name="cancellationToken">Operation cancellation.</param>
    Task<IReadOnlyList<CatalogSourceResponse>> ListAsync(
        bool includeRemoved,
        CancellationToken cancellationToken = default);

    /// <summary>Reads one source.</summary>
    /// <param name="catalogSourceId">Source to read.</param>
    /// <param name="cancellationToken">Operation cancellation.</param>
    /// <exception cref="CatalogFailureException">The source does not exist.</exception>
    Task<CatalogSourceResponse> ReadAsync(
        Guid catalogSourceId,
        CancellationToken cancellationToken = default);

    /// <summary>Adds a source.</summary>
    /// <param name="request">Kind, name, location, credentials reference and trust level.</param>
    /// <param name="actingUserId">The authenticated administrator, recorded in the audit trail.</param>
    /// <param name="correlationId">Correlation identifier of the calling request.</param>
    /// <param name="remoteAddress">Caller address, when known.</param>
    /// <param name="cancellationToken">Operation cancellation.</param>
    /// <exception cref="CatalogFailureException">A field is invalid or the location is already configured.</exception>
    Task<CatalogSourceResponse> AddAsync(
        AddCatalogSourceRequest request,
        Guid actingUserId,
        string correlationId,
        string? remoteAddress,
        CancellationToken cancellationToken = default);

    /// <summary>Changes what an administrator may change about a source.</summary>
    /// <param name="catalogSourceId">Source to change.</param>
    /// <param name="request">New values and the revision the caller last read.</param>
    /// <param name="actingUserId">The authenticated administrator, recorded in the audit trail.</param>
    /// <param name="correlationId">Correlation identifier of the calling request.</param>
    /// <param name="remoteAddress">Caller address, when known.</param>
    /// <param name="cancellationToken">Operation cancellation.</param>
    /// <exception cref="CatalogFailureException">The source is missing, removed or the change is invalid.</exception>
    Task<CatalogSourceResponse> UpdateAsync(
        Guid catalogSourceId,
        UpdateCatalogSourceRequest request,
        Guid actingUserId,
        string correlationId,
        string? remoteAddress,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a source, leaving a tombstone.
    /// </summary>
    /// <remarks>
    /// Installed applications keep pointing at it: a deployment records which source it came
    /// from, and that history must stay resolvable after the source is gone.
    /// </remarks>
    /// <param name="catalogSourceId">Source to remove.</param>
    /// <param name="revision">The revision the caller last read.</param>
    /// <param name="actingUserId">The authenticated administrator, recorded in the audit trail.</param>
    /// <param name="correlationId">Correlation identifier of the calling request.</param>
    /// <param name="remoteAddress">Caller address, when known.</param>
    /// <param name="cancellationToken">Operation cancellation.</param>
    /// <exception cref="CatalogFailureException">The source is missing or is the official one.</exception>
    Task<CatalogSourceResponse> RemoveAsync(
        Guid catalogSourceId,
        int revision,
        Guid actingUserId,
        string correlationId,
        string? remoteAddress,
        CancellationToken cancellationToken = default);

    /// <summary>Lists the publisher keys observed in one source.</summary>
    /// <param name="catalogSourceId">Source whose keys are listed.</param>
    /// <param name="cancellationToken">Operation cancellation.</param>
    /// <exception cref="CatalogFailureException">The source does not exist.</exception>
    Task<IReadOnlyList<CatalogPublisherKeyResponse>> ListPublisherKeysAsync(
        Guid catalogSourceId,
        CancellationToken cancellationToken = default);

    /// <summary>Reads one publisher key.</summary>
    /// <param name="catalogPublisherKeyId">Key to read.</param>
    /// <param name="cancellationToken">Operation cancellation.</param>
    /// <exception cref="CatalogFailureException">The key does not exist.</exception>
    Task<CatalogPublisherKeyResponse> ReadPublisherKeyAsync(
        Guid catalogPublisherKeyId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Records an administrator decision about one publisher key.
    /// </summary>
    /// <remarks>
    /// The decision changes future evaluation only. It never rewrites the signature state
    /// recorded for anything already installed, so installed history stays explainable.
    /// </remarks>
    /// <param name="catalogPublisherKeyId">Key to decide about.</param>
    /// <param name="request">The decision and the revision the caller last read.</param>
    /// <param name="actingUserId">The authenticated administrator making the decision.</param>
    /// <param name="correlationId">Correlation identifier of the calling request.</param>
    /// <param name="remoteAddress">Caller address, when known.</param>
    /// <param name="cancellationToken">Operation cancellation.</param>
    /// <exception cref="CatalogFailureException">The key is missing or the decision is invalid.</exception>
    Task<CatalogPublisherKeyResponse> SetPublisherKeyTrustAsync(
        Guid catalogPublisherKeyId,
        SetPublisherKeyTrustRequest request,
        Guid actingUserId,
        string correlationId,
        string? remoteAddress,
        CancellationToken cancellationToken = default);
}
