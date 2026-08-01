using JulOS.Domain;
using JulOS.Domain.Packages;
using JulOS.Domain.Primitives;
using Microsoft.Extensions.Time.Testing;

namespace JulOS.Domain.Tests.Packages;

/// <summary>Verifies the package installation lifecycle: states, transitions and fault metadata.</summary>
[TestClass]
public sealed class PackageInstallationTests
{
    [TestMethod]
    public void ANewInstallationStartsInstallingAtTheInitialRevision()
    {
        var installation = NewInstallation();

        Assert.AreEqual(PackageInstallationState.Installing, installation.State);
        Assert.AreEqual(Revision.Initial, installation.Revision);
        Assert.IsNull(installation.FaultCode);
    }

    [TestMethod]
    public void TheFullLifecyclePathFromInstallThroughRemovalSucceeds()
    {
        var installation = NewInstallation();
        var revision = installation.Revision;

        foreach (var next in new[]
        {
            PackageInstallationState.Installed,
            PackageInstallationState.Configuring,
            PackageInstallationState.Disabled,
            PackageInstallationState.Starting,
            PackageInstallationState.Enabled,
            PackageInstallationState.Stopping,
            PackageInstallationState.Disabled,
            PackageInstallationState.Removing,
        })
        {
            installation.TransitionTo(next);

            Assert.AreEqual(next, installation.State);
            Assert.IsTrue(installation.Revision > revision, "Every accepted transition must move the revision forward.");
            revision = installation.Revision;
        }
    }

    [TestMethod]
    public void AnInvalidTransitionFailsExplicitly()
    {
        var installation = NewInstallation();

        var exception = Assert.ThrowsExactly<DomainRuleViolationException>(
            () => installation.TransitionTo(PackageInstallationState.Enabled));

        Assert.AreEqual("package.transition.invalid", exception.Code);
        Assert.AreEqual(PackageInstallationState.Installing, installation.State, "A refused transition must not change the state.");
    }

    [TestMethod]
    public void TransitionToRefusesTheFaultedTargetRegardlessOfCurrentState()
    {
        var installation = NewInstallation();

        var exception = Assert.ThrowsExactly<DomainRuleViolationException>(
            () => installation.TransitionTo(PackageInstallationState.Faulted));

        Assert.AreEqual("package.transition.fault_requires_reason", exception.Code);
    }

    [TestMethod]
    public void FaultRecordsTheReasonAndTheMoment()
    {
        var installation = NewInstallation();
        installation.TransitionTo(PackageInstallationState.Installed);
        installation.TransitionTo(PackageInstallationState.Configuring);
        installation.TransitionTo(PackageInstallationState.Disabled);
        installation.TransitionTo(PackageInstallationState.Starting);
        installation.TransitionTo(PackageInstallationState.Enabled);

        var timeProvider = new FakeTimeProvider();
        var revisionBeforeFault = installation.Revision;

        installation.Fault("package.worker.crashed", "The worker process exited unexpectedly.", timeProvider);

        Assert.AreEqual(PackageInstallationState.Faulted, installation.State);
        Assert.AreEqual("package.worker.crashed", installation.FaultCode);
        Assert.AreEqual("The worker process exited unexpectedly.", installation.FaultDetail);
        Assert.AreEqual(timeProvider.GetUtcNow(), installation.FaultedAtUtc);
        Assert.IsTrue(installation.Revision > revisionBeforeFault);
    }

    [TestMethod]
    public void FaultFromARestStateWithNoActiveWorkerFailsExplicitly()
    {
        var installation = NewInstallation();
        installation.TransitionTo(PackageInstallationState.Installed);

        var exception = Assert.ThrowsExactly<DomainRuleViolationException>(
            () => installation.Fault("package.worker.crashed", "unused", new FakeTimeProvider()));

        Assert.AreEqual("package.transition.invalid", exception.Code);
        Assert.AreEqual(PackageInstallationState.Installed, installation.State);
    }

    [TestMethod]
    public void ABlankFaultCodeIsRejected()
    {
        var installation = NewInstallation();
        installation.TransitionTo(PackageInstallationState.Installed);
        installation.TransitionTo(PackageInstallationState.Configuring);
        installation.TransitionTo(PackageInstallationState.Disabled);
        installation.TransitionTo(PackageInstallationState.Starting);
        installation.TransitionTo(PackageInstallationState.Enabled);

        Assert.ThrowsExactly<ArgumentException>(
            () => installation.Fault("   ", "detail", new FakeTimeProvider()));
    }

    [TestMethod]
    public void LeavingTheFaultedStateClearsTheFaultMetadata()
    {
        var installation = NewInstallation();
        installation.TransitionTo(PackageInstallationState.Installed);
        installation.TransitionTo(PackageInstallationState.Configuring);
        installation.Fault("package.dependency.unmet", "A required capability provider is missing.", new FakeTimeProvider());

        installation.TransitionTo(PackageInstallationState.Updating);

        Assert.AreEqual(PackageInstallationState.Updating, installation.State);
        Assert.IsNull(installation.FaultCode);
        Assert.IsNull(installation.FaultDetail);
        Assert.IsNull(installation.FaultedAtUtc);
    }

    [TestMethod]
    public void AFaultedInstallationMayBeRemoved()
    {
        var installation = NewInstallation();
        installation.TransitionTo(PackageInstallationState.Installed);
        installation.TransitionTo(PackageInstallationState.Configuring);
        installation.Fault("package.configuration.invalid", "A required setting failed validation.", new FakeTimeProvider());

        installation.TransitionTo(PackageInstallationState.Removing);

        Assert.AreEqual(PackageInstallationState.Removing, installation.State);
    }

    [TestMethod]
    public void RemovingHasNoSuccessfulOutcomeBesidesLeavingTheAggregate()
    {
        var installation = NewInstallation();
        installation.TransitionTo(PackageInstallationState.Installed);
        installation.TransitionTo(PackageInstallationState.Removing);

        var exception = Assert.ThrowsExactly<DomainRuleViolationException>(
            () => installation.TransitionTo(PackageInstallationState.Installed));

        Assert.AreEqual("package.transition.invalid", exception.Code);
    }

    [TestMethod]
    public void AnEnabledPackageMayFaultOnAWorkerCrash()
    {
        var installation = NewInstallation();
        installation.TransitionTo(PackageInstallationState.Installed);
        installation.TransitionTo(PackageInstallationState.Configuring);
        installation.TransitionTo(PackageInstallationState.Disabled);
        installation.TransitionTo(PackageInstallationState.Starting);
        installation.TransitionTo(PackageInstallationState.Enabled);

        installation.Fault("package.worker.crashed", "The worker process exited unexpectedly.", new FakeTimeProvider());

        Assert.AreEqual(PackageInstallationState.Faulted, installation.State);
    }

    [TestMethod]
    public void AnInstalledPackageMayEnterUpdatingWithoutConfiguringFirst()
    {
        var installation = NewInstallation();
        installation.TransitionTo(PackageInstallationState.Installed);

        installation.TransitionTo(PackageInstallationState.Updating);

        Assert.AreEqual(PackageInstallationState.Updating, installation.State);
    }

    [TestMethod]
    public void AnUpdateReturnsToInstalledOnSuccess()
    {
        var installation = NewInstallation();
        installation.TransitionTo(PackageInstallationState.Installed);
        installation.TransitionTo(PackageInstallationState.Configuring);
        installation.TransitionTo(PackageInstallationState.Disabled);
        installation.TransitionTo(PackageInstallationState.Starting);
        installation.TransitionTo(PackageInstallationState.Enabled);
        installation.TransitionTo(PackageInstallationState.Updating);

        installation.TransitionTo(PackageInstallationState.Installed);

        Assert.AreEqual(PackageInstallationState.Installed, installation.State);
    }

    [TestMethod]
    public void ThePackageIdentityIsFixedForTheLifeOfTheRecord()
    {
        var packageId = PackageId.Parse("de.juloc.julos.example");
        var installation = PackageInstallation.BeginInstallation(
            new PackageInstallationId(Guid.CreateVersion7()),
            packageId);

        installation.TransitionTo(PackageInstallationState.Installed);
        installation.TransitionTo(PackageInstallationState.Updating);
        installation.TransitionTo(PackageInstallationState.Installed);

        Assert.AreEqual(packageId, installation.PackageId, "An update must not change which package this record describes.");
    }

    private static PackageInstallation NewInstallation() =>
        PackageInstallation.BeginInstallation(
            new PackageInstallationId(Guid.CreateVersion7()),
            PackageId.Parse("de.juloc.julos.example"));
}
