using JulOS.Domain.Permissions;

namespace JulOS.Domain.Tests.Permissions;

/// <summary>Verifies the identity of one permission assignment record.</summary>
[TestClass]
public sealed class PermissionAssignmentIdTests
{
    [TestMethod]
    public void AGeneratedValueIsAccepted()
    {
        var value = Guid.CreateVersion7();

        Assert.AreEqual(value, new PermissionAssignmentId(value).Value);
    }

    [TestMethod]
    public void AnEmptyValueIsRejectedBecauseItIdentifiesNothing()
    {
        var exception = Assert.ThrowsExactly<ArgumentException>(() => new PermissionAssignmentId(Guid.Empty));

        Assert.AreEqual("Value", exception.ParamName, "The failure must name the rejected expression.");
    }
}
