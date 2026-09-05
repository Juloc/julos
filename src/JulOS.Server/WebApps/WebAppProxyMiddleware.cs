using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;

using JulOS.Infrastructure.WebApps;
using JulOS.Server.Authorization;

using Microsoft.AspNetCore.Authorization;

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
    private const int MaximumInternalNavigationRedirects = 5;

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

    private static readonly Action<ILogger, int, string, string, Exception?> LogUpstreamRedirect =
        LoggerMessage.Define<int, string, string>(
            LogLevel.Information,
            new EventId(1503, nameof(LogUpstreamRedirect)),
            "Web application upstream redirect {StatusCode} from {Source} to {Target}.");

    private static readonly Action<ILogger, int, string, Exception?> LogProxyRedirect =
        LoggerMessage.Define<int, string>(
            LogLevel.Information,
            new EventId(1504, nameof(LogProxyRedirect)),
            "Web application proxy returns redirect {StatusCode} to {Target}.");

    private static readonly Action<ILogger, string, string, Exception?> LogInternalNavigationRedirect =
        LoggerMessage.Define<string, string>(
            LogLevel.Information,
            new EventId(1505, nameof(LogInternalNavigationRedirect)),
            "Web application proxy follows same-origin navigation redirect from {Source} to {Target}.");

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

    public async Task InvokeAsync(HttpContext context, IAuthorizationService authorizationService)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(authorizationService);

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

        // Reaching a configured or dynamically allowlisted target can expose internal
        // infrastructure, so authentication alone is not enough: the caller must hold the
        // web-application permission, which the Administrator role has by default.
        var authorization = await authorizationService
            .AuthorizeAsync(context.User, JulOsAuthorizationPolicies.WebAppUse)
            .ConfigureAwait(false);
        if (!authorization.Succeeded)
        {
            await WriteFailureAsync(
                context,
                StatusCodes.Status403Forbidden,
                "webapp.not_authorized",
                "This account is not permitted to open web applications.").ConfigureAwait(false);
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
        var publicRequestScheme = GetPublicRequestScheme(request);
        var browserDocumentNavigation = IsBrowserDocumentNavigation(request);
        var browserNavigation = IsBrowserNavigation(request);
        var upstreamRequest = new HttpRequestMessage(
            new HttpMethod(request.Method),
            BuildUpstreamUri(target.Upstream, request, publicRequestScheme));
        var browserRequestOrigin = request.Headers.Origin.ToString();
        string? upstreamRequestOrigin = null;

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

            if (target.RequiresAddressPinning
                && string.Equals(header.Key, "Accept-Encoding", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (target.RequiresAddressPinning
                && string.Equals(header.Key, "Origin", StringComparison.OrdinalIgnoreCase)
                && TryVirtualizeProxyRequestUrl(header.Value.ToString(), this.registry.DynamicProxyZone, true, out var virtualizedOrigin))
            {
                upstreamRequestOrigin = virtualizedOrigin;
                upstreamRequest.Headers.TryAddWithoutValidation(header.Key, virtualizedOrigin);
                continue;
            }

            if (target.RequiresAddressPinning
                && string.Equals(header.Key, "Referer", StringComparison.OrdinalIgnoreCase)
                && TryVirtualizeProxyRequestUrl(header.Value.ToString(), this.registry.DynamicProxyZone, false, out var virtualizedReferer))
            {
                upstreamRequest.Headers.TryAddWithoutValidation(header.Key, virtualizedReferer);
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
        if (target.RequiresAddressPinning)
        {
            upstreamRequest.Headers.TryAddWithoutValidation("Accept-Encoding", "identity");
        }

        if (browserDocumentNavigation)
        {
            upstreamRequest.Headers.TryAddWithoutValidation("Sec-Fetch-Dest", "document");
            upstreamRequest.Headers.TryAddWithoutValidation("Sec-Fetch-Mode", "navigate");
            upstreamRequest.Headers.TryAddWithoutValidation("Sec-Fetch-Site", "none");
            upstreamRequest.Headers.TryAddWithoutValidation("Sec-Fetch-User", "?1");
        }

        if (!target.RequiresAddressPinning)
        {
            upstreamRequest.Headers.TryAddWithoutValidation("X-Forwarded-Host", context.Request.Host.Value);
            upstreamRequest.Headers.TryAddWithoutValidation("X-Forwarded-Proto", publicRequestScheme);
        }

        var client = this.httpClientFactory.CreateClient(
            target.RequiresAddressPinning ? DynamicHttpClientName : HttpClientName);
        UpstreamHttpResult upstreamResult;
        try
        {
            upstreamResult = await this.SendUpstreamAsync(
                client,
                upstreamRequest,
                target,
                pinnedAddresses,
                browserNavigation,
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

        using (upstreamResult.Request)
        using (upstreamResult.Response)
        {
            if (upstreamResult.RedirectLoopDetected)
            {
                await WriteFailureAsync(
                    context,
                    StatusCodes.Status508LoopDetected,
                    "webapp.redirect_loop",
                    "The web application returned a same-origin redirect loop.").ConfigureAwait(false);
                return;
            }

            var upstreamResponse = upstreamResult.Response;
            var finalUpstreamRequest = upstreamResult.Request;
            var upstreamStatusCode = (int)upstreamResponse.StatusCode;
            var redirectLocation = upstreamResponse.Headers.Location;
            context.Response.StatusCode = WebAppResponsePolicy.NormalizeRedirectStatusCode(
                upstreamStatusCode,
                redirectLocation is not null);

            if (upstreamStatusCode is >= 300 and < 400
                && redirectLocation is not null)
            {
                LogUpstreamRedirect(
                    this.logger,
                    upstreamStatusCode,
                    SanitizeUriForLog(finalUpstreamRequest.RequestUri!),
                    SanitizeRedirectForLog(finalUpstreamRequest.RequestUri!, redirectLocation),
                    null);
            }

            var rewrittenContent = await TryRewriteBrowserContentAsync(
                upstreamResponse,
                finalUpstreamRequest.RequestUri!,
                publicRequestScheme,
                target.RequiresAddressPinning,
                this.registry.DynamicEnabled ? this.registry.DynamicProxyZone : null,
                context.RequestAborted).ConfigureAwait(false);

            CopyResponseHeaders(
                upstreamResponse,
                context.Response,
                new WebAppResponseContext(
                    target.Upstream,
                    finalUpstreamRequest.RequestUri!,
                    publicRequestScheme,
                    context.Request.Host.Value ?? string.Empty,
                    string.Equals(publicRequestScheme, "https", StringComparison.Ordinal),
                    this.registry.DynamicEnabled ? this.registry.DynamicProxyZone : null,
                    browserRequestOrigin,
                    upstreamRequestOrigin,
                    rewrittenContent?.InjectedScriptHash));

            if (redirectLocation is not null)
            {
                context.Response.Headers.CacheControl = "no-store";
                context.Response.Headers.Pragma = "no-cache";

                // A permanent redirect cached by the browser for an encoded proxy origin can
                // survive a JulOS upgrade and bypass the corrected proxy logic entirely. Clear
                // only the HTTP cache (not cookies/storage) before following downgraded 301/308
                // redirects so existing proxy-host redirect loops self-heal.
                if (upstreamStatusCode is StatusCodes.Status301MovedPermanently
                    or StatusCodes.Status308PermanentRedirect)
                {
                    context.Response.Headers["Clear-Site-Data"] = "\"cache\"";
                }

                if (context.Response.Headers.Location.Count > 0)
                {
                    LogProxyRedirect(
                        this.logger,
                        context.Response.StatusCode,
                        SanitizeRedirectForLog(
                            finalUpstreamRequest.RequestUri!,
                            new Uri(context.Response.Headers.Location.ToString(), UriKind.RelativeOrAbsolute)),
                        null);
                }
            }

            if (rewrittenContent is not null)
            {
                context.Response.Headers.Remove("Content-Length");
                context.Response.Headers.Remove("Content-Encoding");
                context.Response.Headers.Remove("ETag");
                context.Response.ContentType = rewrittenContent.ContentType;
                context.Response.ContentLength = rewrittenContent.Body.Length;
                await context.Response.Body.WriteAsync(rewrittenContent.Body, context.RequestAborted).ConfigureAwait(false);
            }
            else
            {
                await upstreamResponse.Content
                    .CopyToAsync(context.Response.Body, context.RequestAborted)
                    .ConfigureAwait(false);
            }
        }
    }

    private async Task<UpstreamHttpResult> SendUpstreamAsync(
        HttpClient client,
        HttpRequestMessage initialRequest,
        WebAppTarget target,
        IPAddress[] pinnedAddresses,
        bool followSameOriginNavigationRedirects,
        CancellationToken cancellationToken)
    {
        var currentRequest = initialRequest;
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            currentRequest.RequestUri!.AbsoluteUri,
        };
        var followedRedirects = 0;

        while (true)
        {
            HttpResponseMessage response;
            try
            {
                response = await client.SendAsync(
                    currentRequest,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                currentRequest.Dispose();
                throw;
            }

            if (!followSameOriginNavigationRedirects
                || !TryResolveSameOriginNavigationRedirect(
                    currentRequest,
                    response,
                    target.Upstream,
                    out var redirectTarget))
            {
                return new UpstreamHttpResult(currentRequest, response, RedirectLoopDetected: false);
            }

            if (visited.Contains(redirectTarget.AbsoluteUri))
            {
                return new UpstreamHttpResult(currentRequest, response, RedirectLoopDetected: true);
            }

            if (!IsDirectoryIndexCanonicalization(currentRequest.RequestUri!, redirectTarget))
            {
                return new UpstreamHttpResult(currentRequest, response, RedirectLoopDetected: false);
            }

            if (followedRedirects >= MaximumInternalNavigationRedirects)
            {
                return new UpstreamHttpResult(currentRequest, response, RedirectLoopDetected: true);
            }

            visited.Add(redirectTarget.AbsoluteUri);

            LogInternalNavigationRedirect(
                this.logger,
                SanitizeUriForLog(currentRequest.RequestUri!),
                SanitizeUriForLog(redirectTarget),
                null);

            response.Dispose();
            var nextRequest = CloneRedirectRequest(
                currentRequest,
                redirectTarget,
                target,
                pinnedAddresses);
            currentRequest.Dispose();
            currentRequest = nextRequest;
            followedRedirects++;
        }
    }

    private static bool TryResolveSameOriginNavigationRedirect(
        HttpRequestMessage request,
        HttpResponseMessage response,
        Uri upstream,
        out Uri redirectTarget)
    {
        redirectTarget = null!;
        if (request.Method != HttpMethod.Get && request.Method != HttpMethod.Head)
        {
            return false;
        }

        if ((int)response.StatusCode is not (
            StatusCodes.Status301MovedPermanently
            or StatusCodes.Status302Found
            or StatusCodes.Status303SeeOther
            or StatusCodes.Status307TemporaryRedirect
            or StatusCodes.Status308PermanentRedirect))
        {
            return false;
        }

        var location = response.Headers.Location;
        if (location is null || response.Headers.Contains("Set-Cookie"))
        {
            return false;
        }

        var resolved = location.IsAbsoluteUri ? location : new Uri(request.RequestUri!, location);
        if (resolved.Scheme is not ("http" or "https")
            || !string.Equals(
                resolved.GetLeftPart(UriPartial.Authority),
                upstream.GetLeftPart(UriPartial.Authority),
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        redirectTarget = resolved;
        return true;
    }

    private static bool IsDirectoryIndexCanonicalization(Uri source, Uri target)
    {
        var sourcePath = source.AbsolutePath;
        var slash = sourcePath.LastIndexOf('/');
        if (slash < 0)
        {
            return false;
        }

        var fileName = sourcePath[(slash + 1)..];
        if (!fileName.Equals("index.html", StringComparison.OrdinalIgnoreCase)
            && !fileName.Equals("index.htm", StringComparison.OrdinalIgnoreCase)
            && !fileName.Equals("index.php", StringComparison.OrdinalIgnoreCase)
            && !fileName.Equals("index.asp", StringComparison.OrdinalIgnoreCase)
            && !fileName.Equals("index.aspx", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return string.Equals(target.AbsolutePath, sourcePath[..(slash + 1)], StringComparison.Ordinal);
    }

    private static HttpRequestMessage CloneRedirectRequest(
        HttpRequestMessage source,
        Uri redirectTarget,
        WebAppTarget target,
        IPAddress[] pinnedAddresses)
    {
        var clone = new HttpRequestMessage(source.Method, redirectTarget);
        foreach (var header in source.Headers)
        {
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        clone.Headers.Host = target.Upstream.Authority;
        if (target.RequiresAddressPinning)
        {
            clone.Options.Set(PinnedAddressesOption, pinnedAddresses);
        }

        return clone;
    }

    private sealed record UpstreamHttpResult(
        HttpRequestMessage Request,
        HttpResponseMessage Response,
        bool RedirectLoopDetected);

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
            var rewritten = WebAppResponsePolicy.RewriteContentSecurityPolicy(
                string.Join(", ", values),
                context.InjectedScriptHash);
            if (!string.IsNullOrEmpty(rewritten))
            {
                response.Headers[name] = rewritten;
            }

            return;
        }

        if (string.Equals(name, "Access-Control-Allow-Origin", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(context.BrowserRequestOrigin)
            && !string.IsNullOrWhiteSpace(context.UpstreamRequestOrigin))
        {
            response.Headers[name] = values.Select(value =>
                string.Equals(value, context.UpstreamRequestOrigin, StringComparison.OrdinalIgnoreCase)
                    ? context.BrowserRequestOrigin
                    : value).ToArray();
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
                    context.UpstreamRequestUri,
                    context.RequestScheme,
                    context.RequestHost,
                    context.DynamicProxyZone))
                .ToArray();
            return;
        }

        response.Headers[name] = values.ToArray();
    }

    private readonly record struct WebAppResponseContext(
        Uri Upstream,
        Uri UpstreamRequestUri,
        string RequestScheme,
        string RequestHost,
        bool RequestIsHttps,
        string? DynamicProxyZone,
        string? BrowserRequestOrigin,
        string? UpstreamRequestOrigin,
        string? InjectedScriptHash);

    private static async Task<RewrittenBrowserContent?> TryRewriteBrowserContentAsync(
        HttpResponseMessage upstreamResponse,
        Uri upstreamRequestUri,
        string requestScheme,
        bool dynamicTarget,
        string? proxyZone,
        CancellationToken cancellationToken)
    {
        if (!dynamicTarget
            || string.IsNullOrWhiteSpace(proxyZone)
            || upstreamResponse.Content.Headers.ContentEncoding.Count > 0)
        {
            return null;
        }

        var mediaType = upstreamResponse.Content.Headers.ContentType?.MediaType;
        if (!string.Equals(mediaType, "text/html", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(mediaType, "text/css", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (upstreamResponse.Content.Headers.ContentLength is > 8 * 1024 * 1024)
        {
            return null;
        }

        var source = await upstreamResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (string.Equals(mediaType, "text/html", StringComparison.OrdinalIgnoreCase))
        {
            var html = WebAppContentRewriter.RewriteHtml(source, upstreamRequestUri, requestScheme, proxyZone);
            return new RewrittenBrowserContent(
                Encoding.UTF8.GetBytes(html.Content),
                "text/html; charset=utf-8",
                html.ScriptHash);
        }

        var css = WebAppContentRewriter.RewriteCss(source, upstreamRequestUri, requestScheme, proxyZone);
        return new RewrittenBrowserContent(
            Encoding.UTF8.GetBytes(css),
            "text/css; charset=utf-8",
            null);
    }

    private static bool TryVirtualizeProxyRequestUrl(
        string value,
        string proxyZone,
        bool originOnly,
        out string virtualized)
    {
        virtualized = string.Empty;
        if (!Uri.TryCreate(value, UriKind.Absolute, out var proxyUri)
            || proxyUri.Scheme is not ("http" or "https")
            || !WebAppOriginCodec.TryDecodeHost(proxyUri.Host, proxyZone, out var upstreamOrigin))
        {
            return false;
        }

        if (originOnly)
        {
            virtualized = upstreamOrigin.GetLeftPart(UriPartial.Authority);
            return true;
        }

        virtualized = new Uri(upstreamOrigin, proxyUri.PathAndQuery).AbsoluteUri;
        return true;
    }

    private sealed record RewrittenBrowserContent(byte[] Body, string ContentType, string? InjectedScriptHash);

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

    private static string GetPublicRequestScheme(HttpRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.IsHttps)
        {
            return "https";
        }

        var forwardedProto = request.Headers["X-Forwarded-Proto"].ToString();
        var separator = forwardedProto.IndexOf(',', StringComparison.Ordinal);
        if (separator >= 0)
        {
            forwardedProto = forwardedProto[..separator];
        }

        forwardedProto = forwardedProto.Trim();
        return string.Equals(forwardedProto, "https", StringComparison.OrdinalIgnoreCase)
            ? "https"
            : "http";
    }

    private static bool IsBrowserDocumentNavigation(HttpRequest request) =>
        string.Equals(request.Headers["Sec-Fetch-Dest"].ToString(), "iframe", StringComparison.OrdinalIgnoreCase)
        && IsBrowserNavigation(request);

    private static bool IsBrowserNavigation(HttpRequest request) =>
        string.Equals(request.Headers["Sec-Fetch-Mode"].ToString(), "navigate", StringComparison.OrdinalIgnoreCase);

    private static string SanitizeUriForLog(Uri uri) =>
        uri.GetLeftPart(UriPartial.Path);

    private static string SanitizeRedirectForLog(Uri source, Uri redirect)
    {
        var resolved = redirect.IsAbsoluteUri ? redirect : new Uri(source, redirect);
        return resolved.Scheme is "http" or "https"
            ? resolved.GetLeftPart(UriPartial.Path)
            : redirect.GetLeftPart(UriPartial.Path);
    }

    private static bool IsSuppressedRequestHeader(string name) =>
        WebAppResponsePolicy.IsSuppressedResponseHeader(name)
        || string.Equals(name, "Host", StringComparison.OrdinalIgnoreCase)
        || string.Equals(name, "Authorization", StringComparison.OrdinalIgnoreCase)
        || string.Equals(name, "Forwarded", StringComparison.OrdinalIgnoreCase)
        || name.StartsWith("X-Forwarded-", StringComparison.OrdinalIgnoreCase)
        || name.StartsWith("Sec-Fetch-", StringComparison.OrdinalIgnoreCase);

    private static async Task WriteFailureAsync(HttpContext context, int status, string code, string detail)
    {
        context.Response.StatusCode = status;
        await context.Response.WriteAsJsonAsync(new { code, detail }).ConfigureAwait(false);
    }
}
