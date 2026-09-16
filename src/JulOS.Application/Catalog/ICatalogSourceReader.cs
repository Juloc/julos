using JulOS.Application.Secrets;
using JulOS.Domain.Catalog;

namespace JulOS.Application.Catalog;

/// <summary>Where one refresh reads from, without any database identity.</summary>
/// <param name="Kind">Which adapter interprets the location.</param>
/// <param name="Location">The configured location text.</param>
/// <param name="AuthenticationSecretReferenceId">Secret reference holding credentials, or null.</param>
public sealed record CatalogSourceLocation(
    CatalogSourceKind Kind,
    string Location,
    Guid? AuthenticationSecretReferenceId);

/// <summary>
/// One opened, content-locked view of a catalog source.
/// </summary>
/// <remarks>
/// A refresh reads every file through one snapshot so that the whole refresh sees one state
/// of the source. Reading the index from one state and the entries from another would let a
/// source change underneath a refresh that then reports success for bytes it never saw
/// together.
/// </remarks>
public abstract class CatalogSourceSnapshot : IAsyncDisposable
{
    /// <summary>The largest single file a catalog may publish, matching the repository validator.</summary>
    public const int MaximumFileBytes = 2 * 1024 * 1024;

    /// <summary>
    /// The immutable identity the transport itself provides, or null when it has none.
    /// </summary>
    /// <remarks>
    /// A Git commit or an OCI artifact digest identifies the whole source in one value. HTTPS
    /// and a local directory do not, so for those the refresh locks to the digest of the
    /// index, which binds every entry by its own digest in turn.
    /// </remarks>
    public abstract string? NativeContentIdentity { get; }

    /// <summary>Reads one file below the source root, or reports that it is absent.</summary>
    /// <param name="relativePath">A relative, normalized path that stays below the root.</param>
    /// <param name="cancellationToken">Operation cancellation.</param>
    /// <returns>The exact published bytes, or null when the source does not publish the file.</returns>
    /// <remarks>
    /// Absence is a result rather than a failure because several catalog files are optional.
    /// A definition with no signature beside it is unsigned; a source that cannot be reached
    /// at all is a different thing, and only that one is an exception.
    /// </remarks>
    /// <exception cref="CatalogRefreshException">The file is too large or the source is unreachable.</exception>
    public abstract Task<byte[]?> TryReadAsync(
        string relativePath,
        CancellationToken cancellationToken = default);

    /// <summary>Reads one file that has to exist.</summary>
    /// <param name="relativePath">A relative, normalized path that stays below the root.</param>
    /// <param name="cancellationToken">Operation cancellation.</param>
    /// <returns>The exact published bytes.</returns>
    /// <exception cref="CatalogRefreshException">The file is missing, too large or unreadable.</exception>
    public async Task<byte[]> ReadAsync(string relativePath, CancellationToken cancellationToken = default) =>
        await this.TryReadAsync(relativePath, cancellationToken).ConfigureAwait(false)
        ?? throw new CatalogRefreshException(
            CatalogRefreshException.SourceUnavailable,
            $"The catalog source does not publish '{relativePath}'.");

    /// <inheritdoc />
    public virtual ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);
        return ValueTask.CompletedTask;
    }
}

/// <summary>Opens a content-locked view of one kind of catalog source.</summary>
public interface ICatalogSourceReader
{
    /// <summary>The source kind this reader interprets.</summary>
    CatalogSourceKind Kind { get; }

    /// <summary>Opens the source.</summary>
    /// <param name="location">Where to read from.</param>
    /// <param name="credential">A lease over the source credential, or null for an anonymous source.</param>
    /// <param name="cancellationToken">Operation cancellation.</param>
    /// <remarks>
    /// The credential arrives as a lease rather than as a value, so it stays owned by the
    /// operation that acquired it and is zeroed when that operation ends.
    /// </remarks>
    /// <exception cref="CatalogRefreshException">The source is unreachable or the location is unusable.</exception>
    Task<CatalogSourceSnapshot> OpenAsync(
        CatalogSourceLocation location,
        SecretLease? credential,
        CancellationToken cancellationToken = default);
}

/// <summary>A refresh that could not complete.</summary>
/// <remarks>
/// Carries the stable code that is recorded on the source, so what an administrator reads
/// later is the same code the refresh failed with rather than a re-derived summary.
/// </remarks>
public sealed class CatalogRefreshException : Exception
{
    /// <summary>The source could not be reached or read at all.</summary>
    public const string SourceUnavailable = "catalog.source_unavailable";

    /// <summary>A file did not match the digest the index declared for it.</summary>
    public const string IntegrityMismatch = "catalog.integrity_mismatch";

    /// <summary>Creates a refresh failure.</summary>
    /// <param name="code">A stable catalog failure code.</param>
    /// <param name="message">Sanitized text safe to return to the caller.</param>
    public CatalogRefreshException(string code, string message)
        : base(message) => this.Code = code;

    /// <summary>Creates a refresh failure with an underlying cause.</summary>
    /// <param name="code">A stable catalog failure code.</param>
    /// <param name="message">Sanitized text safe to return to the caller.</param>
    /// <param name="innerException">The cause.</param>
    public CatalogRefreshException(string code, string message, Exception innerException)
        : base(message, innerException) => this.Code = code;

    /// <summary>Creates a refresh failure without a code.</summary>
    public CatalogRefreshException()
        : this(SourceUnavailable, "The catalog source could not be read.")
    {
    }

    /// <summary>Creates a refresh failure with a message.</summary>
    /// <param name="message">Sanitized text safe to return to the caller.</param>
    public CatalogRefreshException(string message)
        : this(SourceUnavailable, message)
    {
    }

    /// <summary>Creates a refresh failure with a message and an underlying cause.</summary>
    /// <param name="message">Sanitized text safe to return to the caller.</param>
    /// <param name="innerException">The cause.</param>
    public CatalogRefreshException(string message, Exception innerException)
        : this(SourceUnavailable, message, innerException)
    {
    }

    /// <summary>The stable code recorded on the source.</summary>
    public string Code { get; } = SourceUnavailable;
}
