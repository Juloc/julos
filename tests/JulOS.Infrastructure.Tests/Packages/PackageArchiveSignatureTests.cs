using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

using JulOS.Infrastructure.Packages;

namespace JulOS.Infrastructure.Tests.Packages;

public sealed class PackageArchiveSignatureTests
{
    [Fact]
    public void Verify_rejects_worker_change_even_when_manifest_is_unchanged()
    {
        using var signingKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var verifier = new PackageArtifactVerifier([
            new TrustedPackagePublisher("juloc", "test-key", signingKey.ExportSubjectPublicKeyInfoPem()),
        ]);
        var original = CreatePackageArchive("original-worker");
        var modified = CreatePackageArchive("modified-worker");
        var signature = signingKey.SignData(original, HashAlgorithmName.SHA256);
        var modifiedDigest = Convert.ToHexStringLower(SHA256.HashData(modified));

        var exception = Assert.Throws<PackageArtifactVerificationException>(() =>
            verifier.Verify(modified, signature, modifiedDigest, "juloc", "test-key"));

        Assert.Equal("package.signature.invalid", exception.Code);
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
