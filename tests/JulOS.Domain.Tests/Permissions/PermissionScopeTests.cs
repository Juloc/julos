using JulOS.Domain.Packages;
using JulOS.Domain.Permissions;

namespace JulOS.Domain.Tests.Permissions;

/// <summary>Verifies which target scopes a permission scope reaches.</summary>
[TestClass]
public sealed class PermissionScopeTests
{
    [TestMethod]
    public void GlobalPermitsGlobal()
    {
        Assert.IsTrue(PermissionScope.Global.Permits(PermissionScope.Global));
    }

    [TestMethod]
    public void GlobalPermitsAnyPackage()
    {
        var target = PermissionScope.ForPackage(PackageId.Parse("de.juloc.julos.example"));

        Assert.IsTrue(PermissionScope.Global.Permits(target));
    }

    [TestMethod]
    public void GlobalPermitsAnyResource()
    {
        var target = PermissionScope.ForResource(PermissionResourceId.Parse("resource-1"));

        Assert.IsTrue(PermissionScope.Global.Permits(target));
    }

    [TestMethod]
    public void APackageScopePermitsTheSamePackage()
    {
        var packageId = PackageId.Parse("de.juloc.julos.example");
        var scope = PermissionScope.ForPackage(packageId);

        Assert.IsTrue(scope.Permits(PermissionScope.ForPackage(packageId)));
    }

    [TestMethod]
    public void APackageScopeDoesNotPermitADifferentPackage()
    {
        var scope = PermissionScope.ForPackage(PackageId.Parse("de.juloc.julos.example"));
        var target = PermissionScope.ForPackage(PackageId.Parse("de.juloc.julos.other"));

        Assert.IsFalse(scope.Permits(target));
    }

    [TestMethod]
    public void APackageScopeDoesNotPermitTheWholeInstallation()
    {
        var scope = PermissionScope.ForPackage(PackageId.Parse("de.juloc.julos.example"));

        Assert.IsFalse(scope.Permits(PermissionScope.Global), "A narrow grant must never widen into a global one.");
    }

    [TestMethod]
    public void APackageScopeDoesNotPermitAResource()
    {
        var scope = PermissionScope.ForPackage(PackageId.Parse("de.juloc.julos.example"));
        var target = PermissionScope.ForResource(PermissionResourceId.Parse("resource-1"));

        Assert.IsFalse(scope.Permits(target));
    }

    [TestMethod]
    public void AResourceScopePermitsTheSameResource()
    {
        var resourceId = PermissionResourceId.Parse("resource-1");
        var scope = PermissionScope.ForResource(resourceId);

        Assert.IsTrue(scope.Permits(PermissionScope.ForResource(resourceId)));
    }

    [TestMethod]
    public void AResourceScopeDoesNotPermitADifferentResource()
    {
        var scope = PermissionScope.ForResource(PermissionResourceId.Parse("resource-1"));
        var target = PermissionScope.ForResource(PermissionResourceId.Parse("resource-2"));

        Assert.IsFalse(scope.Permits(target));
    }

    [TestMethod]
    public void AResourceScopeDoesNotPermitTheWholeInstallation()
    {
        var scope = PermissionScope.ForResource(PermissionResourceId.Parse("resource-1"));

        Assert.IsFalse(scope.Permits(PermissionScope.Global));
    }

    [TestMethod]
    public void AResourceScopeDoesNotPermitAPackage()
    {
        var scope = PermissionScope.ForResource(PermissionResourceId.Parse("resource-1"));
        var target = PermissionScope.ForPackage(PackageId.Parse("de.juloc.julos.example"));

        Assert.IsFalse(scope.Permits(target));
    }

    [TestMethod]
    public void GlobalHasNoScopeIdentity()
    {
        Assert.AreEqual(PermissionScopeKind.Global, PermissionScope.Global.Kind);
        Assert.IsNull(PermissionScope.Global.ScopeId);
    }
}
