using JulOS.Domain.Primitives;

namespace JulOS.Domain.Tests.Primitives;

/// <summary>Verifies the guard every core identifier type uses.</summary>
[TestClass]
public sealed class EntityIdentifierTests
{
    [TestMethod]
    public void AGeneratedValueIsAccepted()
    {
        var value = Guid.CreateVersion7();

        Assert.AreEqual(value, EntityIdentifier.Validated(value));
    }

    [TestMethod]
    public void AnEmptyValueIsRejectedBecauseItIdentifiesNothing()
    {
        var exception = Assert.ThrowsExactly<ArgumentException>(() => EntityIdentifier.Validated(Guid.Empty));

        Assert.AreEqual("Guid.Empty", exception.ParamName, "The failure must name the rejected expression.");
    }
}
