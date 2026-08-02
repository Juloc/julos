using JulOS.Domain.Agents;
using JulOS.Domain.Packages;
using JulOS.Domain.Primitives;
using JulOS.Infrastructure.Persistence.Core;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace JulOS.Infrastructure.Tests.Persistence;

[TestClass]
public sealed class CoreModelTests
{
    [TestMethod]
    public void EveryMutableCoreRecordMapsRevisionAsConcurrencyToken()
    {
        using var context = CreateContext();

        var mutableTypes = new[]
        {
            typeof(PackageInstallationRow),
            typeof(ApplicationDefinitionRow),
            typeof(LaunchTargetRow),
            typeof(DesktopLayoutRow),
            typeof(DesktopWindowRow),
            typeof(WidgetPlacementRow),
            typeof(SessionReferenceRow),
            typeof(AgentRow),
            typeof(AgentCapabilityRow),
            typeof(ProblemRow),
        };

        foreach (var mutableType in mutableTypes)
        {
            var entity = context.Model.FindEntityType(mutableType);
            Assert.IsNotNull(entity, $"{mutableType.Name} is not mapped.");

            var revision = entity.FindProperty("Revision");
            Assert.IsNotNull(revision, $"{mutableType.Name} has no revision column.");
            Assert.IsTrue(revision.IsConcurrencyToken, $"{mutableType.Name}.Revision is not a concurrency token.");
        }
    }

    [TestMethod]
    public void DomainPackageInstallationMapsWithoutReimplementingLifecycleRules()
    {
        var installation = PackageInstallation.BeginInstallation(
            new PackageInstallationId(Guid.CreateVersion7()),
            PackageId.Parse("de.juloc.example"));

        var row = PackageInstallationRow.FromDomain(installation);

        Assert.AreEqual(installation.Id.Value, row.Id);
        Assert.AreEqual(installation.PackageId.Value, row.PackageId);
        Assert.AreEqual(PackageInstallationState.Installing, row.State);
        Assert.AreEqual(Revision.Initial.Value, row.Revision);
    }

    [TestMethod]
    public void ModelUsesOnlyTheCoreSchema()
    {
        using var context = CreateContext();

        var schemas = context.Model.GetEntityTypes()
            .Select(entity => entity.GetSchema())
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        CollectionAssert.AreEqual(new[] { CoreModelConfiguration.Schema }, schemas);
    }

    [TestMethod]
    public void AuditEventHasNoDatabaseGeneratedMutableColumn()
    {
        using var context = CreateContext();
        var audit = context.Model.FindEntityType(typeof(AuditEventRow));
        Assert.IsNotNull(audit);

        var generated = audit.GetProperties()
            .Where(property => property.ValueGenerated != ValueGenerated.Never)
            .Select(property => property.Name)
            .ToArray();

        Assert.AreEqual(0, generated.Length, $"Audit rows must be supplied completely and append-only: {string.Join(", ", generated)}.");
    }

    private static CoreDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<CoreDbContext>()
            .UseNpgsql("Host=127.0.0.1;Database=model_only;Username=model_only")
            .Options;

        return new CoreDbContext(options);
    }
}
