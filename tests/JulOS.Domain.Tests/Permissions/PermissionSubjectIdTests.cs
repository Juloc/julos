using JulOS.Domain.Permissions;

namespace JulOS.Domain.Tests.Permissions;

/// <summary>Verifies the identity a permission subject refers to.</summary>
[TestClass]
public sealed class PermissionSubjectIdTests
{
    [TestMethod]
    public void AGeneratedValueIsAccepted()
    {
        var value = Guid.CreateVersion7();

        Assert.AreEqual(value, new PermissionSubjectId(value).Value);
    }

    [TestMethod]
    public void AnEmptyValueIsRejectedBecauseItIdentifiesNothing()
    {
        var exception = Assert.ThrowsExactly<ArgumentException>(() => new PermissionSubjectId(Guid.Empty));

        Assert.AreEqual("Value", exception.ParamName, "The failure must name the rejected expression.");
    }
}
