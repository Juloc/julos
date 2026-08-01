using JulOS.Domain;
using JulOS.Domain.Applications;
using JulOS.Domain.Packages;

using Microsoft.Extensions.Time.Testing;

namespace JulOS.Domain.Tests.Applications;

/// <summary>Verifies launch-target identity and the approval lifecycle.</summary>
[TestClass]
public sealed class LaunchTargetTests
{
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public void AProposedTargetIsNotOffered()
    {
        var target = NewTarget(new FakeTimeProvider(Start));

        Assert.AreEqual(LaunchTargetApprovalState.Proposed, target.ApprovalState);
        Assert.IsFalse(target.IsOfferable, "Observing a resource is not the same as being allowed to manage it.");
    }

    [TestMethod]
    public void AnApprovedTargetIsOffered()
    {
        var timeProvider = new FakeTimeProvider(Start);
        var target = NewTarget(timeProvider);

        target.Approve(timeProvider);

        Assert.IsTrue(target.IsOfferable);
        Assert.AreEqual(Start, target.ApprovedAtUtc);
    }

    [TestMethod]
    public void AnIgnoredTargetStaysIgnoredWhenObservedAgain()
    {
        var timeProvider = new FakeTimeProvider(Start);
        var target = NewTarget(timeProvider);

        target.Ignore();
        timeProvider.Advance(TimeSpan.FromHours(1));
        target.Observe("Example resource", timeProvider);

        Assert.AreEqual(
            LaunchTargetApprovalState.Ignored,
            target.ApprovalState,
            "A rejected target reappearing as new on every inventory pass is the behaviour this prevents.");
        Assert.IsFalse(target.IsOfferable);
    }

    [TestMethod]
    public void AnApprovedTargetStaysApprovedWhenObservedAgain()
    {
        var timeProvider = new FakeTimeProvider(Start);
        var target = NewTarget(timeProvider);

        target.Approve(timeProvider);
        timeProvider.Advance(TimeSpan.FromHours(1));
        target.Observe("Renamed resource", timeProvider);

        Assert.IsTrue(target.IsOfferable, "Re-approving after every inventory pass would make approval meaningless.");
    }

    [TestMethod]
    public void RenamingTheResourceDoesNotChangeIdentity()
    {
        var timeProvider = new FakeTimeProvider(Start);
        var target = NewTarget(timeProvider);
        var identity = target.ExternalIdentity;

        target.Observe("A completely different label", timeProvider);

        Assert.AreEqual(identity, target.ExternalIdentity);
        Assert.AreEqual("A completely different label", target.DisplayName);
    }

    [TestMethod]
    public void ObservationTimesAreRecordedSeparately()
    {
        var timeProvider = new FakeTimeProvider(Start);
        var target = NewTarget(timeProvider);

        timeProvider.Advance(TimeSpan.FromMinutes(30));
        target.Observe("Example resource", timeProvider);

        Assert.AreEqual(Start, target.FirstObservedAtUtc);
        Assert.AreEqual(Start.AddMinutes(30), target.LastObservedAtUtc);
    }

    [TestMethod]
    public void ABlankLabelIsRejected()
    {
        var timeProvider = new FakeTimeProvider(Start);

        var exception = Assert.ThrowsExactly<DomainRuleViolationException>(() => LaunchTarget.Propose(
            new LaunchTargetId(Guid.CreateVersion7()),
            new ApplicationDefinitionId(Guid.CreateVersion7()),
            PackageId.Parse("de.juloc.julos.example"),
            ExternalIdentity.Parse("resource-1"),
            "   ",
            timeProvider));

        Assert.AreEqual("launch_target.display_name.invalid", exception.Code);
    }

    private static LaunchTarget NewTarget(TimeProvider timeProvider) => LaunchTarget.Propose(
        new LaunchTargetId(Guid.CreateVersion7()),
        new ApplicationDefinitionId(Guid.CreateVersion7()),
        PackageId.Parse("de.juloc.julos.example"),
        ExternalIdentity.Parse("resource-1"),
        "Example resource",
        timeProvider);
}
