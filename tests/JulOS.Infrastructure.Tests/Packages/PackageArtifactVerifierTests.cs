using System.Security.Cryptography;
using System.Text;

using JulOS.Infrastructure.Packages;

namespace JulOS.Infrastructure.Tests.Packages;

[TestClass]
public sealed class PackageArtifactVerifierTests
{
    [TestMethod]
    public void TrustedSignedManifestIsAccepted()
    {
        using var signingKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var manifest = Encoding.UTF8.GetBytes("{\"schemaVersion\":1,\"id\":\"de.juloc.test\"}");
        var signature = signingKey.SignData(manifest, HashAlgorithmName.SHA256);
        var digest = Convert.ToHexString(SHA256.HashData(manifest)).ToLowerInvariant();
        var verifier = CreateVerifier(signingKey);

        var verified = verifier.Verify(manifest, signature, digest, "Juloc", "release-2026");

        Assert.AreEqual("Juloc", verified.Publisher);
        Assert.AreEqual("release-2026", verified.KeyId);
        Assert.AreEqual(digest, verified.DigestSha256);
        Assert.AreEqual(manifest.Length, verified.ManifestLength);
    }

    [TestMethod]
    public void ModifiedArtifactIsRejectedBeforeSignatureTrust()
    {
        using var signingKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var original = Encoding.UTF8.GetBytes("original");
        var modified = Encoding.UTF8.GetBytes("modified");
        var signature = signingKey.SignData(original, HashAlgorithmName.SHA256);
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
        var manifest = Encoding.UTF8.GetBytes("manifest");
        var signature = signingKey.SignData(manifest, HashAlgorithmName.SHA256);
        var digest = Convert.ToHexString(SHA256.HashData(manifest)).ToLowerInvariant();
        var verifier = new PackageArtifactVerifier([]);

        var failure = Assert.ThrowsExactly<PackageArtifactVerificationException>(() =>
            verifier.Verify(manifest, signature, digest, "Unknown", "unknown-key"));

        Assert.AreEqual("package.publisher.untrusted", failure.Code);
    }

    [TestMethod]
    public void SignatureFromDifferentKeyIsRejected()
    {
        using var trustedKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var attackerKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var manifest = Encoding.UTF8.GetBytes("manifest");
        var signature = attackerKey.SignData(manifest, HashAlgorithmName.SHA256);
        var digest = Convert.ToHexString(SHA256.HashData(manifest)).ToLowerInvariant();
        var verifier = CreateVerifier(trustedKey);

        var failure = Assert.ThrowsExactly<PackageArtifactVerificationException>(() =>
            verifier.Verify(manifest, signature, digest, "Juloc", "release-2026"));

        Assert.AreEqual("package.signature.invalid", failure.Code);
    }

    private static PackageArtifactVerifier CreateVerifier(ECDsa signingKey) =>
        new([
            new TrustedPackagePublisher(
                "Juloc",
                "release-2026",
                signingKey.ExportSubjectPublicKeyInfoPem()),
        ]);
}
