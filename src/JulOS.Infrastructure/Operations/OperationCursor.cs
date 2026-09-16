using System.Buffers.Text;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

using JulOS.Application.Operations;

namespace JulOS.Infrastructure.Operations;

/// <summary>
/// The opaque continuation of one Operation Center page.
/// </summary>
/// <remarks>
/// <para>
/// The cursor carries the final sort tuple of the page it continues, together with a
/// fingerprint of the complete filter set. Binding the filters is what makes a cursor
/// refuse to be reused against a different query: continuing a "failed operations" page
/// into an unfiltered list would otherwise silently skip whatever sorted between them.
/// </para>
/// <para>
/// It is opaque but not secret. It encodes nothing the caller did not send and nothing
/// they cannot already see, so it needs no key; what it needs is to be unforgeable in the
/// sense that a tampered cursor is rejected rather than quietly changing the page.
/// </para>
/// </remarks>
internal readonly record struct OperationCursor(DateTimeOffset CreatedAtUtc, Guid OperationId)
{
    private const char Separator = '|';

    /// <summary>Summarises the filters a cursor is bound to.</summary>
    internal static string Fingerprint(OperationQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);

        var states = query.States is { Count: > 0 }
            ? string.Join(',', query.States.Distinct().Select(state => state.ToString()).Order(StringComparer.Ordinal))
            : string.Empty;
        var canonical = string.Join(
            Separator,
            query.OwnerUserId.ToString("D", CultureInfo.InvariantCulture),
            states,
            query.SourcePackageId ?? string.Empty,
            query.CreatedAfterUtc?.UtcDateTime.ToString("O", CultureInfo.InvariantCulture) ?? string.Empty);

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)))[..16];
    }

    /// <summary>Encodes the continuation of a page.</summary>
    internal static string Write(DateTimeOffset createdAtUtc, Guid operationId, string fingerprint)
    {
        var payload = string.Join(
            Separator,
            fingerprint,
            createdAtUtc.UtcDateTime.ToString("O", CultureInfo.InvariantCulture),
            operationId.ToString("D", CultureInfo.InvariantCulture));
        return Base64Url.EncodeToString(Encoding.UTF8.GetBytes(payload));
    }

    /// <summary>
    /// Decodes a continuation, refusing anything that does not belong to this query.
    /// </summary>
    /// <returns>The position to continue from, or null when the caller sent no cursor.</returns>
    /// <exception cref="OperationFailureException">The cursor is malformed or belongs to other filters.</exception>
    internal static OperationCursor? Read(string? cursor, string fingerprint)
    {
        if (string.IsNullOrWhiteSpace(cursor))
        {
            return null;
        }

        string payload;
        try
        {
            payload = Encoding.UTF8.GetString(Base64Url.DecodeFromChars(cursor));
        }
        catch (FormatException)
        {
            throw new OperationFailureException(OperationFailureReason.Invalid);
        }

        var parts = payload.Split(Separator);
        if (parts.Length != 3
            || !string.Equals(parts[0], fingerprint, StringComparison.Ordinal)
            || !DateTimeOffset.TryParse(
                parts[1],
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out var createdAtUtc)
            || !Guid.TryParseExact(parts[2], "D", out var operationId))
        {
            throw new OperationFailureException(OperationFailureReason.Invalid);
        }

        return new OperationCursor(createdAtUtc, operationId);
    }
}
