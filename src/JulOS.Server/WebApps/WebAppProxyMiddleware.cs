using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;

using JulOS.Infrastructure.WebApps;

namespace JulOS.Server.WebApps;

/// <summary>
/// Transparently reverse-proxies a request whose host matches a configured local web-application
/// target, so the target renders in the user's own browser. Application URLs are not rewritten;
/// framing headers are stripped so the JulOS shell can embed the target. See
/// <c>docs/WEB-APP-RENDERING.md</c> and decision D035.
/// </summary>
internal sealed class WebAppProxyMiddleware
{
    /// <summary>The named <see cref="HttpClient"/> used for administrator-configured upstream forwarding.</summary>
    internal const string HttpClientName = "julos-webapp-proxy";

    /// <summary>The named <see cref="HttpClient"/> whose connections require prevalidated IP addresses.</summary>
    internal const string DynamicHttpClientName = "julos-webapp-dynamic-proxy";

    private const int WebSocketBufferSize = 16 * 1024;

    private static readonly HttpRequestOptionsKey<IPAddress[]> PinnedAddressesOption =
        new("JulOS.WebApps.PinnedAddresses");

    private static readonly Action<ILogger, Uri, Exception?> LogUpstreamHttpFailed =
        LoggerMessage.Define<Uri>(
            LogLevel.Warning,
            new EventId(1500, nameof(LogUpstreamHttpFailed)),
            "Web application upstream HTTP request failed for {Upstream}.");

    private static readonly Action<ILogger, Uri, Exception?> LogUpstreamWebSocketFailed =
        LoggerMessage.Define<Uri>(
            LogLevel.Warning,
            new EventId(1501, nameof(LogUpstreamWebSocketFailed)),
            "Web application upstream WebSocket connection failed for {Upstream}.");

    private static readonly Action<ILogger, Uri, Exception?> LogUpstreamResolutionFailed =
        LoggerMessage.Define<Uri>(
            LogLevel.Warning,
            new EventId(1502, nameof(LogUpstreamResolutionFailed)),
            "Web application upstream DNS resolution failed for {Upstream}.");

    private readonly RequestDelegate next;
    private readonly WebAppTargetRegistry registry;
    private readonly WebAppProxyOptions options;
    private readonly IHttpClientFactory httpClientFactory;
    private readonly ILogger<WebAppProxyMiddleware> logger;

    public WebAppProxyMiddleware(
        RequestDelegate next,
        WebAppTargetRegistry registry,
        WebAppProxyOptions options,
        IHttpClientFactory httpClientFactory,
        ILogger<WebAppProxyMiddleware> logger)
    {
        this.next = next;
        this.registry = registry;
        this.options = options;
        this.httpClientFactory = httpClientFactory;
        this.logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!this.registry.TryResolve(context.Request.Host.Host, out var target))
        {
            await this.next(context).ConfigureAwait(false);
            return;
        }

        if (context.User.Identity?.IsAuthenticated != true)
        {
            await WriteFailureAsync(
                context,
                StatusCodes.Status401Unauthorized,
                "webapp.authentication_required",
                "A JulOS session is required to open a web application.").ConfigureAwait(false);
            return;
        }

        var pinnedAddresses = Array.Empty<IPAddress>();
        if (target.RequiresAddressPinning)
        {
            try
            {
                pinnedAddresses = await this.registry
                    .ResolveAllowedAddressesAsync(target, context.RequestAborted)
                    .ConfigureAwait(false);
            }
            catch (SocketException exception)
            {
                LogUpstreamResolutionFailed(this.logger, target.Upstream, exception);
                await WriteFailureAsync(
                    context,
                    StatusCodes.Status502BadGateway,
                    "webapp.upstream_unavailable",
                    "The web application is unavailable.").ConfigureAwait(false);
                return;
            }

            if (pinnedAddresses.Length == 0)
            {
                await WriteFailureAsync(
                    context,
                    StatusCodes.Status403Forbidden,
                    "webapp.target_not_allowed",
                    "The web application target is outside the configured network allowlist.").ConfigureAwait(false);
                return;
            }
        }

        if (context.WebSockets.IsWebSocketRequest)
        {
            await this.ProxyWebSocketAsync(context, target, pinnedAddresses).ConfigureAwait(false);
            return;
        }

        await this.ProxyHttpAsync(context, target, pinnedAddresses).ConfigureAwait(false);
    }

    private async Task ProxyHttpAsync(
        HttpContext context,
        WebAppTarget target,
        IPAddress[] pinnedAddresses)
    {
        var request = context.Request;
        using var upstreamRequest = new HttpRequestMessage(
            new HttpMethod(request.Method),
            BuildUpstreamUri(target.Upstream, request, request.Scheme));

        if (target.RequiresAddressPinning)
        {
            upstreamRequest.Options.Set(PinnedAddressesOption, pinnedAddresses);
        }

        var hasBody = !HttpMethods.IsGet(request.Method)
            && !HttpMethods.IsHead(request.Method)
            && request.ContentLength != 0;
        if (hasBody)
        {
            upstreamRequest.Content = new StreamContent(request.Body);
        }

        foreach (var header in request.Headers)
        {
            if (string.Equals(header.Key, "Cookie", StringComparison.OrdinalIgnoreCase))
            {
                var forwardedCookies = WebAppResponsePolicy.FilterForwardedCookies(header.Value.ToString());
                if (forwardedCookies is not null)
                {
                    upstreamRequest.Headers.TryAddWithoutValidation("Cookie", forwardedCookies);
                }

                continue;
            }

            if (IsSuppressedRequestHeader(header.Key))
            {
                continue;
            }

            if (!upstreamRequest.Headers.TryAddWithoutValidation(header.Key, (IEnumerable<string?>)header.Value))
            {
                upstreamRequest.Content?.Headers.TryAddWithoutValidation(header.Key, (IEnumerable<string?>)header.Value);
            }
        }

        upstreamRequest.Headers.Host = target.Upstream.Authority;
        upstreamRequest.Headers.TryAddWithoutValidation("X-Forwarded-Host", context.Request.Host.Value);
        upstreamRequest.Headers.TryAddWithoutValidation("X-Forwarded-Proto", context.Request.Scheme);

        var client = this.httpClientFactory.CreateClient(
            target.RequiresAddressPinning ? DynamicHttpClientName : HttpClientName);
        HttpResponseMessage upstreamResponse;
        try
        {
            upstreamResponse = await client.SendAsync(
                upstreamRequest,
                HttpCompletionOption.ResponseHeadersRead,
                context.RequestAborted).ConfigureAwait(false);
        }
        catch (HttpRequestException exception)
        {
            LogUpstreamHttpFailed(this.logger, target.Upstream, exception);
            await WriteFailureAsync(
                context,
                StatusCodes.Status502BadGateway,
                "webapp.upstream_unavailable",
                "The web application is unavailable.").ConfigureAwait(false);
            return;
        }

        using (upstreamResponse)
        {
            context.Response.StatusCode = (int)upstreamResponse.StatusCode;
            CopyResponseHeaders(
                upstreamResponse,
                context.Response,
                new WebAppResponseContext(
                    target.Upstream,
                    context.Request.Scheme,
                    context.Request.Host.Value ?? string.Empty,
                    context.Request.IsHttps));
            await upstreamResponse.Content
                .CopyToAsync(context.Response.Body, context.RequestAborted)
                .ConfigureAwait(false);
        }
    }

    private async Task ProxyWebSocketAsync(
        HttpContext context,
        WebAppTarget target,
        IPAddress[] pinnedAddresses)
    {
        var upstreamUri = BuildUpstreamUri(
            target.Upstream,
            context.Request,
            context.Request.Scheme,
            forWebSocket: true);

        using var upstream = new ClientWebSocket();
        foreach (var protocol in context.WebSockets.WebSocketRequestedProtocols)
        {
            upstream.Options.AddSubProtocol(protocol);
        }

        var cookie = WebAppResponsePolicy.FilterForwardedCookies(context.Request.Headers.Cookie.ToString());
        if (cookie is not null)
        {
            upstream.Options.SetRequestHeader("Cookie", cookie);
        }

        try
        {
            if (target.RequiresAddressPinning)
            {
                var handler = CreateHttpHandler(this.options, requirePinnedAddresses: false);
                handler.ConnectCallback = (connectionContext, cancellationToken) =>
                    ConnectPinnedAsync(
                        pinnedAddresses,
                        connectionContext.DnsEndPoint.Port,
                        cancellationToken);
                using var invoker = new HttpMessageInvoker(handler);
                await upstream.ConnectAsync(upstreamUri, invoker, context.RequestAborted).ConfigureAwait(false);
            }
            else
            {
                upstream.Options.RemoteCertificateValidationCallback = (_, _, _, errors) =>
                    this.options.UpstreamCertificateIsAcceptable(errors);
                await upstream.ConnectAsync(upstreamUri, context.RequestAborted).ConfigureAwait(false);
            }
        }
        catch (Exception exception) when (exception is WebSocketException or HttpRequestException)
        {
            LogUpstreamWebSocketFailed(this.logger, target.Upstream, exception);
            await WriteFailureAsync(
                context,
                StatusCodes.Status502BadGateway,
                "webapp.upstream_unavailable",
                "The web application is unavailable.").ConfigureAwait(false);
            return;
        }

        using var browser = await context.WebSockets
            .AcceptWebSocketAsync(upstream.SubProtocol)
            .ConfigureAwait(false);
        await PumpWebSocketAsync(browser, upstream, context.RequestAborted).ConfigureAwait(false);
    }

    /// <summary>Creates the common upstream handler, optionally requiring a pinned address option.</summary>
    internal static SocketsHttpHandler CreateHttpHandler(
        WebAppProxyOptions options,
        bool requirePinnedAddresses)
    {
        ArgumentNullException.ThrowIfNull(options);
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.None,
            UseCookies = false,
            SslOptions =
            {
                RemoteCertificateValidationCallback = (_, _, _, errors) =>
                    options.UpstreamCertificateIsAcceptable(errors),
            },
        };

        if (requirePinnedAddresses)
        {
            handler.ConnectCallback = ConnectPinnedRequestAsync;
        }

        return handler;
    }

    private static ValueTask<Stream> ConnectPinnedRequestAsync(
        SocketsHttpConnectionContext context,
        CancellationToken cancellationToken)
    {
        if (!context.InitialRequestMessage.Options.TryGetValue(PinnedAddressesOption, out var addresses)
            || addresses.Length == 0)
        {
            return ValueTask.FromException<Stream>(
                new HttpRequestException("Dynamic web application connection has no validated upstream address."));
        }

        return ConnectPinnedAsync(addresses, context.DnsEndPoint.Port, cancellationToken);
    }

    private static async ValueTask<Stream> ConnectPinnedAsync(
        IPAddress[] addresses,
        int port,
        CancellationToken cancellationToken)
    {
        Exception? lastFailure = null;
        foreach (var address in addresses)
        {
            var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp)
            {
                NoDelay = true,
            };
            try
            {
                await socket.ConnectAsync(new IPEndPoint(address, port), cancellationToken).ConfigureAwait(false);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch (OperationCanceledException)
            {
                socket.Dispose();
                throw;
            }
            catch (SocketException exception)
            {
                lastFailure = exception;
                socket.Dispose();
            }
        }

        throw new HttpRequestException("No validated web application upstream address accepted the connection.", lastFailure);
    }

    private static Uri BuildUpstreamUri(
        Uri upstream,
        HttpRequest request,
        string requestScheme,
        bool forWebSocket = false)
    {
        _ = requestScheme;
        var builder = new UriBuilder(upstream)
        {
            Path = request.Path.Value ?? "/",
            Query = request.QueryString.Value ?? string.Empty,
        };

        if (forWebSocket)
        {
            builder.Scheme = string.Equals(upstream.Scheme, "https", StringComparison.OrdinalIgnoreCase)
                ? "wss"
                : "ws";
        }

        return builder.Uri;
    }

    private static void CopyResponseHeaders(
        HttpResponseMessage upstreamResponse,
        HttpResponse response,
        WebAppResponseContext context)
    {
        foreach (var header in upstreamResponse.Headers)
        {
            CopyResponseHeader(response, header.Key, header.Value, context);
        }

        foreach (var header in upstreamResponse.Content.Headers)
        {
            CopyResponseHeader(response, header.Key, header.Value, context);
        }
    }

    private static void CopyResponseHeader(
        HttpResponse response,
        string name,
        IEnumerable<string> values,
        WebAppResponseContext context)
    {
        if (WebAppResponsePolicy.IsSuppressedResponseHeader(name))
        {
            return;
        }

        if (string.Equals(name, "Content-Security-Policy", StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, "Content-Security-Policy-Report-Only", StringComparison.OrdinalIgnoreCase))
        {
            var rewritten = WebAppResponsePolicy.RewriteContentSecurityPolicy(string.Join(", ", values));
            if (!string.IsNullOrEmpty(rewritten))
            {
                response.Headers[name] = rewritten;
            }

            return;
        }

        if (string.Equals(name, "Set-Cookie", StringComparison.OrdinalIgnoreCase))
        {
            response.Headers.SetCookie = values
                .Select(value => WebAppResponsePolicy.RewriteSetCookie(value, context.RequestIsHttps))
                .ToArray();
            return;
        }

        if (string.Equals(name, "Location", StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, "Content-Location", StringComparison.OrdinalIgnoreCase))
        {
            response.Headers[name] = values
                .Select(value => WebAppResponsePolicy.RewriteRedirect(
                    value,
                    context.Upstream,
                    context.RequestScheme,
                    context.RequestHost))
                .ToArray();
            return;
        }

        response.Headers[name] = values.ToArray();
    }

    private readonly record struct WebAppResponseContext(
        Uri Upstream,
        string RequestScheme,
        string RequestHost,
        bool RequestIsHttps);

    private static async Task PumpWebSocketAsync(
        WebSocket browser,
        WebSocket upstream,
        CancellationToken cancellationToken)
    {
        using var pump = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var browserToUpstream = ForwardWebSocketAsync(browser, upstream, pump.Token);
        var upstreamToBrowser = ForwardWebSocketAsync(upstream, browser, pump.Token);

        _ = await Task.WhenAny(browserToUpstream, upstreamToBrowser).ConfigureAwait(false);
        await pump.CancelAsync().ConfigureAwait(false);

        try
        {
            await Task.WhenAll(browserToUpstream, upstreamToBrowser).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (pump.IsCancellationRequested)
        {
        }
        catch (WebSocketException)
        {
            browser.Abort();
            upstream.Abort();
        }
    }

    private static async Task ForwardWebSocketAsync(
        WebSocket source,
        WebSocket destination,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[WebSocketBufferSize];
        while (!cancellationToken.IsCancellationRequested
            && source.State is WebSocketState.Open or WebSocketState.CloseSent
            && destination.State is WebSocketState.Open or WebSocketState.CloseReceived)
        {
            var result = await source.ReceiveAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                if (destination.State == WebSocketState.Open)
                {
                    await destination.CloseOutputAsync(
                        result.CloseStatus ?? WebSocketCloseStatus.NormalClosure,
                        result.CloseStatusDescription,
                        cancellationToken).ConfigureAwait(false);
                }

                return;
            }

            await destination.SendAsync(
                buffer.AsMemory(0, result.Count),
                result.MessageType,
                result.EndOfMessage,
                cancellationToken).ConfigureAwait(false);
        }
    }

    private static bool IsSuppressedRequestHeader(string name) =>
        WebAppResponsePolicy.IsSuppressedResponseHeader(name)
        || string.Equals(name, "Host", StringComparison.OrdinalIgnoreCase)
        || string.Equals(name, "Authorization", StringComparison.OrdinalIgnoreCase)
        || string.Equals(name, "Forwarded", StringComparison.OrdinalIgnoreCase)
        || name.StartsWith("X-Forwarded-", StringComparison.OrdinalIgnoreCase);

    private static async Task WriteFailureAsync(HttpContext context, int status, string code, string detail)
    {
        context.Response.StatusCode = status;
        await context.Response.WriteAsJsonAsync(new { code, detail }).ConfigureAwait(false);
    }
}
