using JulOS.Domain.Packages;

namespace JulOS.Domain.Tests.Packages;

/// <summary>PKG-014: what an administrator confirms, and what the confirmation is bound to.</summary>
[TestClass]
public sealed class PackageTrustAssessmentTests
{
    private const string Digest = "a1b2c3";

    [TestMethod]
    public void AnUnsignedPackageWarnsAboutBothWhatIsUnknownAndWhereItWillRun()
    {
        var assessment = Assess(PackageSignatureState.NotSigned, NoRights());

        CollectionAssert.AreEqual(
            new[] { PackageTrustWarnings.IsolatedRuntime, PackageTrustWarnings.NotSigned },
            assessment.Warnings.ToArray(),
            "Unsigned is what is unknown; isolated is what follows from it, and both are shown.");
        Assert.IsTrue(assessment.RequiresAcknowledgement);
    }

    [TestMethod]
    public void AValidSignatureFromAnUntrustedKeyIsItsOwnWarning()
    {
        var assessment = Assess(PackageSignatureState.UnknownSigned);

        CollectionAssert.Contains(assessment.Warnings.ToArray(), PackageTrustWarnings.UnknownPublisher);
        Assert.IsFalse(
            assessment.Warnings.Contains(PackageTrustWarnings.NotSigned),
            "Something did sign it; what is unknown is who.");
    }

    [TestMethod]
    public void ATrustedPackageThatAsksForNothingNeedsNoAcknowledgement()
    {
        var assessment = Assess(PackageSignatureState.TrustedSigned, NoRights());

        Assert.AreEqual(0, assessment.Warnings.Count);
        Assert.IsFalse(assessment.RequiresAcknowledgement);
        Assert.IsTrue(
            assessment.IsAcknowledgedBy(presented: null, "operation-1"),
            "A confirmation nobody needs must not become a step everybody clicks through.");
    }

    [TestMethod]
    public void ATrustedPackageThatAsksForRightsStillNeedsAcknowledgement()
    {
        // Signing a definition does not make privileged settings safe; runtime-right changes
        // are always shown.
        var assessment = Assess(PackageSignatureState.TrustedSigned);

        CollectionAssert.Contains(assessment.Warnings.ToArray(), PackageTrustWarnings.CriticalRights);
        Assert.IsTrue(assessment.RequiresAcknowledgement);
    }

    [TestMethod]
    public void TheAcknowledgementChangesWithEverythingItCovers()
    {
        var baseline = Assess(PackageSignatureState.NotSigned).Acknowledgement("operation-1");

        Assert.AreNotEqual(
            baseline,
            Assess(PackageSignatureState.NotSigned).Acknowledgement("operation-2"),
            "An approval obtained for one install must not be presentable for another.");
        Assert.AreNotEqual(
            baseline,
            Assess(PackageSignatureState.NotSigned, version: "1.0.1").Acknowledgement("operation-1"));
        Assert.AreNotEqual(
            baseline,
            Assess(PackageSignatureState.NotSigned, artifactDigest: "different").Acknowledgement("operation-1"));
        Assert.AreNotEqual(
            baseline,
            Assess(
                PackageSignatureState.NotSigned,
                new PackageCriticalRights(["core.package.manage"], "none", null, NetworkAccess: false))
                .Acknowledgement("operation-1"),
            "Approving one set of rights is not approving another.");
    }

    [TestMethod]
    public void TheSamePermissionsInAnotherOrderAreTheSameRights()
    {
        var one = new PackageCriticalRights(["b.read", "a.read"], "none", null, NetworkAccess: false);
        var other = new PackageCriticalRights(["a.read", "b.read"], "none", null, NetworkAccess: false);

        Assert.AreEqual(one.Digest(), other.Digest());
    }

    [TestMethod]
    public void AMissingOrForeignAcknowledgementIsRefused()
    {
        var assessment = Assess(PackageSignatureState.NotSigned);

        Assert.IsFalse(assessment.IsAcknowledgedBy(presented: null, "operation-1"));
        Assert.IsFalse(assessment.IsAcknowledgedBy(string.Empty, "operation-1"));
        Assert.IsFalse(
            assessment.IsAcknowledgedBy(assessment.Acknowledgement("operation-2"), "operation-1"),
            "The operation is part of the digest, so an approval cannot move between installs.");
        Assert.IsTrue(assessment.IsAcknowledgedBy(assessment.Acknowledgement("operation-1"), "operation-1"));
    }

    private static PackageCriticalRights NoRights() =>
        new([], "none", null, NetworkAccess: false);

    private static PackageTrustAssessment Assess(
        PackageSignatureState state,
        PackageCriticalRights? rights = null,
        string version = "1.0.0",
        string artifactDigest = Digest) =>
        PackageTrustAssessment.For(
            artifactDigest,
            "de.juloc.example",
            version,
            state,
            state == PackageSignatureState.NotSigned ? null : "juloc",
            state == PackageSignatureState.NotSigned ? null : "release-2026",
            state == PackageSignatureState.NotSigned ? null : "sha256:" + new string('a', 64),
            rights ?? new PackageCriticalRights(["core.system.version.read"], "none", null, NetworkAccess: false));
}
