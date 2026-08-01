using JulOS.Domain;
using JulOS.Domain.Applications;
using JulOS.Domain.Packages;
using JulOS.Domain.Primitives;

namespace JulOS.Domain.Tests.Applications;

/// <summary>Verifies the registered application record.</summary>
[TestClass]
public sealed class ApplicationDefinitionTests
{
    [TestMethod]
    public void RenamingDoesNotChangeIdentity()
    {
        var application = NewApplication();
        var id = application.Id;
        var stableKey = application.StableKey;

        application.RenameTo(LocalizationKey.Parse("app.example.name.v2"));

        Assert.AreEqual(id, application.Id);
        Assert.AreEqual(stableKey, application.StableKey, "A stored window refers to the stable key, not to the label.");
    }

    [TestMethod]
    public void TheRecordHoldsNoDisplayText()
    {
        var application = NewApplication();

        Assert.AreEqual("app.example.name", application.DisplayNameKey.Value);
        Assert.IsFalse(
            typeof(ApplicationDefinition).GetProperties().Any(property => property.Name == "DisplayName"),
            "Holding a name would fix one language into the record.");
    }

    [TestMethod]
    public void AnApplicationSupportingNoViewportIsRejected()
    {
        var exception = Assert.ThrowsExactly<DomainRuleViolationException>(() => ApplicationDefinition.Register(
            new ApplicationDefinitionId(Guid.CreateVersion7()),
            PackageId.Parse("de.juloc.julos.example"),
            ApplicationStableKey.Parse("example"),
            LocalizationKey.Parse("app.example.name"),
            ApplicationInstancePolicy.MultipleInstances,
            WindowSizeConstraints.Create(800, 600, 400, 300),
            []));

        Assert.AreEqual("application.viewport.none_supported", exception.Code);
    }

    [TestMethod]
    public void SupportedViewportsAreReportedExactly()
    {
        var application = NewApplication();

        Assert.IsTrue(application.SupportsViewport(ViewportClass.Desktop));
        Assert.IsFalse(application.SupportsViewport(ViewportClass.Mobile));
    }

    [TestMethod]
    public void DisablingKeepsTheRegistrationAndMovesTheRevision()
    {
        var application = NewApplication();
        var before = application.Revision;

        application.Disable();

        Assert.IsFalse(application.IsEnabled);
        Assert.IsTrue(application.Revision > before);

        application.Enable();

        Assert.IsTrue(application.IsEnabled);
    }

    private static ApplicationDefinition NewApplication() => ApplicationDefinition.Register(
        new ApplicationDefinitionId(Guid.CreateVersion7()),
        PackageId.Parse("de.juloc.julos.example"),
        ApplicationStableKey.Parse("example"),
        LocalizationKey.Parse("app.example.name"),
        ApplicationInstancePolicy.SingleInstancePerUser,
        WindowSizeConstraints.Create(800, 600, 400, 300),
        [ViewportClass.Desktop, ViewportClass.Tablet]);
}
