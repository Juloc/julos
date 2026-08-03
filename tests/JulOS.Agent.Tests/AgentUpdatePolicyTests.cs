using System.Security.Cryptography;

using JulOS.Agent;
using JulOS.Contracts.Agents;

namespace JulOS.Agent.Tests;

[TestClass]
public sealed class AgentUpdatePolicyTests
{
    [TestMethod]
    public void UpgradeRequiresMatchingDigestAndManualInstallation()
    {
        var artifact = "agent-binary"u8.ToArray();
        var digest = Convert.ToHexStringLower(SHA256.HashData(artifact));

        var decision = new AgentUpdatePolicy().Validate(
            "1.0.0",
            "1.1.0",
            allowExplicitDowngrade: false,
            artifact,
            digest);

        Assert.AreEqual(AgentUpdateContract.CurrentVersion, decision.ContractVersion);
        Assert.IsFalse(decision.IsDowngrade);
        Assert.AreEqual(digest, decision.ArtifactDigest);
        Assert.IsTrue(decision.RequiresManualInstallation);
        Assert.IsFalse(decision.AutomaticApplySupported);
    }

    [TestMethod]
    public void DowngradeCannotHappenSilently()
    {
        var artifact = "agent-binary"u8.ToArray();
        var digest = Convert.ToHexStringLower(SHA256.HashData(artifact));

        var failure = Assert.ThrowsExactly<AgentUpdateException>(() => new AgentUpdatePolicy().Validate(
            "2.0.0",
            "1.9.0",
            allowExplicitDowngrade: false,
            artifact,
            digest));

        Assert.AreEqual("agent.update.downgrade_requires_approval", failure.Code);
    }

    [TestMethod]
    public void ExplicitDowngradeStillRequiresManualInstallation()
    {
        var artifact = "agent-binary"u8.ToArray();
        var digest = Convert.ToHexStringLower(SHA256.HashData(artifact));

        var decision = new AgentUpdatePolicy().Validate(
            "2.0.0",
            "1.9.0",
            allowExplicitDowngrade: true,
            artifact,
            digest);

        Assert.IsTrue(decision.IsDowngrade);
        Assert.IsTrue(decision.RequiresManualInstallation);
        Assert.IsFalse(decision.AutomaticApplySupported);
    }

    [TestMethod]
    public void ModifiedArtifactIsRejected()
    {
        var artifact = "agent-binary"u8.ToArray();
        var otherDigest = Convert.ToHexStringLower(SHA256.HashData("different"u8));

        var failure = Assert.ThrowsExactly<AgentUpdateException>(() => new AgentUpdatePolicy().Validate(
            "1.0.0",
            "1.1.0",
            allowExplicitDowngrade: false,
            artifact,
            otherDigest));

        Assert.AreEqual("agent.update.digest_mismatch", failure.Code);
    }
}
