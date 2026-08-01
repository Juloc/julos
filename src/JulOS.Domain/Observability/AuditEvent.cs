namespace JulOS.Domain.Observability;

/// <summary>
/// One recorded mutation, kept for as long as the retention policy requires.
/// </summary>
/// <remarks>
/// The type is append-only by construction: every member is read-only and there is no
/// operation that changes or deletes a recorded event. An audit trail that can be edited
/// is not evidence of anything.
/// <para>
/// <see cref="SafeDetails"/> is sanitized by the caller that owns the operation. A secret
/// value, a credential payload or a connection string must never reach it, because the
/// audit log is readable by administrators who are not entitled to those values.
/// </para>
/// </remarks>
public sealed class AuditEvent
{
    private AuditEvent(
        AuditEventId id,
        DateTimeOffset occurredAtUtc,
        string action,
        string targetType,
        string targetId,
        AuditOutcome outcome,
        string correlationId,
        string safeDetails)
    {
        this.Id = id;
        this.OccurredAtUtc = occurredAtUtc;
        this.Action = action;
        this.TargetType = targetType;
        this.TargetId = targetId;
        this.Outcome = outcome;
        this.CorrelationId = correlationId;
        this.SafeDetails = safeDetails;
    }

    /// <summary>The generated identity of this event.</summary>
    public AuditEventId Id { get; }

    /// <summary>When the recorded operation happened.</summary>
    public DateTimeOffset OccurredAtUtc { get; }

    /// <summary>The stable name of the operation, for example <c>package.enable</c>.</summary>
    public string Action { get; }

    /// <summary>The kind of thing the operation acted on.</summary>
    public string TargetType { get; }

    /// <summary>The stable identity of the thing the operation acted on.</summary>
    public string TargetId { get; }

    /// <summary>Whether the operation succeeded, failed or was refused.</summary>
    public AuditOutcome Outcome { get; }

    /// <summary>The correlation identifier of the request that caused the operation.</summary>
    public string CorrelationId { get; }

    /// <summary>Sanitized detail about the operation. Never a secret.</summary>
    public string SafeDetails { get; }

    /// <summary>Records one completed operation.</summary>
    /// <exception cref="DomainRuleViolationException">A required field is missing.</exception>
    public static AuditEvent Record(
        AuditEventId id,
        string action,
        string targetType,
        string targetId,
        AuditOutcome outcome,
        string correlationId,
        string safeDetails,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);

        return new AuditEvent(
            id,
            timeProvider.GetUtcNow(),
            Required(action, nameof(action)),
            Required(targetType, nameof(targetType)),
            Required(targetId, nameof(targetId)),
            outcome,
            Required(correlationId, nameof(correlationId)),
            safeDetails ?? string.Empty);
    }

    private static string Required(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new DomainRuleViolationException(
                "audit.field.missing",
                $"An audit event without '{name}' cannot be traced back to what happened.");
        }

        return value;
    }
}
