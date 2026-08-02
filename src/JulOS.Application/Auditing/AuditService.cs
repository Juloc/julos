using JulOS.Domain.Observability;

namespace JulOS.Application.Auditing;

/// <summary>One sanitized audit record staged by the owner of an operation.</summary>
public sealed record AuditRecord(
    Guid? UserId,
    Guid? AgentId,
    string? SourcePackageId,
    string Action,
    string TargetType,
    string TargetId,
    AuditOutcome Outcome,
    string CorrelationId,
    string? RemoteAddress,
    string Summary,
    string SafeDetails);

/// <summary>Explicit filters and cursor input for an audit query.</summary>
public sealed record AuditQuery(
    int Limit,
    string? Cursor,
    DateTimeOffset? FromUtc,
    DateTimeOffset? ToUtc,
    Guid? UserId,
    Guid? AgentId,
    string? SourcePackageId,
    string? Action,
    string? TargetType,
    string? TargetId,
    AuditOutcome? Outcome);

/// <summary>Persistence-independent audit event data.</summary>
public sealed record AuditEventSnapshot(
    Guid AuditEventId,
    DateTimeOffset OccurredAtUtc,
    Guid? UserId,
    Guid? AgentId,
    string? SourcePackageId,
    string Action,
    string TargetType,
    string TargetId,
    AuditOutcome Outcome,
    string CorrelationId,
    string? RemoteAddress,
    string Summary,
    string SafeDetails);

/// <summary>One retention-safe page of audit events.</summary>
public sealed record AuditPageSnapshot(
    IReadOnlyList<AuditEventSnapshot> Events,
    string? NextCursor);

/// <summary>Stages immutable audit records and queries the authoritative append-only store.</summary>
public interface IAuditService
{
    /// <summary>
    /// Adds one audit event to the current unit of work without saving it independently.
    /// </summary>
    void Stage(AuditRecord record);

    /// <summary>Adds and persists one audit event when no owning unit of work exists.</summary>
    Task AppendAsync(
        AuditRecord record,
        CancellationToken cancellationToken = default);

    /// <summary>Reads one opaque cursor page in descending occurrence order.</summary>
    Task<AuditPageSnapshot> QueryAsync(
        AuditQuery query,
        CancellationToken cancellationToken = default);
}
