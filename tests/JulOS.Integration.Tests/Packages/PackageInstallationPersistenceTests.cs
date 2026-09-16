using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;

using JulOS.Contracts.Authentication;
using JulOS.Contracts.Packages;

using Microsoft.AspNetCore.Mvc.Testing;

namespace JulOS.Integration.Tests.Packages;

/// <summary>
/// The package install path against a real database, on SQLite.
/// </summary>
/// <remarks>
/// The rest of the package lifecycle is covered against PostgreSQL, which reports
/// inconclusive wherever no container is configured. Installing is the one step whose
/// persistence must be exercised on every machine: <c>PKG-013</c> added a
/// <c>signature_state</c> column with a check constraint and no install path that filled it,
/// and every install failed on a real database while the whole suite stayed green here.
/// </remarks>
[TestClass]
[DoNotParallelize]
public sealed class PackageInstallationPersistenceTests
{
    private const string PackageId = "de.juloc.install-persistence";
    private const string PublisherId = "juloc-test";
    private const string PublisherKeyId = "test-key";

    private static readonly WebApplicationFactoryClientOptions ClientOptions = new()
    {
        BaseAddress = new Uri("https://localhost"),
        AllowAutoRedirect = false,
        HandleCookies = true,
    };

    [TestMethod]
    public async Task InstallingWritesEveryColumnTheSchemaRequires()
    {
        using var signingKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var packageRoot = Path.Combine(
            Path.GetTempPath(),
            "julos-package-persistence-tests",
            Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(packageRoot);

        try
        {
            await using var database = await SqliteServerHost
                .CreateAsync("package-install", TrustSettings(signingKey, packageRoot))
                .ConfigureAwait(false);
            using var client = database.CreateClient(ClientOptions);
            await SetupAdministratorAsync(client).ConfigureAwait(false);

            var (archive, digest) = CreateSignedArchive(signingKey);

            using var install = await InstallAsync(client, signingKey, archive, digest).ConfigureAwait(false);

            Assert.AreEqual(
                HttpStatusCode.Created,
                install.StatusCode,
                await install.Content.ReadAsStringAsync().ConfigureAwait(false));
            var installed = await install.Content
                .ReadFromJsonAsync<PackageInstallationResponse>()
                .ConfigureAwait(false);
            Assert.IsNotNull(installed);
            Assert.AreEqual("installed", installed.State);
            Assert.AreEqual(digest, installed.ArtifactDigest);

            // The verifier accepts only a configured trusted publisher today, so the stored
            // state has to be the one that says exactly that.
            Assert.AreEqual(
                "TrustedSigned",
                await ReadSignatureStateAsync(database.ConnectionString).ConfigureAwait(false));
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            try
            {
                Directory.Delete(packageRoot, recursive: true);
            }
            catch (IOException)
            {
                // The isolated package database file may still be held open; a temporary
                // directory left behind is test litter, not a failure.
            }
        }
    }

    private static async Task<string?> ReadSignatureStateAsync(string connectionString)
    {
        await using var connection = new Microsoft.Data.Sqlite.SqliteConnection(connectionString);
        await connection.OpenAsync().ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT signature_state FROM package_installations WHERE package_id = $id;";
        _ = command.Parameters.AddWithValue("$id", PackageId);
        return await command.ExecuteScalarAsync().ConfigureAwait(false) as string;
    }

    private static async Task<HttpResponseMessage> InstallAsync(
        HttpClient client,
        ECDsa signingKey,
        byte[] archive,
        string expectedDigest)
    {
        var signature = signingKey.SignData(
            archive,
            HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        using var content = new MultipartFormDataContent
        {
            {
                new ByteArrayContent(archive)
                {
                    Headers = { ContentType = new MediaTypeHeaderValue("application/zip") },
                },
                "Artifact",
                "package.zip"
            },
            {
                new ByteArrayContent(signature)
                {
                    Headers = { ContentType = new MediaTypeHeaderValue("application/octet-stream") },
                },
                "Signature",
                "package.zip.sig"
            },
            { new StringContent(PublisherId), "PublisherId" },
            { new StringContent(PublisherKeyId), "PublisherKeyId" },
            { new StringContent(expectedDigest), "ExpectedDigest" },
            { new StringContent(Guid.NewGuid().ToString("N")), "OperationKey" },
        };

        var token = await ReadAntiforgeryTokenAsync(client).ConfigureAwait(false);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/packages/install")
        {
            Content = content,
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

    private static (byte[] Archive, string Digest) CreateSignedArchive(ECDsa signingKey)
    {
        _ = signingKey;
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = archive.CreateEntry("manifest.json", CompressionLevel.NoCompression);
            using var writer = new StreamWriter(entry.Open(), Encoding.UTF8, leaveOpen: false);
            writer.Write(ManifestJson);
        }

        var bytes = stream.ToArray();
        return (bytes, Convert.ToHexStringLower(SHA256.HashData(bytes)));
    }

    private const string ManifestJson = $$"""
        {
          "SchemaVersion": "1",
          "PackageId": "{{PackageId}}",
          "Version": "1.0.0",
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

    private static Dictionary<string, string?> TrustSettings(ECDsa signingKey, string packageRoot) => new()
    {
        ["Packages:Root"] = packageRoot,
        ["Packages:TrustedPublishers:0:PublisherId"] = PublisherId,
        ["Packages:TrustedPublishers:0:KeyId"] = PublisherKeyId,
        ["Packages:TrustedPublishers:0:PublicKeyPem"] = signingKey.ExportSubjectPublicKeyInfoPem(),
    };
}
