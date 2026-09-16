using System.Net;
using System.Net.Http.Headers;
using System.Text;

using JulOS.Application.Catalog;
using JulOS.Application.Secrets;
using JulOS.Domain.Catalog;

namespace JulOS.Infrastructure.Catalog;

/// <summary>
/// Reads a static versioned catalog served over HTTPS.
/// </summary>
/// <remarks>
/// <para>
/// A private source authenticates with one opaque bearer token taken from its secret
/// reference. The secret store holds a single value per reference, so a single-value scheme
/// is the one that fits it; the token is read out of the lease for each request and never
/// copied into a field, a URL or a log.
/// </para>
/// <para>
/// The transport says nothing about trust. Reaching a catalog over TLS proves who served the
/// bytes, not who produced the definitions, which is why every entry is still checked
/// against the digest the index declares and every signature against a key an administrator
/// decided about.
/// </para>
/// </remarks>
internal sealed class HttpsCatalogSourceReader : ICatalogSourceReader
{
    private readonly HttpClient client;

    /// <summary>Creates a reader over one long-lived client.</summary>
    /// <param name="client">A client configured for catalog reads.</param>
    public HttpsCatalogSourceReader(HttpClient client) =>
        this.client = client ?? throw new ArgumentNullException(nameof(client));

    /// <summary>Builds the client a deployment uses to read catalogs.</summary>
    /// <remarks>
    /// Redirects are not followed: a redirect would let a source move a catalog read to a
    /// host the administrator never configured. Connections are recycled so a source that
    /// changes address is not pinned to a stale one for the process lifetime.
    /// </remarks>
    internal static HttpClient CreateClient() => new(
        new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        })
    {
        Timeout = TimeSpan.FromSeconds(30),
    };

    public CatalogSourceKind Kind => CatalogSourceKind.Https;

    public Task<CatalogSourceSnapshot> OpenAsync(
        CatalogSourceLocation location,
        SecretLease? credential,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(location);
        cancellationToken.ThrowIfCancellationRequested();

        if (!Uri.TryCreate(EnsureTrailingSlash(location.Location), UriKind.Absolute, out var root)
            || root.Scheme != Uri.UriSchemeHttps)
        {
            // Plain HTTP is refused rather than downgraded to: a catalog read over it can be
            // rewritten in flight by anything on the path.
            throw new CatalogRefreshException(
                CatalogRefreshException.SourceUnavailable,
                "An HTTPS catalog source is an absolute https:// URL.");
        }

        return Task.FromResult<CatalogSourceSnapshot>(
            new Snapshot(this.client, root, credential));
    }

    private static string EnsureTrailingSlash(string location) =>
        location.EndsWith('/') ? location : location + "/";

    private sealed class Snapshot(HttpClient client, Uri root, SecretLease? credential) : CatalogSourceSnapshot
    {
        public override string? NativeContentIdentity => null;

        public override async Task<byte[]?> TryReadAsync(
            string relativePath,
            CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);

            // Resolving against the root and re-checking containment means a path the index
            // parser somehow let through still cannot leave the configured prefix.
            var target = new Uri(root, relativePath);
            if (!root.IsBaseOf(target))
            {
                throw Refuse(relativePath, "resolves outside the catalog root");
            }

            using var request = new HttpRequestMessage(HttpMethod.Get, target);
            if (credential is not null)
            {
                request.Headers.Authorization = new AuthenticationHeaderValue(
                    "Bearer",
                    Encoding.UTF8.GetString(credential.Value.Span));
            }

            HttpResponseMessage response;
            try
            {
                response = await client
                    .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (HttpRequestException exception)
            {
                throw new CatalogRefreshException(
                    CatalogRefreshException.SourceUnavailable,
                    $"The catalog file '{relativePath}' could not be fetched.",
                    exception);
            }

            using (response)
            {
                if (response.StatusCode == HttpStatusCode.NotFound)
                {
                    return null;
                }

                if (response.StatusCode != HttpStatusCode.OK)
                {
                    throw Refuse(relativePath, $"returned status {(int)response.StatusCode}");
                }

                if (response.Content.Headers.ContentLength > MaximumFileBytes)
                {
                    throw Refuse(relativePath, $"is larger than {MaximumFileBytes} bytes");
                }

                return await ReadBoundedAsync(response, relativePath, cancellationToken).ConfigureAwait(false);
            }
        }

        /// <summary>Reads at most one file's worth of bytes, whatever the source claims.</summary>
        /// <remarks>
        /// A missing or untruthful <c>Content-Length</c> must not turn a remote source into
        /// unbounded memory use on this host, so the limit is enforced while reading too.
        /// </remarks>
        private static async Task<byte[]?> ReadBoundedAsync(
            HttpResponseMessage response,
            string relativePath,
            CancellationToken cancellationToken)
        {
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

                if (buffer.Length + read > MaximumFileBytes)
                {
                    throw Refuse(relativePath, $"is larger than {MaximumFileBytes} bytes");
                }

                buffer.Write(chunk, 0, read);
            }
        }

        // The declared path is catalog content; the resolved URL may carry a private host
        // name and stays out of the message.
        private static CatalogRefreshException Refuse(string relativePath, string what) => new(
            CatalogRefreshException.SourceUnavailable,
            $"The catalog file '{relativePath}' {what}.");
    }
}
