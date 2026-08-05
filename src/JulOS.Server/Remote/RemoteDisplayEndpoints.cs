using System.Globalization;
using System.Net.WebSockets;
using System.Security.Claims;

using JulOS.Infrastructure.Remote;

namespace JulOS.Server.Remote;

/// <summary>Maps the authenticated same-origin Remote display WebSocket.</summary>
internal static class RemoteDisplayEndpoints
{
    private const int BufferSize = 16 * 1024;
    private const string GuacamoleSubprotocol = "guacamole";

    internal static IEndpointRouteBuilder MapJulOsRemoteDisplay(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapGet(
                "/api/v1/remote/sessions/{sessionId:guid}/display",
                ConnectAsync)
            .WithTags("Remote")
            .RequireAuthorization();

        return endpoints;
    }

    private static async Task<IResult> ConnectAsync(
        HttpContext context,
        Guid sessionId,
        RemoteDisplayGateway gateway,
        PostgresRemoteDisplayAuthorizationService authorization,
        CancellationToken cancellationToken)
    {
        if (!gateway.IsAllowedOrigin(context.Request.Headers.Origin.ToString()))
        {
            return Failure(
                StatusCodes.Status403Forbidden,
                "remote.display_origin_denied",
                "Remote display requires the configured same-origin browser origin.");
        }

        var ownerUserId = CurrentUserId(context.User);
        var callerPackageId = context.Request.Query["package"].ToString();
        if (ownerUserId is null
            || !TryReadLong(context, "revision", out var revision)
            || !TryReadLong(context, "expires", out var expires))
        {
            return Failure(
                StatusCodes.Status401Unauthorized,
                "remote.display_authorization_required",
                "Remote display authorization failed.");
        }

        var requestedEndpoint = string.Concat(
            context.Request.PathBase.Value,
            context.Request.Path.Value,
            context.Request.QueryString.Value);

        Uri providerEndpoint;
        try
        {
            providerEndpoint = await authorization.AuthorizeAsync(
                ownerUserId.Value,
                sessionId,
                callerPackageId,
                revision,
                expires,
                requestedEndpoint,
                cancellationToken).ConfigureAwait(false);
        }
        catch (RemoteDisplayAuthorizationException exception)
        {
            return AuthorizationFailure(exception);
        }

        if (!context.WebSockets.IsWebSocketRequest)
        {
            return Failure(
                StatusCodes.Status400BadRequest,
                "remote.display_websocket_required",
                "Remote display requires a WebSocket request.");
        }

        var requestedProtocols = context.WebSockets.WebSocketRequestedProtocols;
        if (requestedProtocols.Count != 1
            || !string.Equals(
                requestedProtocols[0],
                GuacamoleSubprotocol,
                StringComparison.Ordinal))
        {
            return Failure(
                StatusCodes.Status400BadRequest,
                "remote.display_subprotocol_required",
                "Remote display requires the guacamole WebSocket subprotocol.");
        }

        using var provider = new ClientWebSocket();
        provider.Options.AddSubProtocol(GuacamoleSubprotocol);
        try
        {
            await provider.ConnectAsync(providerEndpoint, cancellationToken).ConfigureAwait(false);
        }
        catch (WebSocketException)
        {
            return Failure(
                StatusCodes.Status502BadGateway,
                "remote.display_provider_unavailable",
                "The Remote display provider is unavailable.");
        }
        catch (HttpRequestException)
        {
            return Failure(
                StatusCodes.Status502BadGateway,
                "remote.display_provider_unavailable",
                "The Remote display provider is unavailable.");
        }

        if (!string.Equals(
                provider.SubProtocol,
                GuacamoleSubprotocol,
                StringComparison.Ordinal))
        {
            return Failure(
                StatusCodes.Status502BadGateway,
                "remote.display_provider_subprotocol_invalid",
                "The Remote display provider did not negotiate the guacamole WebSocket subprotocol.");
        }

        using var browser = await context.WebSockets
            .AcceptWebSocketAsync(GuacamoleSubprotocol)
            .ConfigureAwait(false);
        await ProxyAsync(browser, provider, cancellationToken).ConfigureAwait(false);
        return Results.Empty;
    }

    private static async Task ProxyAsync(
        WebSocket browser,
        WebSocket provider,
        CancellationToken cancellationToken)
    {
        using var proxyCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var browserToProvider = ForwardAsync(browser, provider, proxyCancellation.Token);
        var providerToBrowser = ForwardAsync(provider, browser, proxyCancellation.Token);

        _ = await Task.WhenAny(browserToProvider, providerToBrowser).ConfigureAwait(false);
        await proxyCancellation.CancelAsync().ConfigureAwait(false);

        try
        {
            await Task.WhenAll(browserToProvider, providerToBrowser).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (proxyCancellation.IsCancellationRequested)
        {
        }
        catch (WebSocketException)
        {
            browser.Abort();
            provider.Abort();
        }
    }

    private static async Task ForwardAsync(
        WebSocket source,
        WebSocket destination,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[BufferSize];
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

    private static bool TryReadLong(HttpContext context, string name, out long value) =>
        long.TryParse(
            context.Request.Query[name].ToString(),
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out value);

    private static Guid? CurrentUserId(ClaimsPrincipal principal)
    {
        var identifier = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(identifier, out var userId) && userId != Guid.Empty
            ? userId
            : null;
    }

    private static IResult AuthorizationFailure(RemoteDisplayAuthorizationException exception) =>
        exception.Failure switch
        {
            RemoteDisplayAuthorizationFailure.Expired => Failure(
                StatusCodes.Status410Gone,
                "remote.display_descriptor_expired",
                exception.Message),
            RemoteDisplayAuthorizationFailure.Stale => Failure(
                StatusCodes.Status409Conflict,
                "remote.display_descriptor_stale",
                exception.Message),
            RemoteDisplayAuthorizationFailure.Unavailable => Failure(
                StatusCodes.Status409Conflict,
                "remote.display_unavailable",
                exception.Message),
            _ => Failure(
                StatusCodes.Status401Unauthorized,
                "remote.display_authorization_required",
                exception.Message),
        };

    private static IResult Failure(int status, string code, string detail) =>
        Results.Json(new { code, detail }, statusCode: status);
}
