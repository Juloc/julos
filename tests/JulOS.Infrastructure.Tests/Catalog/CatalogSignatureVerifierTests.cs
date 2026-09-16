using System.Security.Cryptography;

using JulOS.Domain.Catalog;
using JulOS.Infrastructure.Catalog;

namespace JulOS.Infrastructure.Tests.Catalog;

/// <summary>
/// CAT-002 trust evaluation: the exact outcomes <c>docs/APPLICATION_CATALOG.md</c> section 6
/// requires for trusted, unknown, unsigned, invalid and denied content.
/// </summary>
[TestClass]
public sealed class CatalogSignatureVerifierTests
{
    private const string Publisher = "juloc-official";
    private const string KeyIdentity = "official-2026-01";
    private static readonly DateTimeOffset SignedAt = DateTimeOffset.Parse("2026-08-25T12:00:00Z", null);
    private static readonly DateTimeOffset ValidFrom = DateTimeOffset.Parse("2026-01-01T00:00:00Z", null);

    [TestMethod]
    public void ADefinitionWithNoEnvelopeIsUnsignedAndStillInstallable()
    {
        var evaluation = CatalogSignatureVerifier.Evaluate(null, Digest('a'), [], officialSource: false);

        Assert.AreEqual(CatalogSignatureState.NotSigned, evaluation.State);
        Assert.AreEqual(
            CatalogTrustPolicy.Allow,
            evaluation.Policy,
            "Nothing claimed authenticity, so nothing failed to prove it.");
    }

    [TestMethod]
    public void AnAdministratorTrustedKeyProducesTrustedSigned()
    {
        using var signer = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var digest = Digest('a');
        var key = KeyFor(signer, AdministratorTrustState.Trusted);

        var evaluation = CatalogSignatureVerifier.Evaluate(
            Envelope(signer, digest, key), digest, [key], officialSource: false);

        Assert.AreEqual(CatalogSignatureState.TrustedSigned, evaluation.State);
        Assert.AreEqual(CatalogTrustPolicy.Allow, evaluation.Policy);
    }

    [TestMethod]
    public void AValidSignatureFromAnUndecidedKeyIsUnknownSigned()
    {
        using var signer = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var digest = Digest('a');
        var key = KeyFor(signer, AdministratorTrustState.Unknown);

        var evaluation = CatalogSignatureVerifier.Evaluate(
            Envelope(signer, digest, key), digest, [key], officialSource: false);

        Assert.AreEqual(CatalogSignatureState.UnknownSigned, evaluation.State);
        Assert.AreEqual(
            CatalogTrustPolicy.Allow,
            evaluation.Policy,
            "Unknown is a warning an administrator may continue past.");
    }

    [TestMethod]
    public void AnOfficialPinnedKeyIsTrustedWithoutAnAdministratorDecision()
    {
        using var signer = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var digest = Digest('a');
        var key = KeyFor(signer, AdministratorTrustState.Unknown) with { OfficialPinned = true };

        var evaluation = CatalogSignatureVerifier.Evaluate(
            Envelope(signer, digest, key), digest, [key], officialSource: true);

        Assert.AreEqual(CatalogSignatureState.TrustedSigned, evaluation.State);
    }

    [TestMethod]
    public void AnEnvelopeForOtherBytesIsInvalidRatherThanUnsigned()
    {
        using var signer = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var key = KeyFor(signer, AdministratorTrustState.Trusted);
        var envelope = Envelope(signer, Digest('a'), key);

        var evaluation = CatalogSignatureVerifier.Evaluate(envelope, Digest('b'), [key], officialSource: false);

        Assert.AreEqual(CatalogSignatureState.InvalidSignature, evaluation.State);
        Assert.AreEqual(CatalogTrustPolicy.Deny, evaluation.Policy);
    }

    [TestMethod]
    public void ASignatureMadeByAnotherKeyDoesNotVerify()
    {
        using var signer = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var impostor = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var digest = Digest('a');
        var published = KeyFor(signer, AdministratorTrustState.Trusted);
        var envelope = Envelope(impostor, digest, published);

        var evaluation = CatalogSignatureVerifier.Evaluate(envelope, digest, [published], officialSource: false);

        Assert.AreEqual(CatalogSignatureState.InvalidSignature, evaluation.State);
    }

    [TestMethod]
    public void AFingerprintThatDoesNotMatchTheKeyIsInvalid()
    {
        using var signer = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var digest = Digest('a');
        var key = KeyFor(signer, AdministratorTrustState.Trusted);
        var envelope = Envelope(signer, digest, key) with
        {
            PublicKeyFingerprint = $"sha256:{new string('0', 64)}",
        };

        var evaluation = CatalogSignatureVerifier.Evaluate(envelope, digest, [key], officialSource: false);

        Assert.AreEqual(CatalogSignatureState.InvalidSignature, evaluation.State);
    }

    [TestMethod]
    public void AnAbsentKeyIsInvalidRatherThanUnsigned()
    {
        using var signer = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var digest = Digest('a');
        var envelope = Envelope(signer, digest, KeyFor(signer, AdministratorTrustState.Unknown));

        var evaluation = CatalogSignatureVerifier.Evaluate(envelope, digest, [], officialSource: false);

        Assert.AreEqual(
            CatalogSignatureState.InvalidSignature,
            evaluation.State,
            "Absence of a usable key is never treated as a verified signature.");
    }

    [TestMethod]
    public void AnUnsupportedAlgorithmOrSchemaIsInvalid()
    {
        using var signer = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var digest = Digest('a');
        var key = KeyFor(signer, AdministratorTrustState.Trusted);
        var envelope = Envelope(signer, digest, key);

        Assert.AreEqual(
            CatalogSignatureState.InvalidSignature,
            CatalogSignatureVerifier.Evaluate(
                envelope with { Algorithm = "rsa-pss-sha256" }, digest, [key], false).State);
        Assert.AreEqual(
            CatalogSignatureState.InvalidSignature,
            CatalogSignatureVerifier.Evaluate(
                envelope with { SchemaVersion = 2 }, digest, [key], false).State);
    }

    [TestMethod]
    public void ASignatureDatedBeforeItsKeyExistedIsInvalid()
    {
        using var signer = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var digest = Digest('a');
        var key = KeyFor(signer, AdministratorTrustState.Trusted) with
        {
            ValidFromUtc = SignedAt.AddDays(1),
        };

        var evaluation = CatalogSignatureVerifier.Evaluate(
            Envelope(signer, digest, key), digest, [key], officialSource: false);

        Assert.AreEqual(
            CatalogSignatureState.InvalidSignature,
            evaluation.State,
            "A signature the key could not have made is not merely early.");
    }

    [TestMethod]
    public void AnExpiredKeyWarnsForCustomContentAndDeniesForTheOfficialSource()
    {
        using var signer = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var digest = Digest('a');
        var key = KeyFor(signer, AdministratorTrustState.Trusted) with
        {
            ValidUntilUtc = SignedAt.AddDays(-1),
        };
        var envelope = Envelope(signer, digest, key);

        var custom = CatalogSignatureVerifier.Evaluate(envelope, digest, [key], officialSource: false);
        var official = CatalogSignatureVerifier.Evaluate(envelope, digest, [key], officialSource: true);

        Assert.IsTrue(custom.Expired);
        Assert.AreEqual(CatalogTrustPolicy.Allow, custom.Policy);
        Assert.AreEqual(CatalogTrustPolicy.Deny, official.Policy);
    }

    [TestMethod]
    public void RevocationAndDistrustDenyWithoutRewritingWhatTheSignatureWas()
    {
        using var signer = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var digest = Digest('a');
        var trusted = KeyFor(signer, AdministratorTrustState.Trusted);
        var envelope = Envelope(signer, digest, trusted);

        var revoked = CatalogSignatureVerifier.Evaluate(
            envelope, digest, [trusted with { RevokedAtUtc = SignedAt.AddDays(1) }], officialSource: false);
        var distrusted = CatalogSignatureVerifier.Evaluate(
            envelope,
            digest,
            [trusted with { AdministratorTrust = AdministratorTrustState.Distrusted }],
            officialSource: false);

        Assert.AreEqual(CatalogTrustPolicy.Deny, revoked.Policy);
        Assert.AreEqual(
            CatalogSignatureState.TrustedSigned,
            revoked.State,
            "History stays explainable: the signature was what it was, only the policy changed.");
        Assert.AreEqual(CatalogTrustPolicy.Deny, distrusted.Policy);
        Assert.AreEqual(CatalogSignatureState.UnknownSigned, distrusted.State);
    }

    [TestMethod]
    public void TrustCannotRescueContentThatDoesNotVerify()
    {
        using var signer = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var impostor = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var digest = Digest('a');
        var key = KeyFor(signer, AdministratorTrustState.Trusted) with { OfficialPinned = true };

        var evaluation = CatalogSignatureVerifier.Evaluate(
            Envelope(impostor, digest, key), digest, [key], officialSource: true);

        Assert.AreEqual(
            CatalogSignatureState.InvalidSignature,
            evaluation.State,
            "An administrator decision is read only after the cryptography already succeeded.");
        Assert.AreEqual(CatalogTrustPolicy.Deny, evaluation.Policy);
    }

    private static string Digest(char fill) => new(fill, 64);

    private static CatalogVerificationKey KeyFor(ECDsa signer, AdministratorTrustState trust) => new(
        Publisher,
        KeyIdentity,
        CatalogSignatureInput.Algorithm,
        Convert.ToBase64String(signer.ExportSubjectPublicKeyInfo()),
        OfficialPinned: false,
        trust,
        ValidFrom,
        ValidUntilUtc: null,
        RevokedAtUtc: null);

    private static CatalogSignatureEnvelope Envelope(
        ECDsa signer,
        string definitionSha256,
        CatalogVerificationKey key) => new(
        CatalogSignatureInput.SchemaVersion,
        Publisher,
        KeyIdentity,
        CatalogSignatureVerifier.Fingerprint(key.PublicKeySpki),
        CatalogSignatureInput.Algorithm,
        definitionSha256,
        SignedAt,
        Convert.ToBase64String(signer.SignData(
            CatalogSignatureInput.For(definitionSha256),
            HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation)));
}
