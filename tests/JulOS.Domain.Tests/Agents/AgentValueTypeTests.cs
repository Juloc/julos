using JulOS.Domain;
using JulOS.Domain.Agents;

namespace JulOS.Domain.Tests.Agents;

/// <summary>Verifies the validated values the Agent model is built from.</summary>
[TestClass]
public sealed class AgentValueTypeTests
{
    [TestMethod]
    public void AMachineIdentityAcceptsAnOpaqueValue()
    {
        Assert.AreEqual("host-1", AgentMachineIdentity.Parse("host-1").Value);
    }

    [TestMethod]
    public void AMachineIdentityRejectsSurroundingWhitespace()
    {
        Assert.ThrowsExactly<DomainRuleViolationException>(() => AgentMachineIdentity.Parse(" host-1"));
        Assert.ThrowsExactly<DomainRuleViolationException>(() => AgentMachineIdentity.Parse("host-1 "));
    }

    [TestMethod]
    public void AMachineIdentityRejectsAControlCharacter()
    {
        // A line break would let a reported value forge a second entry in a log file.
        Assert.ThrowsExactly<DomainRuleViolationException>(() => AgentMachineIdentity.Parse("host\n1"));
    }

    [TestMethod]
    public void AMachineIdentityRejectsAnEmptyValue()
    {
        var exception = Assert.ThrowsExactly<DomainRuleViolationException>(() => AgentMachineIdentity.Parse(string.Empty));

        Assert.AreEqual("agent.machine_identity.invalid", exception.Code);
    }

    [TestMethod]
    public void ACapabilityNameAcceptsTheDeclaredShape()
    {
        Assert.AreEqual("system.metrics", CapabilityName.Parse("system.metrics").Value);
        Assert.AreEqual("system.free-space", CapabilityName.Parse("system.free-space").Value);
    }

    [TestMethod]
    public void ACapabilityNameRejectsWhatCannotBeAName()
    {
        foreach (var invalid in new[] { string.Empty, "single-segment", "Upper.Case", "has space.name", "trailing-.dot" })
        {
            Assert.ThrowsExactly<DomainRuleViolationException>(
                () => CapabilityName.Parse(invalid),
                $"'{invalid}' must not become a capability name.");
        }
    }

    [TestMethod]
    public void ACapabilityNameFailureCarriesAStableCode()
    {
        var exception = Assert.ThrowsExactly<DomainRuleViolationException>(() => CapabilityName.Parse("no-dots"));

        Assert.AreEqual("agent_capability.name.invalid", exception.Code);
    }

    [TestMethod]
    public void ACapabilityVersionStartsAtOne()
    {
        Assert.AreEqual(1, CapabilityVersion.Initial.Value);
    }

    [TestMethod]
    public void ACapabilityVersionBelowOneIsRejected()
    {
        var exception = Assert.ThrowsExactly<DomainRuleViolationException>(() => CapabilityVersion.From(0));

        Assert.AreEqual("agent_capability.version.invalid", exception.Code);
    }

    [TestMethod]
    public void CapabilityVersionsCompareByNumber()
    {
        Assert.IsTrue(CapabilityVersion.From(1).CompareTo(CapabilityVersion.From(2)) < 0);
    }

    [TestMethod]
    public void CapabilityMetadataAcceptsAnEmptyPayload()
    {
        Assert.AreEqual(string.Empty, CapabilityMetadata.Empty.Value);
        Assert.AreEqual(string.Empty, CapabilityMetadata.Parse(string.Empty).Value);
    }

    [TestMethod]
    public void CapabilityMetadataRejectsAnOversizedPayload()
    {
        var oversized = new string('a', 8193);

        var exception = Assert.ThrowsExactly<DomainRuleViolationException>(() => CapabilityMetadata.Parse(oversized));

        Assert.AreEqual("agent_capability.metadata.too_long", exception.Code);
    }

    [TestMethod]
    public void CapabilityMetadataRejectsAMissingValue()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() => CapabilityMetadata.Parse(null!));
    }
}
