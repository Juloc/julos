namespace JulOS.Contracts.Auditing;

/// <summary>Stable transport names for audit outcomes.</summary>
public static class AuditOutcomeNames
{
    /// <summary>The requested operation completed successfully.</summary>
    public const string Succeeded = "succeeded";

    /// <summary>The requested operation ran but failed.</summary>
    public const string Failed = "failed";

    /// <summary>The requested operation was refused.</summary>
    public const string Denied = "denied";
}

/// <summary>One immutable audit event returned by the control-plane API.</summary>
/// <param name="AuditEventId">The stable event identifier.</param>
/// <param name="OccurredAtUtc">When the event occurred.</param>
/// <param name="UserId">The acting local user when one was known.</param>
/// <param name="AgentId">The acting Agent when one was known.</param>
/// <param name="SourcePackageId">The package that owned the action when applicable.</param>
/// <param name="Action">The stable action name.</param>
/// <param name="TargetType">The stable target kind.</param>
/// <param name="TargetId">The stable target identity.</param>
/// <param name="Outcome">The stable outcome name.</param>
/// <param name="CorrelationId">The request correlation identifier.</param>
/// <param name="RemoteAddress">The sanitized remote address when available.</param>
/// <param name="Summary">A caller-safe summary.</param>
/// <param name="SafeDetails">Sanitized details that never contain credentials or secret values.</param>
public sealed record AuditEventResponse(
    Guid AuditEventId,
    DateTimeOffset OccurredAtUtc,
    Guid? UserId,
    Guid? AgentId,
    string? SourcePackageId,
    string Action,
    string TargetType,
    string TargetId,
    string Outcome,
    string CorrelationId,
    string? RemoteAddress,
    string Summary,
    string SafeDetails);

/// <summary>A retention-safe cursor page of audit events.</summary>
/// <param name="Events">The events in descending occurrence order.</param>
/// <param name="NextCursor">An opaque cursor for the next older page, or null at the end.</param>
public sealed record AuditEventPageResponse(
    IReadOnlyList<AuditEventResponse> Events,
    string? NextCursor);
