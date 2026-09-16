using JulOS.Domain.Packages;

namespace JulOS.Domain.Tests.Packages;

/// <summary>PKG-013: which packages run their code on the isolated path.</summary>
[TestClass]
public sealed class PackageIsolationTests
{
    [TestMethod]
    public void OnlyATrustedSignatureAvoidsIsolation()
    {
        Assert.IsFalse(PackageIsolation.IsRequiredFor(PackageSignatureState.TrustedSigned));
        Assert.IsTrue(PackageIsolation.IsRequiredFor(PackageSignatureState.UnknownSigned));
        Assert.IsTrue(PackageIsolation.IsRequiredFor(PackageSignatureState.NotSigned));
    }

    [TestMethod]
    public void ASignatureStateThisBuildDoesNotKnowIsIsolated()
    {
        // The rule is written as "trusted is the exception", so a state added later is
        // isolated by default rather than silently inheriting full access. This asserts
        // that property directly, because the cost of getting it wrong is an untrusted
        // package running in the Shell realm.
        Assert.IsTrue(PackageIsolation.IsRequiredFor((PackageSignatureState)99));
    }

    [TestMethod]
    public void AnInstallationRecordsWhatItWasInstalledWith()
    {
        var trusted = PackageInstallation.BeginInstallation(
            new PackageInstallationId(Guid.CreateVersion7()),
            PackageId.Parse("de.juloc.julos.reference"));
        var unknown = PackageInstallation.BeginInstallation(
            new PackageInstallationId(Guid.CreateVersion7()),
            PackageId.Parse("de.juloc.example"),
            PackageSignatureState.UnknownSigned);

        Assert.AreEqual(PackageSignatureState.TrustedSigned, trusted.SignatureState);
        Assert.IsFalse(trusted.RequiresIsolation);
        Assert.AreEqual(PackageSignatureState.UnknownSigned, unknown.SignatureState);
        Assert.IsTrue(unknown.RequiresIsolation);
    }
}
