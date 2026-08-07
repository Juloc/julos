using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

using JulOS.Infrastructure.Packages;

namespace JulOS.Infrastructure.Tests.Packages;

[TestClass]
public sealed class PackageArchiveSignatureTests
{
    [TestMethod]
    public void VerifyRejectsWorkerChangeEvenWhenManifestIsUnchanged()
    {
        using var signingKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var verifier = new PackageArtifactVerifier([
            new TrustedPackagePublisher("juloc", "test-key", signingKey.ExportSubjectPublicKeyInfoPem()),
        ]);
        var original = CreatePackageArchive("original-worker");
        var modified = CreatePackageArchive("modified-worker");
        var signature = signingKey.SignData(
            original,
            HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        var modifiedDigest = Convert.ToHexStringLower(SHA256.HashData(modified));

        var exception = Assert.ThrowsExactly<PackageArtifactVerificationException>(() =>
            verifier.Verify(modified, signature, modifiedDigest, "juloc", "test-key"));

        Assert.AreEqual("package.signature.invalid", exception.Code);
    }

    private static byte[] CreatePackageArchive(string workerContent)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteEntry(archive, "manifest.json", "{\"schemaVersion\":1,\"packageId\":\"JulOS.Test\"}");
            WriteEntry(archive, "worker/JulOS.Test.Worker", workerContent);
        }
        return stream.ToArray();
    }

    private static void WriteEntry(ZipArchive archive, string path, string content)
    {
        var entry = archive.CreateEntry(path, CompressionLevel.NoCompression);
        using var writer = new StreamWriter(entry.Open(), Encoding.UTF8, leaveOpen: false);
        writer.Write(content);
    }
}
