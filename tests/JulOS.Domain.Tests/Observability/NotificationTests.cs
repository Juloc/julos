using JulOS.Domain;
using JulOS.Domain.Observability;

using Microsoft.Extensions.Time.Testing;

namespace JulOS.Domain.Tests.Observability;

/// <summary>Verifies notification deduplication and read state.</summary>
[TestClass]
public sealed class NotificationTests
{
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public void ANewNotificationIsUnread()
    {
        var notification = NewNotification("resource-1.unreachable", new FakeTimeProvider(Start));

        Assert.IsTrue(notification.IsUnread);
        Assert.IsNull(notification.ReadAtUtc);
    }

    [TestMethod]
    public void ReadingTwiceKeepsTheFirstTime()
    {
        var timeProvider = new FakeTimeProvider(Start);
        var notification = NewNotification("resource-1.unreachable", timeProvider);

        notification.MarkRead(timeProvider);
        timeProvider.Advance(TimeSpan.FromHours(1));
        notification.MarkRead(timeProvider);

        Assert.AreEqual(Start, notification.ReadAtUtc);
    }

    [TestMethod]
    public void TwoNotificationsOfTheSameConditionRepeatEachOther()
    {
        var timeProvider = new FakeTimeProvider(Start);
        var first = NewNotification("resource-1.unreachable", timeProvider);
        var second = NewNotification("resource-1.unreachable", timeProvider);

        Assert.IsTrue(
            second.Repeats(first),
            "An event arriving on every poll must be recognisable as the same message.");
    }

    [TestMethod]
    public void NotificationsAboutDifferentResourcesDoNotRepeat()
    {
        var timeProvider = new FakeTimeProvider(Start);
        var first = NewNotification("resource-1.unreachable", timeProvider);
        var second = NewNotification("resource-2.unreachable", timeProvider);

        Assert.IsFalse(second.Repeats(first));
    }

    [TestMethod]
    public void ANotificationWithoutADeduplicationKeyIsRejected()
    {
        var exception = Assert.ThrowsExactly<DomainRuleViolationException>(() => Notification.Create(
            new NotificationId(Guid.CreateVersion7()),
            ProblemSeverity.Warning,
            "notification.resource.unreachable.title",
            deduplicationKey: "   ",
            new FakeTimeProvider(Start)));

        Assert.AreEqual("notification.field.missing", exception.Code);
    }

    private static Notification NewNotification(string deduplicationKey, TimeProvider timeProvider) =>
        Notification.Create(
            new NotificationId(Guid.CreateVersion7()),
            ProblemSeverity.Warning,
            "notification.resource.unreachable.title",
            deduplicationKey,
            timeProvider);
}
