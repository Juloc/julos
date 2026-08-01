using JulOS.Domain;
using JulOS.Domain.Applications;

namespace JulOS.Domain.Tests.Applications;

/// <summary>Verifies the validated values the application model is built from.</summary>
[TestClass]
public sealed class ApplicationValueTypeTests
{
    [TestMethod]
    public void AStableKeyAcceptsTheDeclaredCharacterSet()
    {
        Assert.AreEqual("host-metrics.overview", ApplicationStableKey.Parse("host-metrics.overview").Value);
    }

    [TestMethod]
    public void AStableKeyRejectsWhatCannotBeAnIdentity()
    {
        foreach (var invalid in new[] { string.Empty, " leading", "Upper", "1starts-with-digit", "has space", "sym!bol" })
        {
            Assert.ThrowsExactly<DomainRuleViolationException>(
                () => ApplicationStableKey.Parse(invalid),
                $"'{invalid}' must not become a stable key.");
        }
    }

    [TestMethod]
    public void ALocalizationKeyRejectsWhitespace()
    {
        Assert.ThrowsExactly<DomainRuleViolationException>(() => LocalizationKey.Parse("app example name"));
    }

    [TestMethod]
    public void AnExternalIdentityRejectsSurroundingWhitespace()
    {
        Assert.ThrowsExactly<DomainRuleViolationException>(() => ExternalIdentity.Parse(" resource-1"));
        Assert.ThrowsExactly<DomainRuleViolationException>(() => ExternalIdentity.Parse("resource-1 "));
    }

    [TestMethod]
    public void AnExternalIdentityRejectsAControlCharacter()
    {
        // A line break would let an observed value forge a second entry in a log file.
        Assert.ThrowsExactly<DomainRuleViolationException>(() => ExternalIdentity.Parse("resource"));
        Assert.ThrowsExactly<DomainRuleViolationException>(() => ExternalIdentity.Parse("resource\nspoofed"));
    }

    [TestMethod]
    public void ADefaultWindowSizeBelowTheMinimumIsRejected()
    {
        var exception = Assert.ThrowsExactly<DomainRuleViolationException>(
            () => WindowSizeConstraints.Create(300, 600, 400, 300));

        Assert.AreEqual("application.window_size.default_below_minimum", exception.Code);
    }

    [TestMethod]
    public void AnUnusablySmallWindowIsRejected()
    {
        var exception = Assert.ThrowsExactly<DomainRuleViolationException>(
            () => WindowSizeConstraints.Create(800, 600, 10, 300));

        Assert.AreEqual("application.window_size.out_of_range", exception.Code);
    }

    [TestMethod]
    public void ValidWindowConstraintsAreKept()
    {
        var constraints = WindowSizeConstraints.Create(800, 600, 400, 300);

        Assert.AreEqual(800, constraints.DefaultWidth);
        Assert.AreEqual(600, constraints.DefaultHeight);
        Assert.AreEqual(400, constraints.MinimumWidth);
        Assert.AreEqual(300, constraints.MinimumHeight);
    }
}
