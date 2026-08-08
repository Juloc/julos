using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using JulOS.Contracts.Authentication;
using JulOS.Contracts.Packages;
using JulOS.Infrastructure.Persistence.Core;
using JulOS.Integration.Tests.Persistence;

using Microsoft.AspNetCore.Mvc.Testing;

namespace JulOS.Integration.Tests.Packages;

/// <summary>
/// Drives the real Package Manager HTTP API end to end. These endpoints had no integration
/// coverage before; a live deployment check found that installing, listing and removing a
/// package all crashed the server (missing authorization policy registration, an unhandled
/// exception for a faulted installation with no metadata file, and archive path validation
/// that rejected valid ZIP directory entries).
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class PackageEndpointTests
{
    private const string PackageId = "de.juloc.test.noop";
    private const string PublisherId = "juloc-test";
    private const string PublisherKeyId = "test-key";

    private static readonly WebApplicationFactoryClientOptions ClientOptions = new()
    {
        BaseAddress = new Uri("https://localhost"),
        AllowAutoRedirect = false,
        HandleCookies = true,
    };

    [TestMethod]
    public async Task InstallListConfigureEnableDisableAndRemoveSucceed()
    {
        using var signingKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        await using var database = await CreateMigratedDatabaseAsync().ConfigureAwait(false);
        var packageRoot = CreateTemporaryPackageRoot();

        try
        {
            using var host = new ServerHost(
                database.ConnectionString,
                TrustSettings(signingKey, packageRoot));
            using var client = host.CreateClient(ClientOptions);
            await SetupAdministratorAsync(client).ConfigureAwait(false);

            var (archive, digest) = CreateSignedArchive(signingKey, "1.0.0", includeDirectoryEntry: true);
            using var install = await InstallAsync(client, signingKey, archive, digest).ConfigureAwait(false);
            Assert.AreEqual(HttpStatusCode.Created, install.StatusCode);
            var installed = await install.Content.ReadFromJsonAsync<PackageInstallationResponse>().ConfigureAwait(false);
            Assert.IsNotNull(installed);
            Assert.AreEqual("installed", installed.State);
            Assert.AreEqual(digest, installed.ArtifactDigest);

            using var list = await client.GetAsync("/api/v1/packages").ConfigureAwait(false);
            Assert.AreEqual(HttpStatusCode.OK, list.StatusCode);
            var listed = await list.Content.ReadFromJsonAsync<PackageInstallationResponse[]>().ConfigureAwait(false);
            Assert.IsNotNull(listed);
            Assert.IsTrue(listed.Any(package => package.PackageId == PackageId));

            using var configure = await SendAsync(
                client,
                HttpMethod.Put,
                $"/api/v1/packages/{PackageId}/configuration",
                new ConfigurePackageRequest(installed.Revision, new Dictionary<string, string>()))
                .ConfigureAwait(false);
            Assert.AreEqual(HttpStatusCode.OK, configure.StatusCode);
            var configured = await configure.Content.ReadFromJsonAsync<PackageInstallationResponse>().ConfigureAwait(false);
            Assert.IsNotNull(configured);

            using var enable = await SendAsync(
                client,
                HttpMethod.Post,
                $"/api/v1/packages/{PackageId}/enable",
                new PackageRevisionRequest(configured.Revision))
                .ConfigureAwait(false);
            Assert.AreEqual(HttpStatusCode.OK, enable.StatusCode);
            var enabled = await enable.Content.ReadFromJsonAsync<PackageInstallationResponse>().ConfigureAwait(false);
            Assert.IsNotNull(enabled);
            Assert.AreEqual("enabled", enabled.State);

            using var disable = await SendAsync(
                client,
                HttpMethod.Post,
                $"/api/v1/packages/{PackageId}/disable",
                new PackageRevisionRequest(enabled.Revision))
                .ConfigureAwait(false);
            Assert.AreEqual(HttpStatusCode.OK, disable.StatusCode);
            var disabled = await disable.Content.ReadFromJsonAsync<PackageInstallationResponse>().ConfigureAwait(false);
            Assert.IsNotNull(disabled);
            Assert.AreEqual("disabled", disabled.State);

            using var remove = await SendAsync(
                client,
                HttpMethod.Delete,
                $"/api/v1/packages/{PackageId}",
                new RemovePackageRequest(disabled.Revision, DeletePackageData: true))
                .ConfigureAwait(false);
            Assert.AreEqual(HttpStatusCode.OK, remove.StatusCode);
        }
        finally
        {
            Directory.Delete(packageRoot, recursive: true);
        }
    }

    [TestMethod]
    public async Task AFaultedInstallationDoesNotBreakListingOrRemoval()
    {
        using var signingKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        await using var database = await CreateMigratedDatabaseAsync().ConfigureAwait(false);
        var packageRoot = CreateTemporaryPackageRoot();

        try
        {
            using var host = new ServerHost(
                database.ConnectionString,
                TrustSettings(signingKey, packageRoot));
            using var client = host.CreateClient(ClientOptions);
            await SetupAdministratorAsync(client).ConfigureAwait(false);

            // A path-traversal entry passes signature/digest verification (which covers the
            // whole archive, not its contents) and only fails once the installation row
            // already exists and extraction starts, leaving a "faulted" row with no metadata
            // file ever written to disk.
            var (archive, digest) = CreateArchiveWithPathTraversalEntry();
            using var install = await InstallAsync(client, signingKey, archive, digest).ConfigureAwait(false);
            Assert.AreEqual(HttpStatusCode.BadRequest, install.StatusCode);

            using var list = await client.GetAsync("/api/v1/packages").ConfigureAwait(false);
            Assert.AreEqual(HttpStatusCode.OK, list.StatusCode);
            var listed = await list.Content.ReadFromJsonAsync<PackageInstallationResponse[]>().ConfigureAwait(false);
            Assert.IsNotNull(listed);
            var faulted = listed.Single(package => package.PackageId == PackageId);
            Assert.AreEqual("faulted", faulted.State);
            Assert.IsNotNull(faulted.FaultCode);

            using var remove = await SendAsync(
                client,
                HttpMethod.Delete,
                $"/api/v1/packages/{PackageId}",
                new RemovePackageRequest(faulted.Revision, DeletePackageData: true))
                .ConfigureAwait(false);
            Assert.AreEqual(HttpStatusCode.OK, remove.StatusCode);
        }
        finally
        {
            Directory.Delete(packageRoot, recursive: true);
        }
    }

    private static async Task<HttpResponseMessage> InstallAsync(
        HttpClient client,
        ECDsa signingKey,
        byte[] archive,
        string? expectedDigest)
    {
        var signature = signingKey.SignData(
            archive,
            HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        using var content = new MultipartFormDataContent
        {
            { new ByteArrayContent(archive) { Headers = { ContentType = new MediaTypeHeaderValue("application/zip") } }, "Artifact", "package.zip" },
            { new ByteArrayContent(signature) { Headers = { ContentType = new MediaTypeHeaderValue("application/octet-stream") } }, "Signature", "package.zip.sig" },
            { new StringContent(PublisherId), "PublisherId" },
            { new StringContent(PublisherKeyId), "PublisherKeyId" },
            { new StringContent(Guid.NewGuid().ToString("N")), "OperationKey" },
        };
        if (expectedDigest is not null)
        {
            content.Add(new StringContent(expectedDigest), "ExpectedDigest");
        }

        var token = await ReadAntiforgeryTokenAsync(client).ConfigureAwait(false);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/packages/install") { Content = content };
        request.Headers.Add(token.HeaderName, token.Token);
        return await client.SendAsync(request).ConfigureAwait(false);
    }

    private static async Task<HttpResponseMessage> SendAsync<TBody>(
        HttpClient client,
        HttpMethod method,
        string requestUri,
        TBody body)
    {
        var token = await ReadAntiforgeryTokenAsync(client).ConfigureAwait(false);
        using var request = new HttpRequestMessage(method, requestUri)
        {
            Content = JsonContent.Create(body),
        };
        request.Headers.Add(token.HeaderName, token.Token);
        return await client.SendAsync(request).ConfigureAwait(false);
    }

    private static async Task<AntiforgeryTokenResponse> ReadAntiforgeryTokenAsync(HttpClient client)
    {
        var token = await client
            .GetFromJsonAsync<AntiforgeryTokenResponse>("/api/v1/auth/antiforgery")
            .ConfigureAwait(false);
        return token ?? throw new InvalidOperationException("Antiforgery token was not issued.");
    }

    private static async Task SetupAdministratorAsync(HttpClient client)
    {
        using var setup = await client.PostAsJsonAsync(
            "/api/v1/auth/setup",
            new InitialAdministratorRequest("admin", "Administrator", "Valid-Initial-Password-42!"))
            .ConfigureAwait(false);
        Assert.AreEqual(HttpStatusCode.Created, setup.StatusCode);
    }

    private static (byte[] Archive, string Digest) CreateSignedArchive(
        ECDsa signingKey,
        string version,
        bool includeDirectoryEntry)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            if (includeDirectoryEntry)
            {
                archive.CreateEntry("frontend/", CompressionLevel.NoCompression);
            }

            WriteEntry(archive, "manifest.json", BuildManifestJson(version));
        }

        var bytes = stream.ToArray();
        var digest = Convert.ToHexStringLower(SHA256.HashData(bytes));
        return (bytes, digest);
    }

    private static (byte[] Archive, string Digest) CreateArchiveWithPathTraversalEntry()
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteEntry(archive, "manifest.json", BuildManifestJson("1.0.0"));
            WriteEntry(archive, "../escape.txt", "unauthorized");
        }

        var bytes = stream.ToArray();
        var digest = Convert.ToHexStringLower(SHA256.HashData(bytes));
        return (bytes, digest);
    }

    private static string BuildManifestJson(string version) => $$"""
        {
          "SchemaVersion": "1",
          "PackageId": "{{PackageId}}",
          "Version": "{{version}}",
          "PublisherId": "{{PublisherId}}",
          "DisplayNameKey": "package.test.name",
          "DescriptionKey": "package.test.description",
          "Runtime": {
            "Kind": "none",
            "Image": null,
            "EntryPoint": null,
            "MemoryLimitMegabytes": 64,
            "CpuLimit": 0.1,
            "StartupTimeoutSeconds": 5,
            "NetworkAccess": false
          },
          "Permissions": ["core.system.version.read"],
          "Applications": [],
          "Widgets": [],
          "Capabilities": [],
          "Migrations": [],
          "Frontend": null
        }
        """;

    private static void WriteEntry(ZipArchive archive, string path, string content)
    {
        var entry = archive.CreateEntry(path, CompressionLevel.NoCompression);
        using var writer = new StreamWriter(entry.Open(), Encoding.UTF8, leaveOpen: false);
        writer.Write(content);
    }

    private static string CreateTemporaryPackageRoot()
    {
        var path = Path.Combine(Path.GetTempPath(), "julos-package-endpoint-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static Dictionary<string, string?> TrustSettings(ECDsa signingKey, string packageRoot) => new()
    {
        ["Packages:Root"] = packageRoot,
        ["Packages:TrustedPublishers:0:PublisherId"] = PublisherId,
        ["Packages:TrustedPublishers:0:KeyId"] = PublisherKeyId,
        ["Packages:TrustedPublishers:0:PublicKeyPem"] = signingKey.ExportSubjectPublicKeyInfoPem(),
    };

    private static async Task<PostgreSqlTestDatabase> CreateMigratedDatabaseAsync()
    {
        var database = await PostgreSqlTestDatabase.CreateAsync().ConfigureAwait(false);
        await CoreDatabaseMigrator.MigrateAsync(database.ConnectionString).ConfigureAwait(false);
        return database;
    }
}
