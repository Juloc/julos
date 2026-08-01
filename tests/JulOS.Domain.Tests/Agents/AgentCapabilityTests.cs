using JulOS.Domain.Agents;
using JulOS.Domain.Primitives;

using Microsoft.Extensions.Time.Testing;

namespace JulOS.Domain.Tests.Agents;

/// <summary>Verifies the advertised-capability record and its enable/disable lifecycle.</summary>
[TestClass]
public sealed class AgentCapabilityTests
{
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static readonly AgentId OwningAgentId = new(Guid.CreateVersion7());

    [TestMethod]
    public void AdvertisingACapabilityStartsEnabledAtTheInitialRevision()
    {
        var capability = NewCapability(new FakeTimeProvider(Start));

        Assert.IsTrue(capability.Enabled);
        Assert.AreEqual(Revision.Initial, capability.Revision);
        Assert.AreEqual(Start, capability.ObservedAtUtc);
        Assert.AreEqual(OwningAgentId, capability.AgentId);
    }

    [TestMethod]
    public void DisablingStopsOfferingItWithoutForgettingIt()
    {
        var timeProvider = new FakeTimeProvider(Start);
        var capability = NewCapability(timeProvider);

        capability.Disable();

        Assert.IsFalse(capability.Enabled);
        Assert.AreEqual(CapabilityName.Parse("system.metrics"), capability.Name);
    }

    [TestMethod]
    public void ARefreshDoesNotReenableADisabledCapability()
    {
        var timeProvider = new FakeTimeProvider(Start);
        var capability = NewCapability(timeProvider);
        capability.Disable();

        timeProvider.Advance(TimeSpan.FromHours(1));
        capability.Refresh(CapabilityVersion.From(2), CapabilityMetadata.Parse("updated"), timeProvider);

        Assert.IsFalse(
            capability.Enabled,
            "An administrator's decision to disable a capability must survive the Agent re-advertising it.");
        Assert.AreEqual(CapabilityVersion.From(2), capability.MetadataVersion);
        Assert.AreEqual(Start.AddHours(1), capability.ObservedAtUtc);
    }

    [TestMethod]
    public void EnablingAfterADisableOffersItAgain()
    {
        var timeProvider = new FakeTimeProvider(Start);
        var capability = NewCapability(timeProvider);
        capability.Disable();

        capability.Enable();

        Assert.IsTrue(capability.Enabled);
    }

    private static AgentCapability NewCapability(TimeProvider timeProvider) => AgentCapability.Advertise(
        new AgentCapabilityId(Guid.CreateVersion7()),
        OwningAgentId,
        CapabilityName.Parse("system.metrics"),
        CapabilityVersion.Initial,
        CapabilityVersion.Initial,
        CapabilityMetadata.Empty,
        timeProvider);
}
