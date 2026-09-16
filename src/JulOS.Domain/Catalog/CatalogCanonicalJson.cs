using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace JulOS.Domain.Catalog;

/// <summary>
/// Canonical JSON bytes for a catalog definition, and the definition digest over them.
/// </summary>
/// <remarks>
/// <para>
/// The digest has to agree byte for byte with the digest the repository validator computes,
/// because the same definition is signed once and verified in both places. That is why the
/// rules here are spelled out rather than delegated to a serializer's default behaviour:
/// object keys sorted by UTF-16 code unit, minimal string escaping, no insignificant
/// whitespace.
/// </para>
/// <para>
/// Numbers are restricted to integers a double represents exactly. `app-manifest.v1` has one
/// numeric field and it is a bounded port, so nothing legitimate is excluded, and the
/// restriction removes the whole class of digest disagreements that floating-point
/// formatting differences between two languages would otherwise cause. A document carrying
/// any other number is refused rather than hashed under a guess.
/// </para>
/// </remarks>
public static class CatalogCanonicalJson
{
    /// <summary>The largest integer a double represents exactly.</summary>
    private const long SafeInteger = 9007199254740991;

    /// <summary>Canonicalizes a parsed JSON document.</summary>
    /// <param name="element">The document root.</param>
    /// <returns>The canonical UTF-8 bytes.</returns>
    /// <exception cref="DomainRuleViolationException">The document carries a value that cannot be canonicalized.</exception>
    public static byte[] Canonicalize(JsonElement element)
    {
        var builder = new StringBuilder();
        Write(element, builder);
        return Encoding.UTF8.GetBytes(builder.ToString());
    }

    /// <summary>Computes the definition digest of a parsed definition document.</summary>
    /// <param name="element">The document root.</param>
    /// <returns>The lowercase hexadecimal SHA-256 over the canonical bytes.</returns>
    /// <exception cref="DomainRuleViolationException">The document carries a value that cannot be canonicalized.</exception>
    public static string DefinitionDigest(JsonElement element) =>
        Convert.ToHexStringLower(SHA256.HashData(Canonicalize(element)));

    /// <summary>
    /// Computes the definition digest of the exact bytes a source published.
    /// </summary>
    /// <remarks>
    /// The entry digest in the index covers the published bytes, byte-order mark included;
    /// this digest covers the canonical form, which has no byte-order mark by construction.
    /// The two answer different questions and are deliberately not the same value.
    /// </remarks>
    /// <param name="utf8">The exact bytes read from the source.</param>
    /// <returns>The lowercase hexadecimal SHA-256 over the canonical bytes.</returns>
    /// <exception cref="DomainRuleViolationException">The bytes are not a canonicalizable JSON document.</exception>
    public static string DefinitionDigest(ReadOnlySpan<byte> utf8)
    {
        using var document = CatalogJson.Parse(utf8, "catalog.definition_invalid");
        return DefinitionDigest(document.RootElement);
    }

    private static void Write(JsonElement element, StringBuilder builder)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                WriteObject(element, builder);
                break;
            case JsonValueKind.Array:
                WriteArray(element, builder);
                break;
            case JsonValueKind.String:
                WriteString(element.GetString() ?? string.Empty, builder);
                break;
            case JsonValueKind.Number:
                WriteNumber(element, builder);
                break;
            case JsonValueKind.True:
                _ = builder.Append("true");
                break;
            case JsonValueKind.False:
                _ = builder.Append("false");
                break;
            case JsonValueKind.Null:
                _ = builder.Append("null");
                break;
            default:
                throw new DomainRuleViolationException(
                    "catalog.definition_invalid",
                    "A catalog definition contains a value that is not JSON.");
        }
    }

    private static void WriteObject(JsonElement element, StringBuilder builder)
    {
        // Ordinal order is UTF-16 code unit order, which is what the canonical form requires.
        var properties = element
            .EnumerateObject()
            .OrderBy(property => property.Name, StringComparer.Ordinal)
            .ToArray();

        _ = builder.Append('{');
        for (var index = 0; index < properties.Length; index++)
        {
            if (index > 0)
            {
                _ = builder.Append(',');
            }

            WriteString(properties[index].Name, builder);
            _ = builder.Append(':');
            Write(properties[index].Value, builder);
        }

        _ = builder.Append('}');
    }

    private static void WriteArray(JsonElement element, StringBuilder builder)
    {
        _ = builder.Append('[');
        var first = true;
        foreach (var item in element.EnumerateArray())
        {
            if (!first)
            {
                _ = builder.Append(',');
            }

            first = false;
            Write(item, builder);
        }

        _ = builder.Append(']');
    }

    private static void WriteNumber(JsonElement element, StringBuilder builder)
    {
        if (!element.TryGetInt64(out var value) || Math.Abs(value) > SafeInteger)
        {
            throw new DomainRuleViolationException(
                "catalog.definition_invalid",
                "A catalog definition number is an integer a double represents exactly.");
        }

        _ = builder.Append(value.ToString(CultureInfo.InvariantCulture));
    }

    private static void WriteString(string value, StringBuilder builder)
    {
        _ = builder.Append('"');
        foreach (var character in value)
        {
            switch (character)
            {
                case '"':
                    _ = builder.Append("\\\"");
                    break;
                case '\\':
                    _ = builder.Append("\\\\");
                    break;
                case '\b':
                    _ = builder.Append("\\b");
                    break;
                case '\f':
                    _ = builder.Append("\\f");
                    break;
                case '\n':
                    _ = builder.Append("\\n");
                    break;
                case '\r':
                    _ = builder.Append("\\r");
                    break;
                case '\t':
                    _ = builder.Append("\\t");
                    break;
                default:
                    // Everything else, including non-ASCII, stays literal: the canonical form
                    // is UTF-8 and escaping it would produce different bytes on each side.
                    _ = character < ' '
                        ? builder.Append(CultureInfo.InvariantCulture, $"\\u{(int)character:x4}")
                        : builder.Append(character);
                    break;
            }
        }

        _ = builder.Append('"');
    }
}
