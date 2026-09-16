using System.Text;
using System.Text.Json;

using JulOS.Domain.Catalog;

namespace JulOS.Domain.Tests.Catalog;

/// <summary>CAT-002: what a catalog index is, and what the definition digest has to be.</summary>
[TestClass]
public sealed class CatalogDocumentTests
{
    /// <summary>
    /// The digest `tools/lib/catalog.mjs` computes for
    /// `tests/fixtures/catalog/valid/apps/home-assistant/2026.8.0/app.json`.
    /// </summary>
    /// <remarks>
    /// The same definition is signed once and verified in two languages, so the canonical
    /// form has to agree byte for byte. Pinning the validator's value here is what turns that
    /// agreement into something a test can fail on.
    /// </remarks>
    private const string FixtureDefinitionDigest =
        "a2cd547d040ee5a46bd53531a06158a342ab0669b3532b34787fe0b856852f4f";

    [TestMethod]
    public void TheDefinitionDigestMatchesTheRepositoryValidator()
    {
        var path = Path.Combine(
            RepositoryRoot(),
            "tests",
            "fixtures",
            "catalog",
            "valid",
            "apps",
            "home-assistant",
            "2026.8.0",
            "app.json");
        Assert.AreEqual(
            FixtureDefinitionDigest,
            CatalogCanonicalJson.DefinitionDigest(File.ReadAllBytes(path)),
            "The fixture is stored with a byte-order mark, which the canonical form has not.");
    }

    [TestMethod]
    public void CanonicalizationSortsKeysAndKeepsNonAsciiLiteral()
    {
        using var document = JsonDocument.Parse("""{"b":1,"a":{"d":[true,null],"c":"Grüße"}}""");

        var canonical = Encoding.UTF8.GetString(CatalogCanonicalJson.Canonicalize(document.RootElement));

        Assert.AreEqual("""{"a":{"c":"Grüße","d":[true,null]},"b":1}""", canonical);
    }

    [TestMethod]
    public void ANumberADoubleCannotRepresentExactlyIsRefused()
    {
        // The alternative would be hashing under a guess about how the other language prints
        // it, and the two digests would silently disagree.
        using var document = JsonDocument.Parse("""{"value":0.1}""");

        var failure = Assert.ThrowsExactly<DomainRuleViolationException>(
            () => CatalogCanonicalJson.DefinitionDigest(document.RootElement));

        Assert.AreEqual("catalog.definition_invalid", failure.Code);
    }

    [TestMethod]
    public void AValidIndexParses()
    {
        var index = CatalogIndexDocument.Parse(Utf8(Index()));

        Assert.AreEqual("community.example", index.SourceId);
        Assert.AreEqual(1, index.Entries.Count);
        Assert.AreEqual("home-assistant", index.Entries[0].AppId);
        Assert.AreEqual("apps/home-assistant/2026.8.0/app.json", index.Entries[0].File.Path);
        Assert.IsNull(index.KeySet);
    }

    [TestMethod]
    public void ADuplicateApplicationVersionFailsTheWholeIndex()
    {
        // Two definitions for one version would make "which bytes did this installation
        // verify" unanswerable, so the refusal is the whole refresh rather than one entry.
        var failure = Assert.ThrowsExactly<DomainRuleViolationException>(
            () => CatalogIndexDocument.Parse(Utf8(Index(entries: $"{Entry()},{Entry()}"))));

        Assert.AreEqual("catalog.definition_invalid", failure.Code);
    }

    [TestMethod]
    public void APathThatEscapesTheSourceRootIsRefused()
    {
        foreach (var path in new[]
        {
            "../secrets.json",
            "/etc/passwd",
            "apps/../../escape.json",
            "apps\\\\home-assistant\\\\app.json",
            "C:/windows/system32/config",
        })
        {
            var failure = Assert.ThrowsExactly<DomainRuleViolationException>(
                () => CatalogIndexDocument.Parse(Utf8(Index(entries: Entry(path: path)))),
                $"'{path}' does not stay below the source root.");
            Assert.AreEqual("catalog.definition_invalid", failure.Code);
        }
    }

    [TestMethod]
    public void AnUnknownSchemaIsRefusedBeforeAnythingElseIsRead()
    {
        var failure = Assert.ThrowsExactly<DomainRuleViolationException>(
            () => CatalogIndexDocument.Parse(Utf8("""{"schema":"app-catalog-index.v2"}""")));

        Assert.AreEqual("catalog.schema_unsupported", failure.Code);
    }

    [TestMethod]
    public void AMalformedDigestOrSourceIdentityIsRefused()
    {
        var digest = Assert.ThrowsExactly<DomainRuleViolationException>(
            () => CatalogIndexDocument.Parse(Utf8(Index(entries: Entry(sha256: "NOTHEX")))));
        Assert.AreEqual("catalog.definition_invalid", digest.Code);

        var sourceId = Assert.ThrowsExactly<DomainRuleViolationException>(
            () => CatalogIndexDocument.Parse(Utf8(Index(sourceId: "Community Example"))));
        Assert.AreEqual("catalog.definition_invalid", sourceId.Code);
    }

    [TestMethod]
    public void AKeySetParsesAndRefusesARepeatedKeyIdentity()
    {
        var keySet = CatalogKeySetDocument.Parse(Utf8(KeySet()));
        Assert.AreEqual("juloc-official", keySet.PublisherId);
        Assert.AreEqual("official-2026-01", keySet.Keys[0].KeyId);
        Assert.IsNull(keySet.Keys[0].ValidUntilUtc);

        var failure = Assert.ThrowsExactly<DomainRuleViolationException>(
            () => CatalogKeySetDocument.Parse(Utf8(KeySet(keys: $"{Key()},{Key()}"))));
        Assert.AreEqual("catalog.definition_invalid", failure.Code);
    }

    private const string Sha256 =
        "36de3265dad72885c930384f074625601d44d28d84e5dad9f70478edd3a39ede";

    private static string Entry(
        string appId = "home-assistant",
        string version = "2026.8.0",
        string path = "apps/home-assistant/2026.8.0/app.json",
        string sha256 = Sha256) =>
        $$"""{"appId":"{{appId}}","version":"{{version}}","path":"{{path}}","sha256":"{{sha256}}"}""";

    private static string Index(string sourceId = "community.example", string? entries = null) =>
        $$"""
        {
          "schema": "app-catalog-index.v1",
          "sourceId": "{{sourceId}}",
          "generatedAtUtc": "2026-08-25T12:00:00Z",
          "keySet": null,
          "entries": [{{entries ?? Entry()}}]
        }
        """;

    private static string Key(string keyId = "official-2026-01") =>
        $$"""
        {
          "keyId": "{{keyId}}",
          "algorithm": "ecdsa-p256-sha256-p1363",
          "publicKeySpkiBase64": "c3BraS1ieXRlcw==",
          "publicKeyFingerprint": "sha256:{{new string('a', 64)}}",
          "validFromUtc": "2026-01-01T00:00:00Z",
          "validUntilUtc": null,
          "revokedAtUtc": null
        }
        """;

    private static string KeySet(string? keys = null) =>
        $$"""
        {
          "schema": "app-catalog-keyset.v1",
          "publisherId": "juloc-official",
          "keys": [{{keys ?? Key()}}]
        }
        """;

    private static byte[] Utf8(string value) => Encoding.UTF8.GetBytes(value);

    private static string RepositoryRoot()
    {
        var directory = AppContext.BaseDirectory;
        while (directory is not null && !File.Exists(Path.Combine(directory, "JulOS.slnx")))
        {
            directory = Path.GetDirectoryName(directory);
        }

        return directory ?? throw new AssertFailedException("The repository root could not be located.");
    }
}
