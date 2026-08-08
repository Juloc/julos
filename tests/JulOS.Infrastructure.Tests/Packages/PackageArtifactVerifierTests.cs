using System.Security.Cryptography;
using System.Text;

using JulOS.Infrastructure.Packages;

namespace JulOS.Infrastructure.Tests.Packages;

[TestClass]
public sealed class PackageArtifactVerifierTests
{
    [TestMethod]
    public void TrustedSignedArtifactIsAccepted()
    {
        using var signingKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var artifact = Encoding.UTF8.GetBytes("package archive bytes");
        var signature = Sign(signingKey, artifact);
        var digest = Convert.ToHexString(SHA256.HashData(artifact)).ToLowerInvariant();
        var verifier = CreateVerifier(signingKey);

        var verified = verifier.Verify(artifact, signature, digest, "Juloc", "release-2026");

        Assert.AreEqual("Juloc", verified.Publisher);
        Assert.AreEqual("release-2026", verified.KeyId);
        Assert.AreEqual(digest, verified.DigestSha256);
        Assert.AreEqual(artifact.Length, verified.ArtifactLength);
    }

    [TestMethod]
    public void ModifiedArtifactIsRejectedBeforeSignatureTrust()
    {
        using var signingKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var original = Encoding.UTF8.GetBytes("original");
        var modified = Encoding.UTF8.GetBytes("modified");
        var signature = Sign(signingKey, original);
        var originalDigest = Convert.ToHexString(SHA256.HashData(original)).ToLowerInvariant();
        var verifier = CreateVerifier(signingKey);

        var failure = Assert.ThrowsExactly<PackageArtifactVerificationException>(() =>
            verifier.Verify(modified, signature, originalDigest, "Juloc", "release-2026"));

        Assert.AreEqual("package.digest.mismatch", failure.Code);
    }

    [TestMethod]
    public void UntrustedPublisherCannotInstall()
    {
        using var signingKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var artifact = Encoding.UTF8.GetBytes("artifact");
        var signature = Sign(signingKey, artifact);
        var digest = Convert.ToHexString(SHA256.HashData(artifact)).ToLowerInvariant();
        var verifier = new PackageArtifactVerifier([]);

        var failure = Assert.ThrowsExactly<PackageArtifactVerificationException>(() =>
            verifier.Verify(artifact, signature, digest, "Unknown", "unknown-key"));

        Assert.AreEqual("package.publisher.untrusted", failure.Code);
    }

    [TestMethod]
    public void SignatureFromDifferentKeyIsRejected()
    {
        using var trustedKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var attackerKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var artifact = Encoding.UTF8.GetBytes("artifact");
        var signature = Sign(attackerKey, artifact);
        var digest = Convert.ToHexString(SHA256.HashData(artifact)).ToLowerInvariant();
        var verifier = CreateVerifier(trustedKey);

        var failure = Assert.ThrowsExactly<PackageArtifactVerificationException>(() =>
            verifier.Verify(artifact, signature, digest, "Juloc", "release-2026"));

        Assert.AreEqual("package.signature.invalid", failure.Code);
    }

    private static byte[] Sign(ECDsa signingKey, byte[] artifact) => signingKey.SignData(
        artifact,
        HashAlgorithmName.SHA256,
        DSASignatureFormat.IeeeP1363FixedFieldConcatenation);

    private static PackageArtifactVerifier CreateVerifier(ECDsa signingKey) =>
        new([
            new TrustedPackagePublisher(
                "Juloc",
                "release-2026",
                signingKey.ExportSubjectPublicKeyInfoPem()),
        ]);
}
