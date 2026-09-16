using System.Security.Cryptography;
using System.Text;

using JulOS.Domain.Packages;
using JulOS.Infrastructure.Packages;

namespace JulOS.Infrastructure.Tests.Packages;

/// <summary>
/// PKG-014: what verification reports when a signature is optional, and what it still refuses.
/// </summary>
[TestClass]
public sealed class PackageArtifactEvaluationTests
{
    private static readonly byte[] Artifact = Encoding.UTF8.GetBytes("artifact");

    [TestMethod]
    public void AnArtifactThatClaimsNothingIsUnsignedRatherThanRefused()
    {
        var verifier = new PackageArtifactVerifier([]);

        var evaluation = verifier.Evaluate(Artifact, [], Digest(Artifact), null, null, null);

        Assert.AreEqual(PackageSignatureState.NotSigned, evaluation.SignatureState);
        Assert.IsNull(evaluation.Publisher);
        Assert.IsNull(evaluation.PublicKeyFingerprint);
    }

    [TestMethod]
    public void ASignatureFromASuppliedKeyIsUnknownSignedAndNeverTrusted()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var verifier = new PackageArtifactVerifier([]);

        var evaluation = verifier.Evaluate(
            Artifact,
            Sign(key, Artifact),
            Digest(Artifact),
            "someone",
            "their-key",
            Spki(key));

        Assert.AreEqual(
            PackageSignatureState.UnknownSigned,
            evaluation.SignatureState,
            "An artifact describing its own key proves only that whoever wrote one wrote the other.");
        Assert.AreEqual(PackageArtifactVerifier.Fingerprint(key), evaluation.PublicKeyFingerprint);
    }

    [TestMethod]
    public void ASignatureFromAConfiguredKeyIsTrustedSigned()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var verifier = Configured(key);

        var evaluation = verifier.Evaluate(
            Artifact,
            Sign(key, Artifact),
            Digest(Artifact),
            "juloc",
            "release-2026",
            null);

        Assert.AreEqual(PackageSignatureState.TrustedSigned, evaluation.SignatureState);
    }

    [TestMethod]
    public void ClaimingATrustedIdentityWithOtherKeyMaterialIsRefused()
    {
        using var trusted = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var attacker = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var verifier = Configured(trusted);

        // Downgrading this to unknown-signed would let an attacker install under a name the
        // administrator recognises, with a key nobody configured.
        var failure = Assert.ThrowsExactly<PackageArtifactVerificationException>(
            () => verifier.Evaluate(
                Artifact,
                Sign(attacker, Artifact),
                Digest(Artifact),
                "juloc",
                "release-2026",
                Spki(attacker)));

        Assert.AreEqual("package.publisher.key_conflict", failure.Code);
    }

    [TestMethod]
    public void AClaimedSignatureWithNoUsableKeyIsInvalidRatherThanUnsigned()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var verifier = new PackageArtifactVerifier([]);

        var failure = Assert.ThrowsExactly<PackageArtifactVerificationException>(
            () => verifier.Evaluate(Artifact, Sign(key, Artifact), Digest(Artifact), "someone", "their-key", null));

        Assert.AreEqual("package.signature.key_unavailable", failure.Code);
    }

    [TestMethod]
    public void HalfAClaimIsStillAClaimThatCannotBeCompleted()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var verifier = new PackageArtifactVerifier([]);

        foreach (var (signature, publisher, keyId, spki) in new (byte[], string?, string?, string?)[]
        {
            ([], "someone", "their-key", Spki(key)),
            (Sign(key, Artifact), null, "their-key", Spki(key)),
            (Sign(key, Artifact), "someone", null, Spki(key)),
        })
        {
            var failure = Assert.ThrowsExactly<PackageArtifactVerificationException>(
                () => verifier.Evaluate(Artifact, signature, Digest(Artifact), publisher, keyId, spki));
            Assert.AreEqual("package.signature.invalid", failure.Code);
        }
    }

    [TestMethod]
    public void ADigestMismatchFailsWhateverTheSignatureSays()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var verifier = Configured(key);
        var other = Encoding.UTF8.GetBytes("other bytes");

        // A claim about who produced bytes means nothing until it is settled which bytes.
        var failure = Assert.ThrowsExactly<PackageArtifactVerificationException>(
            () => verifier.Evaluate(other, Sign(key, other), Digest(Artifact), "juloc", "release-2026", null));

        Assert.AreEqual("package.digest.mismatch", failure.Code);
    }

    [TestMethod]
    public void ASignatureThatDoesNotVerifyIsInvalid()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var attacker = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var verifier = new PackageArtifactVerifier([]);

        var failure = Assert.ThrowsExactly<PackageArtifactVerificationException>(
            () => verifier.Evaluate(
                Artifact,
                Sign(attacker, Artifact),
                Digest(Artifact),
                "someone",
                "their-key",
                Spki(key)));

        Assert.AreEqual("package.signature.invalid", failure.Code);
    }

    private static PackageArtifactVerifier Configured(ECDsa key) =>
        new([new TrustedPackagePublisher("juloc", "release-2026", key.ExportSubjectPublicKeyInfoPem())]);

    private static string Spki(ECDsa key) => Convert.ToBase64String(key.ExportSubjectPublicKeyInfo());

    private static string Digest(byte[] artifact) => Convert.ToHexStringLower(SHA256.HashData(artifact));

    private static byte[] Sign(ECDsa key, byte[] artifact) => key.SignData(
        artifact,
        HashAlgorithmName.SHA256,
        DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
}
