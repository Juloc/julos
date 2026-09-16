using JulOS.Domain;
using JulOS.Domain.Catalog;

namespace JulOS.Domain.Tests.Catalog;

/// <summary>CAT-002: what a catalog source is, and what a failed refresh may not do to it.</summary>
[TestClass]
public sealed class CatalogSourceTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-08-25T12:00:00Z", null);

    [TestMethod]
    public void ANewSourceHasNeverBeenRefreshed()
    {
        var source = Add();

        Assert.AreEqual(CatalogRefreshState.Never, source.LastRefreshState);
        Assert.IsNull(source.LastSuccessfulDigest);
        Assert.IsTrue(source.IsActive);
    }

    [TestMethod]
    public void AFailedRefreshKeepsTheLastValidCatalogAndMarksItStale()
    {
        var source = Add();
        source.RecordRefreshSucceeded(7, "sha256:good", Now);

        source.RecordRefreshFailed("catalog.source_unreachable", Now.AddHours(1));

        Assert.AreEqual(CatalogRefreshState.Stale, source.LastRefreshState);
        Assert.AreEqual(
            7,
            source.LastSuccessfulRevision,
            "A failed refresh never replaces a valid cache with a partially parsed source.");
        Assert.AreEqual("sha256:good", source.LastSuccessfulDigest);
        Assert.AreEqual("catalog.source_unreachable", source.LastFailureCode);
    }

    [TestMethod]
    public void ASourceThatHasNeverSucceededIsNotStale()
    {
        var source = Add();

        source.RecordRefreshFailed("catalog.source_unreachable", Now);

        Assert.AreEqual(
            CatalogRefreshState.Never,
            source.LastRefreshState,
            "There is no previous catalog to call stale.");
    }

    [TestMethod]
    public void ASucceedingRefreshClearsTheFailure()
    {
        var source = Add();
        source.RecordRefreshFailed("catalog.source_unreachable", Now);

        source.RecordRefreshSucceeded(2, "sha256:good", Now.AddHours(1));

        Assert.AreEqual(CatalogRefreshState.Fresh, source.LastRefreshState);
        Assert.IsNull(source.LastFailureCode);
    }

    [TestMethod]
    public void RemovingASourceLeavesATombstoneRatherThanDeletingIt()
    {
        var source = Add();

        source.Remove(Now);

        Assert.AreEqual(Now, source.DeletedAtUtc);
        Assert.IsFalse(source.IsActive);
        Assert.IsFalse(source.Enabled);
    }

    [TestMethod]
    public void ARemovedSourceIsNotChangedFurther()
    {
        var source = Add();
        source.Remove(Now);
        var revision = source.Revision.Value;

        source.Remove(Now.AddDays(1));
        Assert.AreEqual(revision, source.Revision.Value, "Removing twice is not two removals.");

        var failure = Assert.ThrowsExactly<DomainRuleViolationException>(
            () => source.RecordRefreshSucceeded(1, "sha256:good", Now));
        Assert.AreEqual("catalog.source_removed", failure.Code);
    }

    [TestMethod]
    public void TheOfficialSourceCannotBeRemoved()
    {
        var official = CatalogSource.Add(
            new CatalogSourceId(Guid.CreateVersion7()),
            CatalogSourceKind.Official,
            "JulOS",
            "builtin",
            CatalogSourceTrustLevel.Official);

        var failure = Assert.ThrowsExactly<DomainRuleViolationException>(() => official.Remove(Now));

        Assert.AreEqual("catalog.source_invalid", failure.Code);
    }

    [TestMethod]
    public void OnlyTheBuiltInSourceMayBeOfficial()
    {
        // Otherwise an administrator could mint a second "official" source and inherit the
        // pinned key set that belongs to the real one.
        var minted = Assert.ThrowsExactly<DomainRuleViolationException>(
            () => CatalogSource.Add(
                new CatalogSourceId(Guid.CreateVersion7()),
                CatalogSourceKind.Https,
                "Impostor",
                "https://example.test/catalog",
                CatalogSourceTrustLevel.Official));
        Assert.AreEqual("catalog.source_invalid", minted.Code);

        var demoted = Assert.ThrowsExactly<DomainRuleViolationException>(
            () => CatalogSource.Add(
                new CatalogSourceId(Guid.CreateVersion7()),
                CatalogSourceKind.Official,
                "JulOS",
                "builtin",
                CatalogSourceTrustLevel.Custom));
        Assert.AreEqual("catalog.source_invalid", demoted.Code);
    }

    [TestMethod]
    public void AnUpdateCannotPromoteACustomSourceToOfficial()
    {
        var source = Add();

        var failure = Assert.ThrowsExactly<DomainRuleViolationException>(
            () => source.Update(
                "Renamed",
                "https://example.test/catalog",
                null,
                CatalogSourceTrustLevel.Official,
                enabled: true));

        Assert.AreEqual("catalog.source_invalid", failure.Code);
    }

    [TestMethod]
    public void AnUpdateChangesWhatAnAdministratorMayChange()
    {
        var source = Add();
        var secret = Guid.CreateVersion7();

        source.Update(
            "Renamed",
            "https://example.test/other",
            secret,
            CatalogSourceTrustLevel.AdministratorTrusted,
            enabled: false);

        Assert.AreEqual("Renamed", source.DisplayName);
        Assert.AreEqual("https://example.test/other", source.Location);
        Assert.AreEqual(secret, source.AuthenticationSecretReferenceId);
        Assert.AreEqual(CatalogSourceTrustLevel.AdministratorTrusted, source.TrustLevel);
        Assert.IsFalse(source.IsActive, "A disabled source takes no further part in refreshes.");
        Assert.AreEqual(
            CatalogSourceKind.Https,
            source.Kind,
            "The kind is not changeable: installed applications point at this identity.");
    }

    [TestMethod]
    public void ALocationCarryingAPasswordIsRefused()
    {
        // The location is stored, returned and audited. A password in it would be a secret
        // in a URL and in a log; private credentials belong in the secret reference.
        var failure = Assert.ThrowsExactly<DomainRuleViolationException>(
            () => CatalogSource.Add(
                new CatalogSourceId(Guid.CreateVersion7()),
                CatalogSourceKind.Https,
                "Example catalog",
                "https://reader:hunter2@example.test/catalog",
                CatalogSourceTrustLevel.Custom));

        Assert.AreEqual("catalog.source_invalid", failure.Code);

        // A user without a password names a user, not a credential.
        var ssh = CatalogSource.Add(
            new CatalogSourceId(Guid.CreateVersion7()),
            CatalogSourceKind.Git,
            "Example repository",
            "git@example.test:juloc/catalog.git",
            CatalogSourceTrustLevel.Custom);
        Assert.AreEqual("git@example.test:juloc/catalog.git", ssh.Location);
    }

    private static CatalogSource Add() => CatalogSource.Add(
        new CatalogSourceId(Guid.CreateVersion7()),
        CatalogSourceKind.Https,
        "Example catalog",
        "https://example.test/catalog",
        CatalogSourceTrustLevel.Custom);
}
