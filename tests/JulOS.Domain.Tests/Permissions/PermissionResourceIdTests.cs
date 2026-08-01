using JulOS.Domain.Permissions;

namespace JulOS.Domain.Tests.Permissions;

/// <summary>Verifies the opaque identity a resource-scoped permission refers to.</summary>
[TestClass]
public sealed class PermissionResourceIdTests
{
    [TestMethod]
    public void AnOpaqueValueIsAccepted()
    {
        Assert.AreEqual("agent-1", PermissionResourceId.Parse("agent-1").Value);
    }

    [TestMethod]
    public void AnEmptyOrWhitespaceValueIsRejected()
    {
        Assert.ThrowsExactly<DomainRuleViolationException>(() => PermissionResourceId.Parse(string.Empty));
        Assert.ThrowsExactly<DomainRuleViolationException>(() => PermissionResourceId.Parse("   "));
    }

    [TestMethod]
    public void SurroundingWhitespaceIsRejected()
    {
        Assert.ThrowsExactly<DomainRuleViolationException>(() => PermissionResourceId.Parse(" resource-1"));
        Assert.ThrowsExactly<DomainRuleViolationException>(() => PermissionResourceId.Parse("resource-1 "));
    }

    [TestMethod]
    public void AControlCharacterIsRejected()
    {
        // A line break would let an observed value forge a second entry in a log file.
        Assert.ThrowsExactly<DomainRuleViolationException>(() => PermissionResourceId.Parse("resource\nspoofed"));
    }

    [TestMethod]
    public void TheFailureCarriesAStableCode()
    {
        var exception = Assert.ThrowsExactly<DomainRuleViolationException>(() => PermissionResourceId.Parse(string.Empty));

        Assert.AreEqual("permission.resource_id.invalid", exception.Code);
    }
}
