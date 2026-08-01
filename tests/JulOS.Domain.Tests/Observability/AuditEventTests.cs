using System.Reflection;

using JulOS.Domain;
using JulOS.Domain.Observability;

using Microsoft.Extensions.Time.Testing;

namespace JulOS.Domain.Tests.Observability;

/// <summary>Verifies that a recorded audit event cannot be changed after the fact.</summary>
[TestClass]
public sealed class AuditEventTests
{
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public void ARecordedEventKeepsWhatItWasGiven()
    {
        var recorded = NewEvent(AuditOutcome.Succeeded);

        Assert.AreEqual(Start, recorded.OccurredAtUtc);
        Assert.AreEqual("package.enable", recorded.Action);
        Assert.AreEqual("package-installation", recorded.TargetType);
        Assert.AreEqual(AuditOutcome.Succeeded, recorded.Outcome);
    }

    [TestMethod]
    public void NoMemberCanBeChangedAfterRecording()
    {
        var settable = typeof(AuditEvent)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.CanWrite)
            .Select(property => property.Name)
            .ToArray();

        Assert.AreEqual(
            0,
            settable.Length,
            $"An audit trail that can be edited is not evidence. Writable: {string.Join(", ", settable)}.");
    }

    [TestMethod]
    public void TheTypeOffersNoWayToDeleteOrAmendAnEvent()
    {
        var mutating = typeof(AuditEvent)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(method => !method.IsSpecialName)
            .Select(method => method.Name)
            .ToArray();

        Assert.AreEqual(0, mutating.Length, $"Unexpected instance methods: {string.Join(", ", mutating)}.");
    }

    [TestMethod]
    public void ARefusalIsRecordedSeparatelyFromAFailure()
    {
        // Someone repeatedly being denied is a security signal; something breaking is an
        // operational one. Merging them would hide the first inside the noise of the second.
        Assert.AreEqual(AuditOutcome.Denied, NewEvent(AuditOutcome.Denied).Outcome);
        Assert.AreEqual(AuditOutcome.Failed, NewEvent(AuditOutcome.Failed).Outcome);
    }

    [TestMethod]
    public void AnEventWithoutACorrelationIdentifierIsRejected()
    {
        var exception = Assert.ThrowsExactly<DomainRuleViolationException>(() => AuditEvent.Record(
            new AuditEventId(Guid.CreateVersion7()),
            "package.enable",
            "package-installation",
            "installation-1",
            AuditOutcome.Succeeded,
            correlationId: "  ",
            safeDetails: "requested by an operator",
            new FakeTimeProvider(Start)));

        Assert.AreEqual("audit.field.missing", exception.Code);
    }

    [TestMethod]
    public void AnEventWithoutATargetIsRejected()
    {
        Assert.ThrowsExactly<DomainRuleViolationException>(() => AuditEvent.Record(
            new AuditEventId(Guid.CreateVersion7()),
            "package.enable",
            targetType: string.Empty,
            targetId: "installation-1",
            AuditOutcome.Succeeded,
            "correlation-1",
            "requested by an operator",
            new FakeTimeProvider(Start)));
    }

    private static AuditEvent NewEvent(AuditOutcome outcome) => AuditEvent.Record(
        new AuditEventId(Guid.CreateVersion7()),
        "package.enable",
        "package-installation",
        "installation-1",
        outcome,
        "correlation-1",
        "requested by an operator",
        new FakeTimeProvider(Start));
}
