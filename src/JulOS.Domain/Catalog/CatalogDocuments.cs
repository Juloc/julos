using System.Text.Json;

namespace JulOS.Domain.Catalog;

/// <summary>A digest-bound reference to one file below a catalog source root.</summary>
/// <param name="Path">Relative, normalized path that cannot escape the source root.</param>
/// <param name="Sha256">Lowercase hexadecimal digest the bytes must match before they are parsed.</param>
public sealed record CatalogFileReference(string Path, string Sha256);

/// <summary>One entry of a catalog index.</summary>
/// <param name="AppId">Application identity within the source.</param>
/// <param name="Version">Application version.</param>
/// <param name="File">Where the definition lives and what it must hash to.</param>
public sealed record CatalogIndexEntry(string AppId, string Version, CatalogFileReference File);

/// <summary>
/// The <c>catalog.json</c> at a source root.
/// </summary>
/// <remarks>
/// Parsing is deliberately strict and total: every rule the index has to satisfy is checked
/// here, before anything it points at is read. An index that names a path outside the source
/// root, or the same application version twice, fails the complete refresh rather than the
/// one entry, because a catalog that contradicts itself is not a catalog this installation
/// can serve part of.
/// </remarks>
public sealed class CatalogIndexDocument
{
    /// <summary>The only accepted index schema.</summary>
    public const string SchemaName = "app-catalog-index.v1";

    /// <summary>The largest index this build accepts, matching the published schema.</summary>
    private const int MaximumEntries = 4096;

    private CatalogIndexDocument(
        string sourceId,
        DateTimeOffset generatedAtUtc,
        CatalogFileReference? keySet,
        IReadOnlyList<CatalogIndexEntry> entries)
    {
        this.SourceId = sourceId;
        this.GeneratedAtUtc = generatedAtUtc;
        this.KeySet = keySet;
        this.Entries = entries;
    }

    /// <summary>The stable identity the source claims.</summary>
    public string SourceId { get; }

    /// <summary>When the source generated the index.</summary>
    public DateTimeOffset GeneratedAtUtc { get; }

    /// <summary>The optional published key set.</summary>
    public CatalogFileReference? KeySet { get; }

    /// <summary>The entries, in index order.</summary>
    public IReadOnlyList<CatalogIndexEntry> Entries { get; }

    /// <summary>Parses and validates an index document.</summary>
    /// <param name="utf8">The exact bytes read from the source.</param>
    /// <exception cref="DomainRuleViolationException">The document is not a valid index.</exception>
    public static CatalogIndexDocument Parse(ReadOnlySpan<byte> utf8)
    {
        var root = CatalogJson.ParseObject(utf8, "catalog.schema_unsupported");
        CatalogJson.RequireSchema(root, SchemaName);

        var entries = new List<CatalogIndexEntry>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        var array = CatalogJson.RequireArray(root, "entries");
        if (array.GetArrayLength() > MaximumEntries)
        {
            throw Invalid($"A catalog index has at most {MaximumEntries} entries.");
        }

        foreach (var item in array.EnumerateArray())
        {
            var appId = CatalogJson.RequireIdentifier(item, "appId");
            var version = CatalogJson.RequireText(item, "version", 64);
            var entry = new CatalogIndexEntry(
                appId,
                version,
                new CatalogFileReference(
                    CatalogJson.RequireRelativePath(item, "path"),
                    CatalogJson.RequireDigest(item, "sha256")));

            if (!seen.Add($"{appId}\n{version}"))
            {
                // One version of one application has exactly one definition. Two would make
                // "which bytes did this installation verify" unanswerable.
                throw Invalid($"The catalog index lists '{appId}' version '{version}' more than once.");
            }

            entries.Add(entry);
        }

        return new CatalogIndexDocument(
            CatalogJson.RequireSourceId(root, "sourceId"),
            CatalogJson.RequireTimestamp(root, "generatedAtUtc"),
            CatalogJson.OptionalFileReference(root, "keySet"),
            entries);
    }

    private static DomainRuleViolationException Invalid(string message) =>
        new("catalog.definition_invalid", message);
}

/// <summary>One key of a published key set.</summary>
/// <param name="KeyId">The publisher-assigned key identity.</param>
/// <param name="Algorithm">The signature algorithm the key is for.</param>
/// <param name="PublicKeySpkiBase64">Base64 SubjectPublicKeyInfo bytes.</param>
/// <param name="PublicKeyFingerprint">The fingerprint the source states, as <c>sha256:...</c>.</param>
/// <param name="ValidFromUtc">Start of the key validity interval.</param>
/// <param name="ValidUntilUtc">End of the interval, or null when open-ended.</param>
/// <param name="RevokedAtUtc">When the key was revoked, or null.</param>
public sealed record CatalogKeySetKey(
    string KeyId,
    string Algorithm,
    string PublicKeySpkiBase64,
    string PublicKeyFingerprint,
    DateTimeOffset ValidFromUtc,
    DateTimeOffset? ValidUntilUtc,
    DateTimeOffset? RevokedAtUtc);

/// <summary>
/// The optional <c>keys.json</c> at a source root.
/// </summary>
/// <remarks>
/// A published key set says which keys a source claims. It never says whether they are
/// trusted: transport trust is not publisher trust, so what this document produces is an
/// observation that an administrator may later decide about.
/// </remarks>
public sealed class CatalogKeySetDocument
{
    /// <summary>The only accepted key-set schema.</summary>
    public const string SchemaName = "app-catalog-keyset.v1";

    private const int MaximumKeys = 64;

    private CatalogKeySetDocument(string publisherId, IReadOnlyList<CatalogKeySetKey> keys)
    {
        this.PublisherId = publisherId;
        this.Keys = keys;
    }

    /// <summary>The publisher the keys belong to.</summary>
    public string PublisherId { get; }

    /// <summary>The published keys.</summary>
    public IReadOnlyList<CatalogKeySetKey> Keys { get; }

    /// <summary>Parses and validates a key-set document.</summary>
    /// <param name="utf8">The exact bytes read from the source.</param>
    /// <exception cref="DomainRuleViolationException">The document is not a valid key set.</exception>
    public static CatalogKeySetDocument Parse(ReadOnlySpan<byte> utf8)
    {
        var root = CatalogJson.ParseObject(utf8, "catalog.schema_unsupported");
        CatalogJson.RequireSchema(root, SchemaName);

        var keys = new List<CatalogKeySetKey>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        var array = CatalogJson.RequireArray(root, "keys");
        if (array.GetArrayLength() > MaximumKeys)
        {
            throw new DomainRuleViolationException(
                "catalog.definition_invalid",
                $"A catalog key set has at most {MaximumKeys} keys.");
        }

        foreach (var item in array.EnumerateArray())
        {
            var keyId = CatalogJson.RequireKeyId(item, "keyId");
            if (!seen.Add(keyId))
            {
                throw new DomainRuleViolationException(
                    "catalog.definition_invalid",
                    $"The catalog key set lists key '{keyId}' more than once.");
            }

            keys.Add(new CatalogKeySetKey(
                keyId,
                CatalogJson.RequireText(item, "algorithm", 64),
                CatalogJson.RequireText(item, "publicKeySpkiBase64", 4096),
                CatalogJson.RequireFingerprint(item, "publicKeyFingerprint"),
                CatalogJson.RequireTimestamp(item, "validFromUtc"),
                CatalogJson.OptionalTimestamp(item, "validUntilUtc"),
                CatalogJson.OptionalTimestamp(item, "revokedAtUtc")));
        }

        return new CatalogKeySetDocument(CatalogJson.RequireIdentifier(root, "publisherId"), keys);
    }
}

/// <summary>
/// The optional <c>signature.json</c> beside a definition.
/// </summary>
/// <remarks>
/// Parsing is separate from verification on purpose. A malformed envelope is a claim of
/// authenticity this build cannot complete, which section 6 calls an invalid signature
/// rather than an absent one, so the refusal has to be visible rather than silently read as
/// unsigned content.
/// </remarks>
public static class CatalogSignatureDocument
{
    /// <summary>Parses and validates a detached signature envelope.</summary>
    /// <param name="utf8">The exact bytes read from the source.</param>
    /// <exception cref="DomainRuleViolationException">The document is not a valid envelope.</exception>
    public static CatalogSignatureEnvelope Parse(ReadOnlySpan<byte> utf8)
    {
        var root = CatalogJson.ParseObject(utf8, "catalog.signature_invalid");

        var schemaVersion = root.TryGetProperty("schemaVersion", out var version)
            && version.ValueKind == JsonValueKind.Number
            && version.TryGetInt32(out var parsed)
                ? parsed
                : throw new DomainRuleViolationException(
                    "catalog.signature_invalid",
                    "A signature envelope declares its schema version.");

        return new CatalogSignatureEnvelope(
            schemaVersion,
            CatalogJson.RequireIdentifier(root, "publisherId"),
            CatalogJson.RequireKeyId(root, "keyId"),
            CatalogJson.RequireFingerprint(root, "publicKeyFingerprint"),
            CatalogJson.RequireText(root, "algorithm", 64),
            CatalogJson.RequireDigest(root, "artifactSha256"),
            CatalogJson.RequireTimestamp(root, "createdAtUtc"),
            CatalogJson.RequireText(root, "signature", 512));
    }
}

/// <summary>Reading rules shared by the catalog documents.</summary>
internal static class CatalogJson
{
    /// <summary>The UTF-8 byte-order mark, which a published catalog document may carry.</summary>
    private static ReadOnlySpan<byte> ByteOrderMark => [0xEF, 0xBB, 0xBF];

    internal static JsonDocument Parse(ReadOnlySpan<byte> utf8, string code)
    {
        // JSON itself has no byte-order mark, but a file written on Windows or by this
        // repository's own encoding policy does. Skipping it here means the same document is
        // read identically wherever it was produced.
        var body = utf8.StartsWith(ByteOrderMark) ? utf8[ByteOrderMark.Length..] : utf8;

        try
        {
            var reader = new Utf8JsonReader(body);
            return JsonDocument.ParseValue(ref reader);
        }
        catch (JsonException exception)
        {
            throw new DomainRuleViolationException(code, "A catalog document is not valid JSON.", exception);
        }
    }

    internal static JsonElement ParseObject(ReadOnlySpan<byte> utf8, string code)
    {
        using var document = Parse(utf8, code);
        return document.RootElement.ValueKind == JsonValueKind.Object
            ? document.RootElement.Clone()
            : throw new DomainRuleViolationException(code, "A catalog document is a JSON object.");
    }

    internal static void RequireSchema(JsonElement root, string expected)
    {
        var actual = root.TryGetProperty("schema", out var schema) && schema.ValueKind == JsonValueKind.String
            ? schema.GetString()
            : null;

        if (!string.Equals(actual, expected, StringComparison.Ordinal))
        {
            throw new DomainRuleViolationException(
                "catalog.schema_unsupported",
                $"A catalog document declares schema '{actual ?? "none"}' where '{expected}' is required.");
        }
    }

    internal static JsonElement RequireArray(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Array
            ? value
            : throw Invalid($"A catalog document requires the array '{name}'.");

    internal static string RequireText(JsonElement element, string name, int maximumLength)
    {
        var value = element.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

        return string.IsNullOrEmpty(value) || value.Length > maximumLength
            ? throw Invalid($"A catalog document requires '{name}' with 1 to {maximumLength} characters.")
            : value;
    }

    /// <summary>Reads a lower-case identity segment, as the published schemas define one.</summary>
    internal static string RequireIdentifier(JsonElement element, string name)
    {
        var value = RequireText(element, name, 63);
        var valid = char.IsAsciiLetterLower(value[0]) || char.IsAsciiDigit(value[0]);
        foreach (var character in value)
        {
            valid &= char.IsAsciiLetterLower(character) || char.IsAsciiDigit(character) || character == '-';
        }

        return valid
            ? value
            : throw Invalid($"'{name}' is lower-case letters, digits and hyphens starting with a letter or digit.");
    }

    internal static string RequireSourceId(JsonElement element, string name)
    {
        var value = RequireText(element, name, 128);
        var valid = true;
        foreach (var segment in value.Split('.'))
        {
            valid &= segment.Length > 0
                && (char.IsAsciiLetterLower(segment[0]) || char.IsAsciiDigit(segment[0]));
            foreach (var character in segment)
            {
                valid &= char.IsAsciiLetterLower(character) || char.IsAsciiDigit(character) || character == '-';
            }
        }

        return valid ? value : throw Invalid($"'{name}' is dot-separated lower-case identity segments.");
    }

    internal static string RequireKeyId(JsonElement element, string name)
    {
        var value = RequireText(element, name, 64);
        var valid = char.IsAsciiLetterOrDigit(value[0]);
        foreach (var character in value)
        {
            valid &= char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or '-';
        }

        return valid ? value : throw Invalid($"'{name}' is letters, digits, dot, underscore and hyphen.");
    }

    internal static string RequireDigest(JsonElement element, string name)
    {
        var value = RequireText(element, name, 64);
        return CatalogSignatureInput.IsLowercaseSha256(value)
            ? value
            : throw Invalid($"'{name}' is 64 lower-case hexadecimal characters.");
    }

    internal static string RequireFingerprint(JsonElement element, string name)
    {
        var value = RequireText(element, name, 71);
        return value.StartsWith("sha256:", StringComparison.Ordinal)
            && CatalogSignatureInput.IsLowercaseSha256(value["sha256:".Length..])
            ? value
            : throw Invalid($"'{name}' is 'sha256:' followed by 64 lower-case hexadecimal characters.");
    }

    /// <summary>
    /// Reads a path that stays below the source root.
    /// </summary>
    /// <remarks>
    /// The check is on the declared text rather than on a resolved file-system path, because
    /// the same index is read over HTTPS and out of an OCI artifact where there is no path to
    /// resolve. Refusing the traversal in the document means every reader inherits it.
    /// </remarks>
    internal static string RequireRelativePath(JsonElement element, string name)
    {
        var value = RequireText(element, name, 512);
        var rejected = value.Contains('\\', StringComparison.Ordinal)
            || value.StartsWith('/')
            || value.Contains("//", StringComparison.Ordinal)
            || value.EndsWith('/')
            || (value.Length > 1 && value[1] == ':');

        foreach (var segment in value.Split('/'))
        {
            rejected |= segment.Length == 0 || segment == "." || segment == "..";
        }

        return rejected
            ? throw Invalid($"'{name}' is a relative path below the source root.")
            : value;
    }

    internal static DateTimeOffset RequireTimestamp(JsonElement element, string name) =>
        OptionalTimestamp(element, name)
        ?? throw Invalid($"A catalog document requires the timestamp '{name}'.");

    internal static DateTimeOffset? OptionalTimestamp(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var property) || property.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        return property.ValueKind == JsonValueKind.String
            && DateTimeOffset.TryParse(
                property.GetString(),
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AdjustToUniversal
                    | System.Globalization.DateTimeStyles.AssumeUniversal,
                out var parsed)
            ? parsed
            : throw Invalid($"'{name}' is an RFC 3339 timestamp.");
    }

    internal static CatalogFileReference? OptionalFileReference(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var property) || property.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        return property.ValueKind == JsonValueKind.Object
            ? new CatalogFileReference(
                RequireRelativePath(property, "path"),
                RequireDigest(property, "sha256"))
            : throw Invalid($"'{name}' is an object with a path and a digest.");
    }

    private static DomainRuleViolationException Invalid(string message) =>
        new("catalog.definition_invalid", message);
}
