using JulOS.Domain;
using JulOS.Domain.Catalog;

namespace JulOS.Domain.Tests.Catalog;

/// <summary>CAT-002: a key identity binds immutably to one public key.</summary>
[TestClass]
public sealed class CatalogPublisherKeyTests
{
    private static readonly Guid SourceId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Administrator = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly DateTimeOffset ValidFrom = DateTimeOffset.Parse("2026-01-01T00:00:00Z", null);
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-08-25T12:00:00Z", null);

    [TestMethod]
    public void AnObservedKeyStartsUndecided()
    {
        var key = Observe();

        Assert.AreEqual(AdministratorTrustState.Unknown, key.AdministratorTrustState);
        Assert.IsNull(key.AdministratorDecisionByUserId);
        Assert.IsNull(key.AdministratorDecisionAtUtc);
        Assert.AreEqual(4, key.FirstObservedSourceRevision);
        Assert.AreEqual(4, key.LastObservedSourceRevision);
    }

    [TestMethod]
    public void SeeingTheSameKeyAgainOnlyMovesTheObservedRevision()
    {
        var key = Observe();

        key.ObserveAgain("c3BraS1ieXRlcw==", Fingerprint, 9);

        Assert.AreEqual(4, key.FirstObservedSourceRevision);
        Assert.AreEqual(9, key.LastObservedSourceRevision);
    }

    [TestMethod]
    public void AReusedKeyIdentityWithDifferentBytesIsRefused()
    {
        var key = Observe();

        // Accepting this would let a source replace the key that vouches for everything it
        // has ever published, silently and in one step.
        var failure = Assert.ThrowsExactly<DomainRuleViolationException>(
            () => key.ObserveAgain("b3RoZXItYnl0ZXM=", Fingerprint, 9));

        Assert.AreEqual("catalog.publisher_key_conflict", failure.Code);
    }

    [TestMethod]
    public void ADecisionRecordsWhoMadeItAndClearingForgetsThem()
    {
        var key = Observe();

        key.Decide(AdministratorTrustState.Trusted, Administrator, Now);
        Assert.AreEqual(AdministratorTrustState.Trusted, key.AdministratorTrustState);
        Assert.AreEqual(Administrator, key.AdministratorDecisionByUserId);
        Assert.AreEqual(Now, key.AdministratorDecisionAtUtc);

        key.Decide(AdministratorTrustState.Unknown, null, Now);
        Assert.AreEqual(AdministratorTrustState.Unknown, key.AdministratorTrustState);
        Assert.IsNull(
            key.AdministratorDecisionByUserId,
            "An undecided key must not keep pointing at an administrator who no longer stands behind it.");
        Assert.IsNull(key.AdministratorDecisionAtUtc);
    }

    [TestMethod]
    public void ADecisionWithoutItsAuthorIsRefused()
    {
        var key = Observe();

        var failure = Assert.ThrowsExactly<DomainRuleViolationException>(
            () => key.Decide(AdministratorTrustState.Trusted, null, Now));

        Assert.AreEqual("catalog.publisher_key_invalid", failure.Code);
    }

    [TestMethod]
    public void EveryDecisionAdvancesTheRevision()
    {
        var key = Observe();
        var before = key.Revision.Value;

        key.Decide(AdministratorTrustState.Distrusted, Administrator, Now);

        Assert.AreEqual(before + 1, key.Revision.Value);
    }

    [TestMethod]
    public void RevocationIsRecordedOnceAndDoesNotChangeTheDecision()
    {
        var key = Observe();
        key.Decide(AdministratorTrustState.Trusted, Administrator, Now);
        var afterDecision = key.Revision.Value;

        key.Revoke(Now);
        key.Revoke(Now.AddDays(1));

        Assert.AreEqual(Now, key.RevokedAtUtc);
        Assert.AreEqual(afterDecision + 1, key.Revision.Value, "Revoking twice is not two revocations.");
        Assert.AreEqual(
            AdministratorTrustState.Trusted,
            key.AdministratorTrustState,
            "Revocation denies future installs; it does not rewrite what was decided.");
    }

    [TestMethod]
    public void AnUnsupportedAlgorithmOrMalformedFingerprintIsRefused()
    {
        var algorithm = Assert.ThrowsExactly<DomainRuleViolationException>(
            () => Observe(algorithm: "rsa-pss-sha256"));
        Assert.AreEqual("catalog.signature_algorithm_unsupported", algorithm.Code);

        var fingerprint = Assert.ThrowsExactly<DomainRuleViolationException>(
            () => Observe(fingerprint: "sha256:not-hex"));
        Assert.AreEqual("catalog.publisher_key_invalid", fingerprint.Code);

        var missingPrefix = Assert.ThrowsExactly<DomainRuleViolationException>(
            () => Observe(fingerprint: new string('a', 64)));
        Assert.AreEqual("catalog.publisher_key_invalid", missingPrefix.Code);
    }

    [TestMethod]
    public void AValidityIntervalThatEndsBeforeItBeginsIsRefused()
    {
        var failure = Assert.ThrowsExactly<DomainRuleViolationException>(
            () => Observe(validUntil: ValidFrom.AddDays(-1)));

        Assert.AreEqual("catalog.publisher_key_invalid", failure.Code);
    }

    private const string Fingerprint = "sha256:" +
        "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    private static CatalogPublisherKey Observe(
        string algorithm = CatalogSignatureInput.Algorithm,
        string fingerprint = Fingerprint,
        DateTimeOffset? validUntil = null) =>
        CatalogPublisherKey.Observe(
            new CatalogPublisherKeyId(Guid.CreateVersion7()),
            SourceId,
            "juloc-official",
            "official-2026-01",
            algorithm,
            "c3BraS1ieXRlcw==",
            fingerprint,
            ValidFrom,
            validUntil,
            sourceRevision: 4);
}
