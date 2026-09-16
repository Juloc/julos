using System.Formats.Tar;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;

using JulOS.Application.Catalog;
using JulOS.Domain.Catalog;
using JulOS.Infrastructure.Catalog;

namespace JulOS.Infrastructure.Tests.Catalog;

/// <summary>
/// CAT-002: what the OCI adapter locks a refresh to, and what it refuses.
/// </summary>
/// <remarks>
/// A stub registry rather than a real one, because the behaviour under test is the protocol
/// this adapter speaks — the token handshake, the digest checks and the size bounds — and
/// each of those has to be observable without a network.
/// </remarks>
[TestClass]
public sealed class OciCatalogSourceReaderTests
{
    private const string Registry = "registry.example.test";
    private const string Repository = "juloc/catalog";

    [TestMethod]
    public async Task ATaggedReferenceResolvesToTheManifestDigestAndServesItsFiles()
    {
        var layer = TarLayer(("catalog.json", "{}"), ("apps/sample/1.0.0/app.json", "{\"appId\":\"sample\"}"));
        var registry = new StubRegistry(layer);
        var reader = new OciCatalogSourceReader(registry.CreateClient());

        await using var snapshot = await reader.OpenAsync(Location($"oci://{Registry}/{Repository}:v1"), null);

        Assert.AreEqual(
            registry.ManifestDigest,
            snapshot.NativeContentIdentity,
            "A manifest digest covers every layer, which is what locks a whole refresh to one state.");
        Assert.AreEqual("{}", Text(await snapshot.ReadAsync("catalog.json")));
        Assert.AreEqual(
            "{\"appId\":\"sample\"}",
            Text(await snapshot.ReadAsync("apps/sample/1.0.0/app.json")));
        Assert.IsNull(
            await snapshot.TryReadAsync("apps/sample/1.0.0/signature.json"),
            "A file the artifact does not contain is absent, not a failure.");
    }

    [TestMethod]
    public async Task AnAnonymousTokenHandshakeIsCompleted()
    {
        var registry = new StubRegistry(TarLayer(("catalog.json", "{}"))) { RequireToken = true };
        var reader = new OciCatalogSourceReader(registry.CreateClient());

        await using var snapshot = await reader.OpenAsync(Location($"oci://{Registry}/{Repository}:v1"), null);

        Assert.AreEqual("{}", Text(await snapshot.ReadAsync("catalog.json")));
        Assert.IsTrue(registry.TokenRequested, "A public repository is reached through the same handshake.");
    }

    [TestMethod]
    public async Task ALayerThatDoesNotMatchItsDeclaredDigestIsRefused()
    {
        var registry = new StubRegistry(TarLayer(("catalog.json", "{}"))) { CorruptLayer = true };
        var reader = new OciCatalogSourceReader(registry.CreateClient());

        var failure = await Assert.ThrowsExactlyAsync<CatalogRefreshException>(
            () => reader.OpenAsync(Location($"oci://{Registry}/{Repository}:v1"), null));

        Assert.AreEqual(CatalogRefreshException.IntegrityMismatch, failure.Code);
    }

    [TestMethod]
    public async Task AManifestThatDoesNotMatchARequestedDigestIsRefused()
    {
        var registry = new StubRegistry(TarLayer(("catalog.json", "{}")));
        var reader = new OciCatalogSourceReader(registry.CreateClient());
        var wrong = "sha256:" + new string('b', 64);

        var failure = await Assert.ThrowsExactlyAsync<CatalogRefreshException>(
            () => reader.OpenAsync(Location($"oci://{Registry}/{Repository}@{wrong}"), null));

        Assert.AreEqual(CatalogRefreshException.IntegrityMismatch, failure.Code);
    }

    [TestMethod]
    public async Task AMalformedReferenceIsRefused()
    {
        var registry = new StubRegistry(TarLayer(("catalog.json", "{}")));
        var reader = new OciCatalogSourceReader(registry.CreateClient());

        foreach (var location in new[] { "oci://registry.example.test", "registry.example.test/", "" })
        {
            var failure = await Assert.ThrowsExactlyAsync<CatalogRefreshException>(
                () => reader.OpenAsync(Location(location), null));
            Assert.AreEqual(CatalogRefreshException.SourceUnavailable, failure.Code);
        }
    }

    private static CatalogSourceLocation Location(string location) =>
        new(CatalogSourceKind.Oci, location, null);

    private static string Text(byte[] bytes) => Encoding.UTF8.GetString(bytes);

    /// <summary>Builds one gzipped tar layer holding the named files.</summary>
    private static byte[] TarLayer(params (string Path, string Content)[] files)
    {
        using var raw = new MemoryStream();
        using (var writer = new TarWriter(raw, leaveOpen: true))
        {
            foreach (var (path, content) in files)
            {
                var entry = new PaxTarEntry(TarEntryType.RegularFile, path)
                {
                    DataStream = new MemoryStream(Encoding.UTF8.GetBytes(content)),
                };
                writer.WriteEntry(entry);
            }
        }

        raw.Position = 0;
        using var compressed = new MemoryStream();
        using (var gzip = new GZipStream(compressed, CompressionMode.Compress, leaveOpen: true))
        {
            raw.CopyTo(gzip);
        }

        return compressed.ToArray();
    }

    private static string Digest(byte[] bytes) =>
        $"sha256:{Convert.ToHexStringLower(SHA256.HashData(bytes))}";

    /// <summary>A registry that answers exactly the requests this adapter makes.</summary>
    private sealed class StubRegistry
    {
        private readonly byte[] layer;
        private readonly byte[] manifest;

        internal StubRegistry(byte[] layer)
        {
            this.layer = layer;
            this.manifest = Encoding.UTF8.GetBytes(
                $$"""
                  {
                    "schemaVersion": 2,
                    "mediaType": "application/vnd.oci.image.manifest.v1+json",
                    "layers": [
                      {
                        "mediaType": "application/vnd.oci.image.layer.v1.tar+gzip",
                        "digest": "{{Digest(layer)}}",
                        "size": {{layer.Length}}
                      }
                    ]
                  }
                  """);
            this.ManifestDigest = Digest(this.manifest);
        }

        internal string ManifestDigest { get; }

        internal bool RequireToken { get; init; }

        internal bool CorruptLayer { get; init; }

        internal bool TokenRequested { get; private set; }

        internal HttpClient CreateClient() => new(new Handler(this));

        private HttpResponseMessage Answer(HttpRequestMessage request)
        {
            var path = request.RequestUri!.AbsolutePath;

            if (path == "/token")
            {
                this.TokenRequested = true;
                return Json("""{"token":"issued"}""");
            }

            if (this.RequireToken && request.Headers.Authorization is null)
            {
                var response = new HttpResponseMessage(HttpStatusCode.Unauthorized);
                response.Headers.TryAddWithoutValidation(
                    "WWW-Authenticate",
                    $"Bearer realm=\"https://{Registry}/token\",service=\"{Registry}\",scope=\"repository:{Repository}:pull\"");
                return response;
            }

            if (path.Contains("/manifests/", StringComparison.Ordinal))
            {
                return Bytes(this.manifest);
            }

            if (path.Contains("/blobs/", StringComparison.Ordinal))
            {
                return Bytes(this.CorruptLayer ? [.. this.layer, 0x00] : this.layer);
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        private static HttpResponseMessage Json(string body) =>
            new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

        private static HttpResponseMessage Bytes(byte[] body) =>
            new(HttpStatusCode.OK) { Content = new ByteArrayContent(body) };

        private sealed class Handler(StubRegistry registry) : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken) =>
                Task.FromResult(registry.Answer(request));
        }
    }
}
