using System.Globalization;
using System.Text;

using JulOS.Application.Auditing;
using JulOS.Domain;
using JulOS.Domain.Observability;
using JulOS.Domain.Packages;
using JulOS.Infrastructure.Persistence.Core;

using Microsoft.EntityFrameworkCore;

namespace JulOS.Infrastructure.Auditing;

/// <summary>Persists immutable audit events and reads them with stable keyset pagination.</summary>
internal sealed class PostgresAuditService : IAuditService
{
    private const int MaximumPageSize = 200;
    private const int MaximumActionLength = 256;
    private const int MaximumTargetTypeLength = 128;
    private const int MaximumTargetIdLength = 512;
    private const int MaximumCorrelationIdLength = 64;
    private const int MaximumRemoteAddressLength = 128;
    private const int MaximumSummaryLength = 512;
    private const int MaximumSafeDetailsLength = 8192;

    private readonly CoreDbContext context;
    private readonly TimeProvider timeProvider;

    public PostgresAuditService(CoreDbContext context, TimeProvider timeProvider)
    {
        this.context = context ?? throw new ArgumentNullException(nameof(context));
        this.timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    /// <inheritdoc />
    public void Stage(AuditRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        ValidateRecord(record);

        PackageId? sourcePackageId = null;
        if (record.SourcePackageId is not null)
        {
            try
            {
                sourcePackageId = PackageId.Parse(record.SourcePackageId);
            }
            catch (DomainRuleViolationException exception)
            {
                throw new ArgumentException("The audit source package is invalid.", nameof(record), exception);
            }
        }

        var auditEvent = AuditEvent.Record(
            new AuditEventId(Guid.CreateVersion7(this.timeProvider.GetUtcNow())),
            record.Action,
            record.TargetType,
            record.TargetId,
            record.Outcome,
            record.CorrelationId,
            record.SafeDetails,
            this.timeProvider);

        this.context.AuditEvents.Add(AuditEventRow.FromDomain(
            auditEvent,
            record.UserId,
            record.AgentId,
            sourcePackageId,
            record.RemoteAddress,
            record.Summary));
    }

    /// <inheritdoc />
    public async Task AppendAsync(
        AuditRecord record,
        CancellationToken cancellationToken = default)
    {
        this.Stage(record);
        await this.context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<AuditPageSnapshot> QueryAsync(
        AuditQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ValidateQuery(query);

        var rows = this.context.AuditEvents.AsNoTracking().AsQueryable();

        if (query.FromUtc is not null)
        {
            rows = rows.Where(row => row.OccurredAtUtc >= query.FromUtc.Value);
        }

        if (query.ToUtc is not null)
        {
            rows = rows.Where(row => row.OccurredAtUtc <= query.ToUtc.Value);
        }

        if (query.UserId is not null)
        {
            rows = rows.Where(row => row.UserId == query.UserId.Value);
        }

        if (query.AgentId is not null)
        {
            rows = rows.Where(row => row.AgentId == query.AgentId.Value);
        }

        if (query.SourcePackageId is not null)
        {
            rows = rows.Where(row => row.SourcePackageId == query.SourcePackageId);
        }

        if (query.Action is not null)
        {
            rows = rows.Where(row => row.Action == query.Action);
        }

        if (query.TargetType is not null)
        {
            rows = rows.Where(row => row.TargetType == query.TargetType);
        }

        if (query.TargetId is not null)
        {
            rows = rows.Where(row => row.TargetId == query.TargetId);
        }

        if (query.Outcome is not null)
        {
            rows = rows.Where(row => row.Outcome == query.Outcome.Value);
        }

        if (query.Cursor is not null)
        {
            var cursor = DecodeCursor(query.Cursor);
            rows = rows.Where(row =>
                row.OccurredAtUtc < cursor.OccurredAtUtc
                || (row.OccurredAtUtc == cursor.OccurredAtUtc && row.Id.CompareTo(cursor.Id) < 0));
        }

        var materialized = await rows
            .OrderByDescending(row => row.OccurredAtUtc)
            .ThenByDescending(row => row.Id)
            .Take(query.Limit + 1)
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);

        var hasMore = materialized.Length > query.Limit;
        var visible = hasMore ? materialized[..query.Limit] : materialized;
        var events = visible.Select(ToSnapshot).ToArray();
        var nextCursor = hasMore ? EncodeCursor(visible[^1]) : null;

        return new AuditPageSnapshot(events, nextCursor);
    }

    private static AuditEventSnapshot ToSnapshot(AuditEventRow row) => new(
        row.Id,
        row.OccurredAtUtc,
        row.UserId,
        row.AgentId,
        row.SourcePackageId,
        row.Action,
        row.TargetType,
        row.TargetId,
        row.Outcome,
        row.CorrelationId,
        row.RemoteAddress,
        row.Summary,
        row.SafeDetails);

    private static void ValidateRecord(AuditRecord record)
    {
        if (record.UserId == Guid.Empty || record.AgentId == Guid.Empty)
        {
            throw new ArgumentException("Audit actor identifiers cannot be empty.", nameof(record));
        }

        ValidateRequiredText(record.Action, MaximumActionLength, nameof(record.Action));
        ValidateRequiredText(record.TargetType, MaximumTargetTypeLength, nameof(record.TargetType));
        ValidateRequiredText(record.TargetId, MaximumTargetIdLength, nameof(record.TargetId));
        ValidateRequiredText(record.Summary, MaximumSummaryLength, nameof(record.Summary));

        if (!IsCorrelationId(record.CorrelationId))
        {
            throw new ArgumentException("The audit correlation identifier is invalid.", nameof(record));
        }

        if (record.RemoteAddress is not null)
        {
            ValidateRequiredText(
                record.RemoteAddress,
                MaximumRemoteAddressLength,
                nameof(record.RemoteAddress));
        }

        if (record.SafeDetails.Length > MaximumSafeDetailsLength
            || record.SafeDetails.Any(char.IsControl))
        {
            throw new ArgumentException("The audit safe details are invalid.", nameof(record));
        }
    }

    private static void ValidateQuery(AuditQuery query)
    {
        if (query.Limit is < 1 or > MaximumPageSize)
        {
            throw new ArgumentOutOfRangeException(
                nameof(query),
                $"Audit page size must be between 1 and {MaximumPageSize}.");
        }

        if (query.FromUtc is not null && query.ToUtc is not null && query.FromUtc > query.ToUtc)
        {
            throw new ArgumentException("The audit time range is invalid.", nameof(query));
        }

        if (query.UserId == Guid.Empty || query.AgentId == Guid.Empty)
        {
            throw new ArgumentException("Audit filter identifiers cannot be empty.", nameof(query));
        }

        ValidateOptionalFilter(query.SourcePackageId, 128, nameof(query.SourcePackageId));
        ValidateOptionalFilter(query.Action, MaximumActionLength, nameof(query.Action));
        ValidateOptionalFilter(query.TargetType, MaximumTargetTypeLength, nameof(query.TargetType));
        ValidateOptionalFilter(query.TargetId, MaximumTargetIdLength, nameof(query.TargetId));

        if (query.SourcePackageId is not null)
        {
            try
            {
                _ = PackageId.Parse(query.SourcePackageId);
            }
            catch (DomainRuleViolationException exception)
            {
                throw new ArgumentException("The audit package filter is invalid.", nameof(query), exception);
            }
        }

        if (query.Cursor is not null)
        {
            _ = DecodeCursor(query.Cursor);
        }
    }

    private static void ValidateOptionalFilter(string? value, int maximumLength, string name)
    {
        if (value is not null)
        {
            ValidateRequiredText(value, maximumLength, name);
        }
    }

    private static void ValidateRequiredText(string value, int maximumLength, string name)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value.Length > maximumLength
            || !string.Equals(value, value.Trim(), StringComparison.Ordinal)
            || value.Any(char.IsControl))
        {
            throw new ArgumentException($"The audit field '{name}' is invalid.", name);
        }
    }

    private static bool IsCorrelationId(string value) =>
        !string.IsNullOrWhiteSpace(value)
        && value.Length <= MaximumCorrelationIdLength
        && value.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_');

    private static string EncodeCursor(AuditEventRow row)
    {
        var value = string.Create(
            CultureInfo.InvariantCulture,
            $"{row.OccurredAtUtc.UtcDateTime.Ticks}|{row.Id:D}");
        return ToBase64Url(Encoding.UTF8.GetBytes(value));
    }

    private static AuditCursor DecodeCursor(string value)
    {
        try
        {
            var decoded = Encoding.UTF8.GetString(FromBase64Url(value));
            var separator = decoded.IndexOf('|', StringComparison.Ordinal);
            if (separator <= 0
                || !long.TryParse(
                    decoded.AsSpan(0, separator),
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out var ticks)
                || !Guid.TryParse(decoded.AsSpan(separator + 1), out var id)
                || id == Guid.Empty)
            {
                throw new FormatException();
            }

            return new AuditCursor(new DateTimeOffset(ticks, TimeSpan.Zero), id);
        }
        catch (Exception exception) when (exception is FormatException or ArgumentException)
        {
            throw new ArgumentException("The audit cursor is invalid.", nameof(value), exception);
        }
    }

    private static string ToBase64Url(byte[] value) => Convert
        .ToBase64String(value)
        .TrimEnd('=')
        .Replace('+', '-')
        .Replace('/', '_');

    private static byte[] FromBase64Url(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        padded += padded.Length % 4 switch
        {
            0 => string.Empty,
            2 => "==",
            3 => "=",
            _ => throw new FormatException(),
        };
        return Convert.FromBase64String(padded);
    }

    private sealed record AuditCursor(DateTimeOffset OccurredAtUtc, Guid Id);
}
