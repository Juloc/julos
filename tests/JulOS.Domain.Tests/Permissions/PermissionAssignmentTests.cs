using JulOS.Domain.Permissions;
using Microsoft.Extensions.Time.Testing;

namespace JulOS.Domain.Tests.Permissions;

/// <summary>Verifies the grant record a subject holds a permission through.</summary>
[TestClass]
public sealed class PermissionAssignmentTests
{
    [TestMethod]
    public void GrantRecordsTheSubjectPermissionScopeAndMoment()
    {
        var timeProvider = new FakeTimeProvider();
        var subject = new PermissionSubject(PermissionSubjectKind.User, new PermissionSubjectId(Guid.CreateVersion7()));
        var permission = PermissionName.Parse("packages.read");

        var assignment = PermissionAssignment.Grant(
            new PermissionAssignmentId(Guid.CreateVersion7()),
            subject,
            permission,
            PermissionScope.Global,
            timeProvider);

        Assert.AreEqual(subject, assignment.Subject);
        Assert.AreEqual(permission, assignment.Permission);
        Assert.AreEqual(PermissionScope.Global, assignment.Scope);
        Assert.AreEqual(timeProvider.GetUtcNow(), assignment.GrantedAtUtc);
    }

    [TestMethod]
    public void GrantingWithoutATimeProviderIsRejected()
    {
        var subject = new PermissionSubject(PermissionSubjectKind.User, new PermissionSubjectId(Guid.CreateVersion7()));

        Assert.ThrowsExactly<ArgumentNullException>(() => PermissionAssignment.Grant(
            new PermissionAssignmentId(Guid.CreateVersion7()),
            subject,
            PermissionName.Parse("packages.read"),
            PermissionScope.Global,
            null!));
    }
}
