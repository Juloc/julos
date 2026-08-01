using JulOS.Domain.Packages;

namespace JulOS.Domain.Tests.Packages;

/// <summary>Verifies the identity of one package installation record.</summary>
[TestClass]
public sealed class PackageInstallationIdTests
{
    [TestMethod]
    public void AGeneratedValueIsAccepted()
    {
        var value = Guid.CreateVersion7();

        Assert.AreEqual(value, new PackageInstallationId(value).Value);
    }

    [TestMethod]
    public void AnEmptyValueIsRejectedBecauseItIdentifiesNothing()
    {
        var exception = Assert.ThrowsExactly<ArgumentException>(() => new PackageInstallationId(Guid.Empty));

        Assert.AreEqual("Value", exception.ParamName, "The failure must name the rejected expression.");
    }
}
