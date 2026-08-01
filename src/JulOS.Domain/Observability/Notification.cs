namespace JulOS.Domain.Observability;

/// <summary>
/// One message shown to one user in the notification centre.
/// </summary>
/// <remarks>
/// A notification is a delivery record, not a condition: the condition is the
/// <see cref="Problem"/>. <see cref="DeduplicationKey"/> lets a caller decide whether a
/// user has already been told this, so an event arriving on every poll produces one
/// notification instead of a stream the user learns to dismiss without reading.
/// <para>
/// Like a problem, it carries localization keys and never user-facing text or a secret.
/// </para>
/// </remarks>
public sealed class Notification
{
    private Notification(
        NotificationId id,
        ProblemSeverity severity,
        string titleKey,
        string deduplicationKey,
        DateTimeOffset createdAtUtc)
    {
        this.Id = id;
        this.Severity = severity;
        this.TitleKey = titleKey;
        this.DeduplicationKey = deduplicationKey;
        this.CreatedAtUtc = createdAtUtc;
    }

    /// <summary>The generated identity of this notification.</summary>
    public NotificationId Id { get; }

    /// <summary>How serious the underlying condition is.</summary>
    public ProblemSeverity Severity { get; }

    /// <summary>The localization key of the title. Never the title itself.</summary>
    public string TitleKey { get; }

    /// <summary>What makes two notifications the same message to this user.</summary>
    public string DeduplicationKey { get; }

    /// <summary>When the notification was created.</summary>
    public DateTimeOffset CreatedAtUtc { get; }

    /// <summary>When the user read it, if they have.</summary>
    public DateTimeOffset? ReadAtUtc { get; private set; }

    /// <summary>Whether the user still has this waiting for them.</summary>
    public bool IsUnread => this.ReadAtUtc is null;

    /// <summary>Creates a notification for one user.</summary>
    /// <exception cref="DomainRuleViolationException">A required field is missing.</exception>
    public static Notification Create(
        NotificationId id,
        ProblemSeverity severity,
        string titleKey,
        string deduplicationKey,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);

        if (string.IsNullOrWhiteSpace(titleKey) || string.IsNullOrWhiteSpace(deduplicationKey))
        {
            throw new DomainRuleViolationException(
                "notification.field.missing",
                "A notification needs a title key and a deduplication key.");
        }

        return new Notification(id, severity, titleKey, deduplicationKey, timeProvider.GetUtcNow());
    }

    /// <summary>
    /// Marks the notification read. Reading it again keeps the first time.
    /// </summary>
    public void MarkRead(TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);

        this.ReadAtUtc ??= timeProvider.GetUtcNow();
    }

    /// <summary>Returns whether this notification would repeat one the user already has.</summary>
    public bool Repeats(Notification other)
    {
        ArgumentNullException.ThrowIfNull(other);

        return string.Equals(this.DeduplicationKey, other.DeduplicationKey, StringComparison.Ordinal);
    }
}
