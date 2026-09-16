using System.Formats.Tar;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using JulOS.Application.Catalog;
using JulOS.Application.Secrets;
using JulOS.Domain.Catalog;

namespace JulOS.Infrastructure.Catalog;

/// <summary>
/// Reads a catalog published as an artifact in an OCI registry.
/// </summary>
/// <remarks>
/// <para>
/// A registry reference is the one source kind that names an immutable identity directly.
/// The manifest digest covers every layer, and each layer descriptor covers its own blob, so
/// resolving a tag once and then working from the digest is what locks a whole refresh to one
/// state of the source. A tag is accepted as input and resolved before anything is read.
/// </para>
/// <para>
/// The bundle is materialized once into memory rather than re-fetched per file, because every
/// file the index names is going to be read anyway and a registry blob is not seekable. Total
/// and per-file sizes are bounded, so a registry cannot turn one refresh into unbounded memory
/// on this host.
/// </para>
/// </remarks>
internal sealed class OciCatalogSourceReader : ICatalogSourceReader
{
    /// <summary>The largest bundle this build will materialize for one source.</summary>
    private const long MaximumBundleBytes = 64L * 1024 * 1024;

    /// <summary>The only manifest media type a JulOS catalog artifact is accepted as.</summary>
    /// <remarks>
    /// A catalog artifact is something a publisher builds for JulOS, so requiring the OCI
    /// media type costs a publisher nothing and keeps a specific container product out of
    /// Core, which the architecture rules require.
    /// </remarks>
    private const string ManifestMediaType = "application/vnd.oci.image.manifest.v1+json";

    private readonly HttpClient client;

    /// <summary>Creates a reader over one long-lived client.</summary>
    /// <param name="client">A client configured for catalog reads.</param>
    public OciCatalogSourceReader(HttpClient client) =>
        this.client = client ?? throw new ArgumentNullException(nameof(client));

    public CatalogSourceKind Kind => CatalogSourceKind.Oci;

    public async Task<CatalogSourceSnapshot> OpenAsync(
        CatalogSourceLocation location,
        SecretLease? credential,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(location);

        var reference = OciReference.Parse(location.Location);
        var token = credential is null ? null : Encoding.UTF8.GetString(credential.Value.Span);

        var manifest = await this
            .GetAsync(reference.ManifestUrl, token, Accept(), cancellationToken)
            .ConfigureAwait(false);
        var digest = $"sha256:{Convert.ToHexStringLower(SHA256.HashData(manifest))}";

        if (reference.Digest is string pinned && !string.Equals(pinned, digest, StringComparison.Ordinal))
        {
            // A registry that answers a digest request with other bytes is not a registry
            // this refresh can lock to.
            throw new CatalogRefreshException(
                CatalogRefreshException.IntegrityMismatch,
                "The registry returned a manifest that does not match the requested digest.");
        }

        var files = await this
            .MaterializeAsync(reference, token, manifest, cancellationToken)
            .ConfigureAwait(false);

        return new Snapshot(digest, files);
    }

    /// <summary>Downloads every layer and flattens them into one file map.</summary>
    /// <remarks>
    /// Later layers win, which is the ordinary OCI layering rule. Whiteout entries are not
    /// interpreted: a catalog artifact that needs them is building something this reader does
    /// not claim to understand, and guessing would silently serve the wrong bytes.
    /// </remarks>
    private async Task<Dictionary<string, byte[]>> MaterializeAsync(
        OciReference reference,
        string? token,
        byte[] manifest,
        CancellationToken cancellationToken)
    {
        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        long total = 0;

        foreach (var layer in ReadLayerDigests(manifest))
        {
            var blob = await this
                .GetAsync(reference.BlobUrl(layer), token, null, cancellationToken)
                .ConfigureAwait(false);

            var actual = $"sha256:{Convert.ToHexStringLower(SHA256.HashData(blob))}";
            if (!string.Equals(actual, layer, StringComparison.Ordinal))
            {
                throw new CatalogRefreshException(
                    CatalogRefreshException.IntegrityMismatch,
                    "A catalog layer does not match the digest its manifest declares for it.");
            }

            total += Extract(blob, files, total);
        }

        return files;
    }

    private static long Extract(byte[] blob, Dictionary<string, byte[]> files, long alreadyRead)
    {
        using var compressed = new MemoryStream(blob, writable: false);
        using var stream = IsGzip(blob) ? new GZipStream(compressed, CompressionMode.Decompress) : (Stream)compressed;
        using var archive = new TarReader(stream);

        long read = 0;
        while (archive.GetNextEntry() is TarEntry entry)
        {
            if (entry.EntryType is not (TarEntryType.RegularFile or TarEntryType.V7RegularFile)
                || entry.DataStream is null)
            {
                continue;
            }

            if (entry.Length > CatalogSourceSnapshot.MaximumFileBytes)
            {
                throw new CatalogRefreshException(
                    CatalogRefreshException.SourceUnavailable,
                    $"A catalog file is larger than {CatalogSourceSnapshot.MaximumFileBytes} bytes.");
            }

            read += entry.Length;
            if (alreadyRead + read > MaximumBundleBytes)
            {
                throw new CatalogRefreshException(
                    CatalogRefreshException.SourceUnavailable,
                    $"The catalog artifact is larger than {MaximumBundleBytes} bytes.");
            }

            using var buffer = new MemoryStream();
            entry.DataStream.CopyTo(buffer);
            files[Normalize(entry.Name)] = buffer.ToArray();
        }

        return read;
    }

    /// <summary>Strips the leading path forms a tar entry may use.</summary>
    private static string Normalize(string name) =>
        name.StartsWith("./", StringComparison.Ordinal) ? name[2..] : name.TrimStart('/');

    private static bool IsGzip(byte[] blob) => blob.Length > 2 && blob[0] == 0x1F && blob[1] == 0x8B;

    private static string[] ReadLayerDigests(byte[] manifest)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(manifest);
        }
        catch (JsonException exception)
        {
            throw new CatalogRefreshException(
                CatalogRefreshException.SourceUnavailable,
                "The registry returned a manifest that is not valid JSON.",
                exception);
        }

        using (document)
        {
            if (!document.RootElement.TryGetProperty("layers", out var layers)
                || layers.ValueKind != JsonValueKind.Array)
            {
                throw new CatalogRefreshException(
                    CatalogRefreshException.SourceUnavailable,
                    "The catalog manifest declares no layers.");
            }

            return layers
                .EnumerateArray()
                .Select(layer => layer.TryGetProperty("digest", out var digest) ? digest.GetString() : null)
                .Where(digest => digest is not null)
                .Select(digest => digest!)
                .ToArray();
        }
    }

    private static MediaTypeWithQualityHeaderValue[] Accept() => [new(ManifestMediaType)];

    /// <summary>
    /// Performs one registry GET, completing the registry token handshake when asked to.
    /// </summary>
    /// <remarks>
    /// A registry answers an unauthenticated request with the realm to get a token from, and
    /// that exchange is how anonymous access to a public repository works as well. The
    /// configured secret, when there is one, is only ever presented to that realm as basic
    /// credentials and never to an arbitrary host the response names.
    /// </remarks>
    private async Task<byte[]> GetAsync(
        Uri url,
        string? token,
        MediaTypeWithQualityHeaderValue[]? accept,
        CancellationToken cancellationToken)
    {
        using var first = await this.SendAsync(url, accept, null, cancellationToken).ConfigureAwait(false);
        if (first.StatusCode == HttpStatusCode.OK)
        {
            return await ReadBoundedAsync(first, cancellationToken).ConfigureAwait(false);
        }

        if (first.StatusCode != HttpStatusCode.Unauthorized
            || first.Headers.WwwAuthenticate.FirstOrDefault() is not { } challenge
            || !string.Equals(challenge.Scheme, "Bearer", StringComparison.OrdinalIgnoreCase))
        {
            throw Refuse(first.StatusCode);
        }

        var bearer = await this
            .ExchangeAsync(challenge.Parameter, url, token, cancellationToken)
            .ConfigureAwait(false);

        using var second = await this.SendAsync(url, accept, bearer, cancellationToken).ConfigureAwait(false);
        return second.StatusCode == HttpStatusCode.OK
            ? await ReadBoundedAsync(second, cancellationToken).ConfigureAwait(false)
            : throw Refuse(second.StatusCode);
    }

    private async Task<string> ExchangeAsync(
        string? challengeParameter,
        Uri requested,
        string? token,
        CancellationToken cancellationToken)
    {
        var parameters = ParseChallenge(challengeParameter);
        if (!parameters.TryGetValue("realm", out var realm)
            || !Uri.TryCreate(realm, UriKind.Absolute, out var realmUrl)
            || realmUrl.Scheme != Uri.UriSchemeHttps)
        {
            throw new CatalogRefreshException(
                CatalogRefreshException.SourceUnavailable,
                "The registry named no usable authentication realm.");
        }

        if (!string.Equals(realmUrl.Host, requested.Host, StringComparison.OrdinalIgnoreCase)
            && token is not null)
        {
            // A registry may delegate tokens to a separate host, but presenting the
            // configured credential to a host the response chose is how a compromised
            // registry harvests it. The exchange proceeds anonymously instead.
            token = null;
        }

        var query = new List<string>();
        if (parameters.TryGetValue("service", out var service))
        {
            query.Add($"service={Uri.EscapeDataString(service)}");
        }

        if (parameters.TryGetValue("scope", out var scope))
        {
            query.Add($"scope={Uri.EscapeDataString(scope)}");
        }

        var url = new UriBuilder(realmUrl) { Query = string.Join('&', query) }.Uri;
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        using var response = await this.client.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode != HttpStatusCode.OK)
        {
            throw Refuse(response.StatusCode);
        }

        using var document = JsonDocument.Parse(
            await ReadBoundedAsync(response, cancellationToken).ConfigureAwait(false));
        foreach (var name in new[] { "token", "access_token" })
        {
            if (document.RootElement.TryGetProperty(name, out var value)
                && value.GetString() is { Length: > 0 } issued)
            {
                return issued;
            }
        }

        throw new CatalogRefreshException(
            CatalogRefreshException.SourceUnavailable,
            "The registry issued no access token.");
    }

    private Task<HttpResponseMessage> SendAsync(
        Uri url,
        MediaTypeWithQualityHeaderValue[]? accept,
        string? bearer,
        CancellationToken cancellationToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        foreach (var media in accept ?? [])
        {
            request.Headers.Accept.Add(media);
        }

        if (bearer is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        }

        return this.client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
    }

    private static Dictionary<string, string> ParseChallenge(string? parameter)
    {
        var parsed = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var part in (parameter ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = part.IndexOf('=', StringComparison.Ordinal);
            if (separator > 0)
            {
                parsed[part[..separator].Trim()] = part[(separator + 1)..].Trim().Trim('"');
            }
        }

        return parsed;
    }

    private static async Task<byte[]> ReadBoundedAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (response.Content.Headers.ContentLength > MaximumBundleBytes)
        {
            throw new CatalogRefreshException(
                CatalogRefreshException.SourceUnavailable,
                $"A registry response is larger than {MaximumBundleBytes} bytes.");
        }

        await using var stream = await response.Content
            .ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);
        using var buffer = new MemoryStream();
        var chunk = new byte[64 * 1024];

        while (true)
        {
            var read = await stream.ReadAsync(chunk, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                return buffer.ToArray();
            }

            if (buffer.Length + read > MaximumBundleBytes)
            {
                throw new CatalogRefreshException(
                    CatalogRefreshException.SourceUnavailable,
                    $"A registry response is larger than {MaximumBundleBytes} bytes.");
            }

            buffer.Write(chunk, 0, read);
        }
    }

    // The registry host may be private; only the status reaches the message.
    private static CatalogRefreshException Refuse(HttpStatusCode status) => new(
        CatalogRefreshException.SourceUnavailable,
        $"The catalog registry returned status {(int)status}.");

    /// <summary>One parsed registry reference.</summary>
    private sealed record OciReference(string Registry, string Repository, string Reference, string? Digest)
    {
        internal Uri ManifestUrl => new($"https://{this.Registry}/v2/{this.Repository}/manifests/{this.Reference}");

        internal Uri BlobUrl(string digest) =>
            new($"https://{this.Registry}/v2/{this.Repository}/blobs/{digest}");

        /// <summary>Parses <c>oci://registry/repository:tag</c> or the same with a digest.</summary>
        internal static OciReference Parse(string location)
        {
            var text = location.StartsWith("oci://", StringComparison.Ordinal) ? location[6..] : location;
            var slash = text.IndexOf('/', StringComparison.Ordinal);
            if (slash <= 0 || slash == text.Length - 1)
            {
                throw Invalid();
            }

            var registry = text[..slash];
            var rest = text[(slash + 1)..];

            var at = rest.IndexOf('@', StringComparison.Ordinal);
            if (at > 0)
            {
                var digest = rest[(at + 1)..];
                return digest.StartsWith("sha256:", StringComparison.Ordinal)
                    ? new OciReference(registry, rest[..at], digest, digest)
                    : throw Invalid();
            }

            var colon = rest.LastIndexOf(':');
            return colon > 0 && colon < rest.Length - 1
                ? new OciReference(registry, rest[..colon], rest[(colon + 1)..], null)
                : throw Invalid();
        }

        private static CatalogRefreshException Invalid() => new(
            CatalogRefreshException.SourceUnavailable,
            "An OCI catalog source is 'oci://registry/repository:tag' or the same with a digest.");
    }

    private sealed class Snapshot(string digest, Dictionary<string, byte[]> files) : CatalogSourceSnapshot
    {
        public override string? NativeContentIdentity => digest;

        public override Task<byte[]?> TryReadAsync(
            string relativePath,
            CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
            cancellationToken.ThrowIfCancellationRequested();

            return Task.FromResult(files.TryGetValue(relativePath, out var bytes) ? bytes : null);
        }
    }
}
