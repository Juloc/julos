using JulOS.Domain;
using JulOS.Domain.Packages;

namespace JulOS.Domain.Tests.Packages;

/// <summary>Verifies the published package identity.</summary>
[TestClass]
public sealed class PackageIdTests
{
    [TestMethod]
    public void AReverseDomainNameIsAccepted()
    {
        Assert.AreEqual("de.juloc.julos.example", PackageId.Parse("de.juloc.julos.example").Value);
    }

    [TestMethod]
    public void HyphensInsideASegmentAreAccepted()
    {
        Assert.AreEqual("de.juloc.host-metrics", PackageId.Parse("de.juloc.host-metrics").Value);
    }

    [TestMethod]
    public void AValueWithoutTwoSegmentsIsRejected()
    {
        Assert.ThrowsExactly<DomainRuleViolationException>(() => PackageId.Parse("example"));
    }

    [TestMethod]
    public void AnEmptyOrBlankValueIsRejected()
    {
        Assert.ThrowsExactly<DomainRuleViolationException>(() => PackageId.Parse(string.Empty));
        Assert.ThrowsExactly<DomainRuleViolationException>(() => PackageId.Parse("de..example"));
    }

    [TestMethod]
    public void UpperCaseIsRejectedSoOneIdentityHasOneSpelling()
    {
        Assert.ThrowsExactly<DomainRuleViolationException>(() => PackageId.Parse("de.Juloc.Example"));
    }

    [TestMethod]
    public void ASegmentBoundedByAHyphenIsRejected()
    {
        Assert.ThrowsExactly<DomainRuleViolationException>(() => PackageId.Parse("de.-juloc.example"));
        Assert.ThrowsExactly<DomainRuleViolationException>(() => PackageId.Parse("de.juloc-.example"));
    }

    [TestMethod]
    public void TheFailureCarriesAStableCode()
    {
        var exception = Assert.ThrowsExactly<DomainRuleViolationException>(() => PackageId.Parse("no-dots"));

        Assert.AreEqual("package.id.invalid", exception.Code);
    }
}
