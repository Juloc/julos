using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;

using JulOS.AdaptiveBrowser.Runtime;

const string StreamSubprotocol = "julos-browser-stream.v1";

var streamToken = Environment.GetEnvironmentVariable("JULOS_BROWSER_STREAM_TOKEN");
if (string.IsNullOrWhiteSpace(streamToken) || streamToken.Length > 4096)
{
    Console.Error.WriteLine("JULOS_BROWSER_STREAM_TOKEN is required.");
    return 64;
}

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls("http://0.0.0.0:8080");
builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole(options => options.SingleLine = true);
var app = builder.Build();
app.UseWebSockets(new WebSocketOptions
{
    KeepAliveInterval = TimeSpan.FromSeconds(20),
});

app.MapGet("/health", () => Results.Ok(new { status = "ready" }));
app.Map("/stream", async context =>
{
    if (!context.WebSockets.IsWebSocketRequest)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        return;
    }

    if (context.WebSockets.WebSocketRequestedProtocols.Count != 1
        || !string.Equals(
            context.WebSockets.WebSocketRequestedProtocols[0],
            StreamSubprotocol,
            StringComparison.Ordinal))
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        return;
    }

    var authorization = context.Request.Headers.Authorization.ToString();
    const string bearerPrefix = "Bearer ";
    if (!authorization.StartsWith(bearerPrefix, StringComparison.Ordinal)
        || !FixedTimeEquals(authorization[bearerPrefix.Length..], streamToken))
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        return;
    }

    using var socket = await context.WebSockets
        .AcceptWebSocketAsync(StreamSubprotocol)
        .ConfigureAwait(false);
    var logger = context.RequestServices
        .GetRequiredService<ILoggerFactory>()
        .CreateLogger("JulOS.AdaptiveBrowser.Runtime.Stream");
    var session = new BrowserStreamSession(socket, logger);
    await session.RunAsync(context.RequestAborted).ConfigureAwait(false);
});

await app.RunAsync().ConfigureAwait(false);
return 0;

static bool FixedTimeEquals(string left, string right)
{
    var leftBytes = Encoding.UTF8.GetBytes(left);
    var rightBytes = Encoding.UTF8.GetBytes(right);
    try
    {
        return leftBytes.Length == rightBytes.Length
            && CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
    }
    finally
    {
        CryptographicOperations.ZeroMemory(leftBytes);
        CryptographicOperations.ZeroMemory(rightBytes);
    }
}
