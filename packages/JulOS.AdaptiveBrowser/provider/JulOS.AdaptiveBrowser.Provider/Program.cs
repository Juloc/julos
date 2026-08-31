using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

const string StreamSubprotocol = "julos-browser-stream.v1";
const int BufferSize = 32 * 1024;

var sessionId = RequireGuid("JULOS_REMOTE_SESSION_ID");
var targetHost = Require("JULOS_REMOTE_TARGET_HOST");
var targetPort = RequireInt("JULOS_REMOTE_TARGET_PORT", 1, 65535);
var callbackEndpoint = RequireUri("JULOS_REMOTE_CALLBACK_ENDPOINT", "http", "https");
var callbackToken = Require("JULOS_REMOTE_CALLBACK_TOKEN");
var expectedRevision = RequireLong("JULOS_REMOTE_EXPECTED_REVISION", 1, long.MaxValue);
var credentialBytes = Convert.FromBase64String(Require("JULOS_REMOTE_TARGET_CREDENTIAL"));
var streamToken = Encoding.UTF8.GetString(credentialBytes);
CryptographicOperations.ZeroMemory(credentialBytes);
if (string.IsNullOrWhiteSpace(streamToken) || streamToken.Length > 4096)
{
    throw new InvalidOperationException("Adaptive Browser target credential is invalid.");
}
var runtimeId = $"remote-{sessionId:N}";
var targetHealth = new UriBuilder("http", targetHost, targetPort, "/health").Uri;
var targetStream = new UriBuilder("ws", targetHost, targetPort, "/stream").Uri;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls("http://0.0.0.0:8081");
builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole(options => options.SingleLine = true);
var app = builder.Build();
app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(20) });
app.MapGet("/health", () => Results.Ok(new { status = "ready" }));
app.Map("/", async context =>
{
    if (!context.WebSockets.IsWebSocketRequest
        || context.WebSockets.WebSocketRequestedProtocols.Count != 1
        || !string.Equals(context.WebSockets.WebSocketRequestedProtocols[0], StreamSubprotocol, StringComparison.Ordinal))
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        return;
    }

    using var upstream = new ClientWebSocket();
    upstream.Options.AddSubProtocol(StreamSubprotocol);
    upstream.Options.SetRequestHeader("Authorization", $"Bearer {streamToken}");
    try
    {
        await upstream.ConnectAsync(targetStream, context.RequestAborted).ConfigureAwait(false);
    }
    catch (Exception exception) when (exception is WebSocketException or HttpRequestException)
    {
        context.Response.StatusCode = StatusCodes.Status502BadGateway;
        return;
    }

    if (!string.Equals(upstream.SubProtocol, StreamSubprotocol, StringComparison.Ordinal))
    {
        context.Response.StatusCode = StatusCodes.Status502BadGateway;
        return;
    }

    using var browser = await context.WebSockets.AcceptWebSocketAsync(StreamSubprotocol).ConfigureAwait(false);
    using var linked = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
    var first = ForwardAsync(browser, upstream, linked.Token);
    var second = ForwardAsync(upstream, browser, linked.Token);
    _ = await Task.WhenAny(first, second).ConfigureAwait(false);
    await linked.CancelAsync().ConfigureAwait(false);
    try
    {
        await Task.WhenAll(first, second).ConfigureAwait(false);
    }
    catch (OperationCanceledException) when (linked.IsCancellationRequested)
    {
    }
    catch (WebSocketException)
    {
        browser.Abort();
        upstream.Abort();
    }
});

await app.StartAsync().ConfigureAwait(false);
try
{
    await WaitForTargetAsync(targetHealth).ConfigureAwait(false);
    await ReportAsync("connected", null, null, false).ConfigureAwait(false);
}
catch (Exception exception)
{
    try
    {
        await ReportAsync(
            "failed",
            "remote.provider_target_unavailable",
            "The Adaptive Browser runtime did not become ready.",
            true).ConfigureAwait(false);
    }
    catch
    {
    }
    Console.Error.WriteLine(exception.Message);
    await app.StopAsync().ConfigureAwait(false);
    return 70;
}

await app.WaitForShutdownAsync().ConfigureAwait(false);
return 0;

async Task ReportAsync(string eventName, string? failureCode, string? failureDetail, bool retryable)
{
    using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
    client.DefaultRequestHeaders.Add("X-JulOS-Remote-Token", callbackToken);
    using var response = await client.PostAsJsonAsync(
        callbackEndpoint,
        new
        {
            sessionId,
            runtimeId,
            @event = eventName,
            expectedRevision,
            failureCode,
            failureDetail,
            retryable,
        }).ConfigureAwait(false);
    response.EnsureSuccessStatusCode();
}

static async Task WaitForTargetAsync(Uri healthEndpoint)
{
    using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
    Exception? lastFailure = null;
    for (var attempt = 0; attempt < 100; attempt++)
    {
        try
        {
            using var response = await client.GetAsync(healthEndpoint).ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
            {
                return;
            }
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            lastFailure = exception;
        }
        await Task.Delay(200).ConfigureAwait(false);
    }
    throw new HttpRequestException("Adaptive Browser runtime health check timed out.", lastFailure);
}

static async Task ForwardAsync(WebSocket source, WebSocket destination, CancellationToken cancellationToken)
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

static string Require(string name)
{
    var value = Environment.GetEnvironmentVariable(name);
    return string.IsNullOrWhiteSpace(value)
        ? throw new InvalidOperationException($"{name} is required.")
        : value;
}

static Guid RequireGuid(string name) => Guid.TryParse(Require(name), out var value) && value != Guid.Empty
    ? value
    : throw new InvalidOperationException($"{name} is invalid.");

static int RequireInt(string name, int minimum, int maximum) =>
    int.TryParse(Require(name), System.Globalization.CultureInfo.InvariantCulture, out var value)
        && value >= minimum && value <= maximum
        ? value
        : throw new InvalidOperationException($"{name} is invalid.");

static long RequireLong(string name, long minimum, long maximum) =>
    long.TryParse(Require(name), System.Globalization.CultureInfo.InvariantCulture, out var value)
        && value >= minimum && value <= maximum
        ? value
        : throw new InvalidOperationException($"{name} is invalid.");

static Uri RequireUri(string name, params string[] schemes)
{
    var value = Require(name);
    return Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && schemes.Contains(uri.Scheme, StringComparer.OrdinalIgnoreCase)
        && string.IsNullOrEmpty(uri.UserInfo)
        ? uri
        : throw new InvalidOperationException($"{name} is invalid.");
}
