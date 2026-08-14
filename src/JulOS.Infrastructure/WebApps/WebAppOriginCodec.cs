using System.Globalization;
using System.Text;

namespace JulOS.Infrastructure.WebApps;

/// <summary>
/// Reversibly encodes a target origin (scheme + host + port) into a single DNS label so an
/// arbitrary internal web application can be reached through the JulOS proxy at
/// <c>wa&lt;base32&gt;.&lt;proxy-zone&gt;</c> without any server-side state. A single label is used
/// deliberately: one wildcard DNS record and one wildcard TLS certificate cover exactly one label
/// (see <c>docs/WEB-APP-RENDERING.md</c>, decision D035).
/// </summary>
public static class WebAppOriginCodec
{
    /// <summary>Marker that tags a JulOS-encoded proxy label and its format version.</summary>
    public const string LabelMarker = "wa";

    /// <summary>The maximum length of one DNS label.</summary>
    private const int MaximumLabelLength = 63;

    private const byte HttpScheme = 0x00;
    private const byte HttpsScheme = 0x01;

    // RFC 4648 base32, lowercase, so the label survives DNS case-folding and stays a valid host.
    private const string Base32Alphabet = "abcdefghijklmnopqrstuvwxyz234567";

    /// <summary>Encodes an origin into a single proxy label, or <see langword="null"/> when it will not fit one label.</summary>
    /// <exception cref="ArgumentException">The URI carries a path, query or fragment; only an origin is encodable.</exception>
    public static string? EncodeLabel(Uri origin)
    {
        ArgumentNullException.ThrowIfNull(origin);
        if (!origin.IsAbsoluteUri
            || origin.Scheme is not ("http" or "https"))
        {
            throw new ArgumentException("Only an absolute HTTP or HTTPS origin can be encoded.", nameof(origin));
        }

        if ((origin.AbsolutePath is not ("" or "/"))
            || origin.Query.Length > 0
            || origin.Fragment.Length > 0)
        {
            throw new ArgumentException("Only an origin (no path, query or fragment) can be encoded.", nameof(origin));
        }

        // Deterministic ASCII host (bracketed literal for IPv6, punycode for IDN); the port is
        // always explicit so http://h and http://h:80 canonicalise to the same encoding.
        var host = origin.HostNameType == UriHostNameType.IPv6 ? origin.Host : origin.IdnHost;
        var authority = string.Create(
            CultureInfo.InvariantCulture,
            $"{host.ToLowerInvariant()}:{origin.Port}");
        var payload = new byte[1 + Encoding.UTF8.GetByteCount(authority)];
        payload[0] = string.Equals(origin.Scheme, "https", StringComparison.Ordinal) ? HttpsScheme : HttpScheme;
        Encoding.UTF8.GetBytes(authority, 0, authority.Length, payload, 1);

        var label = LabelMarker + Base32Encode(payload);
        return label.Length > MaximumLabelLength ? null : label;
    }

    /// <summary>Decodes a proxy label back to its origin, or <see langword="null"/> when it is not a valid JulOS proxy label.</summary>
    public static Uri? TryDecodeLabel(string? label)
    {
        if (string.IsNullOrEmpty(label)
            || label.Length <= LabelMarker.Length
            || !label.StartsWith(LabelMarker, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var payload = Base32Decode(label.AsSpan(LabelMarker.Length));
        if (payload is null || payload.Length < 2)
        {
            return null;
        }

        var scheme = payload[0] switch
        {
            HttpScheme => "http",
            HttpsScheme => "https",
            _ => null,
        };
        if (scheme is null)
        {
            return null;
        }

        var authority = Encoding.UTF8.GetString(payload, 1, payload.Length - 1);
        return Uri.TryCreate($"{scheme}://{authority}/", UriKind.Absolute, out var origin)
            && origin.Scheme == scheme
            && origin.AbsolutePath == "/"
            && origin.Query.Length == 0
            ? origin
            : null;
    }

    /// <summary>Encodes an origin into a full proxy host under the configured zone, or <see langword="null"/> when it does not fit.</summary>
    public static string? EncodeHost(Uri origin, string proxyZone)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(proxyZone);
        var label = EncodeLabel(origin);
        return label is null ? null : $"{label}.{proxyZone.Trim('.').ToLowerInvariant()}";
    }

    /// <summary>Decodes a full request host under the proxy zone to its target origin.</summary>
    /// <remarks>Requires exactly one leftmost label carrying the marker in front of the zone.</remarks>
    public static bool TryDecodeHost(string? requestHost, string proxyZone, out Uri upstream)
    {
        upstream = null!;
        if (string.IsNullOrWhiteSpace(requestHost) || string.IsNullOrWhiteSpace(proxyZone))
        {
            return false;
        }

        var host = requestHost.Trim().TrimEnd('.').ToLowerInvariant();
        var zone = proxyZone.Trim().Trim('.').ToLowerInvariant();
        var suffix = "." + zone;
        if (!host.EndsWith(suffix, StringComparison.Ordinal))
        {
            return false;
        }

        var label = host[..^suffix.Length];
        if (label.Length == 0 || label.Contains('.', StringComparison.Ordinal))
        {
            return false;
        }

        var origin = TryDecodeLabel(label);
        if (origin is null)
        {
            return false;
        }

        upstream = origin;
        return true;
    }

    private static string Base32Encode(ReadOnlySpan<byte> data)
    {
        var builder = new StringBuilder((data.Length * 8 + 4) / 5);
        var buffer = 0;
        var bits = 0;
        foreach (var value in data)
        {
            buffer = (buffer << 8) | value;
            bits += 8;
            while (bits >= 5)
            {
                bits -= 5;
                builder.Append(Base32Alphabet[(buffer >> bits) & 0x1F]);
            }
        }

        if (bits > 0)
        {
            builder.Append(Base32Alphabet[(buffer << (5 - bits)) & 0x1F]);
        }

        return builder.ToString();
    }

    private static byte[]? Base32Decode(ReadOnlySpan<char> text)
    {
        var output = new byte[text.Length * 5 / 8];
        var count = 0;
        var buffer = 0;
        var bits = 0;
        foreach (var character in text)
        {
            var index = Base32Alphabet.IndexOf(char.ToLowerInvariant(character));
            if (index < 0)
            {
                return null;
            }

            buffer = (buffer << 5) | index;
            bits += 5;
            if (bits >= 8)
            {
                bits -= 8;
                output[count++] = (byte)((buffer >> bits) & 0xFF);
            }
        }

        return count == output.Length ? output : output[..count];
    }
}
