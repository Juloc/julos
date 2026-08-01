using JulOS.Domain.Packages;
using JulOS.Domain.Permissions;
using Microsoft.Extensions.Time.Testing;

namespace JulOS.Domain.Tests.Permissions;

/// <summary>Verifies the pure rule deciding whether a set of assignments grants a permission.</summary>
[TestClass]
public sealed class PermissionEvaluatorTests
{
    private static readonly PermissionName Read = PermissionName.Parse("packages.read");
    private static readonly PermissionName Control = PermissionName.Parse("packages.control");

    private readonly FakeTimeProvider timeProvider = new();

    [TestMethod]
    public void AnEmptyAssignmentSetGrantsNothing()
    {
        var subject = AUser();

        var granted = PermissionEvaluator.Grants([], subject, Read, PermissionScope.Global);

        Assert.IsFalse(granted, "Default deny: an empty assignment set must never grant a permission.");
    }

    [TestMethod]
    public void NullAssignmentsAreRejected()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() => PermissionEvaluator.Grants(
            null!,
            AUser(),
            Read,
            PermissionScope.Global));
    }

    [TestMethod]
    public void AMatchingGlobalAssignmentGrantsTheRequestedPermission()
    {
        var subject = AUser();
        var assignments = new[] { AGrant(subject, Read, PermissionScope.Global) };

        Assert.IsTrue(PermissionEvaluator.Grants(assignments, subject, Read, PermissionScope.Global));
    }

    [TestMethod]
    public void HoldingTheReadPermissionNeverGrantsTheMatchingControlPermission()
    {
        // The structural rule this work item exists to prove: a read grant, even on the
        // exact same scope as the requested control action, must never satisfy it.
        var subject = AUser();
        var assignments = new[] { AGrant(subject, Read, PermissionScope.Global) };

        var granted = PermissionEvaluator.Grants(assignments, subject, Control, PermissionScope.Global);

        Assert.IsFalse(granted, "Holding a read permission must never imply the matching control permission.");
    }

    [TestMethod]
    public void HoldingTheControlPermissionNeverGrantsTheReadPermission()
    {
        var subject = AUser();
        var assignments = new[] { AGrant(subject, Control, PermissionScope.Global) };

        Assert.IsFalse(PermissionEvaluator.Grants(assignments, subject, Read, PermissionScope.Global));
    }

    [TestMethod]
    public void AGlobalAssignmentGrantsAPackageScopedTarget()
    {
        var subject = AUser();
        var assignments = new[] { AGrant(subject, Read, PermissionScope.Global) };
        var target = PermissionScope.ForPackage(PackageId.Parse("de.juloc.julos.example"));

        Assert.IsTrue(PermissionEvaluator.Grants(assignments, subject, Read, target));
    }

    [TestMethod]
    public void AGlobalAssignmentGrantsAResourceScopedTarget()
    {
        var subject = AUser();
        var assignments = new[] { AGrant(subject, Read, PermissionScope.Global) };
        var target = PermissionScope.ForResource(PermissionResourceId.Parse("resource-1"));

        Assert.IsTrue(PermissionEvaluator.Grants(assignments, subject, Read, target));
    }

    [TestMethod]
    public void APackageScopedAssignmentGrantsOnlyThatPackage()
    {
        var subject = AUser();
        var packageId = PackageId.Parse("de.juloc.julos.example");
        var otherPackageId = PackageId.Parse("de.juloc.julos.other");
        var assignments = new[] { AGrant(subject, Read, PermissionScope.ForPackage(packageId)) };

        Assert.IsTrue(PermissionEvaluator.Grants(assignments, subject, Read, PermissionScope.ForPackage(packageId)));
        Assert.IsFalse(PermissionEvaluator.Grants(assignments, subject, Read, PermissionScope.ForPackage(otherPackageId)));
        Assert.IsFalse(
            PermissionEvaluator.Grants(assignments, subject, Read, PermissionScope.Global),
            "A package-scoped grant must not reach the whole installation.");
    }

    [TestMethod]
    public void AResourceScopedAssignmentGrantsOnlyThatResource()
    {
        var subject = AUser();
        var resourceId = PermissionResourceId.Parse("resource-1");
        var otherResourceId = PermissionResourceId.Parse("resource-2");
        var assignments = new[] { AGrant(subject, Control, PermissionScope.ForResource(resourceId)) };

        Assert.IsTrue(PermissionEvaluator.Grants(assignments, subject, Control, PermissionScope.ForResource(resourceId)));
        Assert.IsFalse(PermissionEvaluator.Grants(assignments, subject, Control, PermissionScope.ForResource(otherResourceId)));
        Assert.IsFalse(PermissionEvaluator.Grants(assignments, subject, Control, PermissionScope.Global));
    }

    [TestMethod]
    public void ADifferentSubjectIsNotGrantedAnotherSubjectsAssignment()
    {
        var owner = AUser();
        var other = AUser();
        var assignments = new[] { AGrant(owner, Read, PermissionScope.Global) };

        Assert.IsFalse(PermissionEvaluator.Grants(assignments, other, Read, PermissionScope.Global));
    }

    [TestMethod]
    public void ARoleAssignmentIsEvaluatedLikeAnyOtherSubject()
    {
        var role = new PermissionSubject(PermissionSubjectKind.Role, new PermissionSubjectId(Guid.CreateVersion7()));
        var assignments = new[] { AGrant(role, Read, PermissionScope.Global) };

        Assert.IsTrue(PermissionEvaluator.Grants(assignments, role, Read, PermissionScope.Global));
    }

    [TestMethod]
    public void OneMatchingAssignmentAmongManyIsEnoughToGrant()
    {
        var subject = AUser();
        var assignments = new[]
        {
            AGrant(AUser(), Read, PermissionScope.Global),
            AGrant(subject, Control, PermissionScope.Global),
            AGrant(subject, Read, PermissionScope.ForResource(PermissionResourceId.Parse("resource-1"))),
            AGrant(subject, Read, PermissionScope.Global),
        };

        Assert.IsTrue(PermissionEvaluator.Grants(assignments, subject, Read, PermissionScope.Global));
    }

    private static PermissionSubject AUser() =>
        new(PermissionSubjectKind.User, new PermissionSubjectId(Guid.CreateVersion7()));

    private PermissionAssignment AGrant(PermissionSubject subject, PermissionName permission, PermissionScope scope) =>
        PermissionAssignment.Grant(new PermissionAssignmentId(Guid.CreateVersion7()), subject, permission, scope, this.timeProvider);
}
