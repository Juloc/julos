using System.Reflection;

using JulOS.Domain.Agents;

using Microsoft.Extensions.Time.Testing;

namespace JulOS.Domain.Tests.Agents;

/// <summary>
/// Verifies that a heartbeat is connectivity evidence only and cannot be turned into a host
/// observation. This is a structural guarantee, not just a behavioral one, so these tests
/// inspect the type's public surface rather than only its values.
/// </summary>
[TestClass]
public sealed class AgentHeartbeatTests
{
    [TestMethod]
    public void NowRecordsTheCurrentTime()
    {
        var timeProvider = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));

        var heartbeat = AgentHeartbeat.Now(timeProvider);

        Assert.AreEqual(timeProvider.GetUtcNow(), heartbeat.AtUtc);
    }

    [TestMethod]
    public void NowRejectsAMissingClock()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() => AgentHeartbeat.Now(null!));
    }

    [TestMethod]
    public void CarriesNoObservationPayloadBesidesTheMoment()
    {
        var properties = typeof(AgentHeartbeat).GetProperties(BindingFlags.Public | BindingFlags.Instance);

        Assert.AreEqual(
            1,
            properties.Length,
            "A heartbeat must carry nothing but the moment it happened, or it could be read as a host measurement.");
        Assert.AreEqual(nameof(AgentHeartbeat.AtUtc), properties[0].Name);
        Assert.AreEqual(typeof(DateTimeOffset), properties[0].PropertyType);
    }

    [TestMethod]
    public void CanOnlyBeProducedFromTheClock()
    {
        var factoryMethods = typeof(AgentHeartbeat)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(method => method.DeclaringType == typeof(AgentHeartbeat) && !method.IsSpecialName)
            .ToArray();

        Assert.AreEqual(
            1,
            factoryMethods.Length,
            "A heartbeat must have exactly one way to come into existence: reading the clock.");

        var parameters = factoryMethods[0].GetParameters();

        Assert.AreEqual(nameof(AgentHeartbeat.Now), factoryMethods[0].Name);
        Assert.AreEqual(1, parameters.Length);
        Assert.AreEqual(typeof(TimeProvider), parameters[0].ParameterType);
    }

    [TestMethod]
    public void HasNoPublicConstructorThatAcceptsAValue()
    {
        var constructors = typeof(AgentHeartbeat).GetConstructors(BindingFlags.Public | BindingFlags.Instance);

        Assert.AreEqual(
            0,
            constructors.Length,
            "A caller must not be able to construct a heartbeat directly from an arbitrary value.");
    }
}
