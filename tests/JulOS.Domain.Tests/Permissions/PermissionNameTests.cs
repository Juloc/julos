using JulOS.Domain.Permissions;

namespace JulOS.Domain.Tests.Permissions;

/// <summary>Verifies the validated permission string.</summary>
[TestClass]
public sealed class PermissionNameTests
{
    [TestMethod]
    public void ADottedPermissionIsAccepted()
    {
        Assert.AreEqual("packages.install", PermissionName.Parse("packages.install").Value);
    }

    [TestMethod]
    public void HyphensInsideASegmentAreAccepted()
    {
        Assert.AreEqual("remote.file-transfer", PermissionName.Parse("remote.file-transfer").Value);
    }

    [TestMethod]
    public void AValueWithoutTwoSegmentsIsRejected()
    {
        Assert.ThrowsExactly<DomainRuleViolationException>(() => PermissionName.Parse("packages"));
    }

    [TestMethod]
    public void AnEmptyOrBlankValueIsRejected()
    {
        Assert.ThrowsExactly<DomainRuleViolationException>(() => PermissionName.Parse(string.Empty));
        Assert.ThrowsExactly<DomainRuleViolationException>(() => PermissionName.Parse("packages..install"));
    }

    [TestMethod]
    public void UpperCaseIsRejectedSoOnePermissionHasOneSpelling()
    {
        Assert.ThrowsExactly<DomainRuleViolationException>(() => PermissionName.Parse("Packages.Install"));
    }

    [TestMethod]
    public void ASegmentBoundedByAHyphenIsRejected()
    {
        Assert.ThrowsExactly<DomainRuleViolationException>(() => PermissionName.Parse("-packages.install"));
        Assert.ThrowsExactly<DomainRuleViolationException>(() => PermissionName.Parse("packages-.install"));
    }

    [TestMethod]
    public void TheFailureCarriesAStableCode()
    {
        var exception = Assert.ThrowsExactly<DomainRuleViolationException>(() => PermissionName.Parse("no-dots"));

        Assert.AreEqual("permission.name.invalid", exception.Code);
    }

    [TestMethod]
    public void ARelatedReadAndControlPermissionAreDifferentValues()
    {
        // Structural separation: "packages.read" and "packages.control" share a prefix but
        // must never be equal, because equality is exactly what PermissionEvaluator relies on.
        var read = PermissionName.Parse("packages.read");
        var control = PermissionName.Parse("packages.control");

        Assert.AreNotEqual(read, control);
    }

    [TestMethod]
    public void TwoPermissionsWithTheSameValueAreEqual()
    {
        Assert.AreEqual(PermissionName.Parse("packages.read"), PermissionName.Parse("packages.read"));
    }
}
