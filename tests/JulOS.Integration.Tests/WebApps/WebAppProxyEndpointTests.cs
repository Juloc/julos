using System.Net;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text;

using JulOS.Contracts.Authentication;
using JulOS.Contracts.WebApps;
using JulOS.Infrastructure.Authentication;
using JulOS.Infrastructure.WebApps;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace JulOS.Integration.Tests.WebApps;

[TestClass]
[DoNotParallelize]
public sealed class WebAppProxyEndpointTests
{
    private const string AdministratorPassword = "Valid-Initial-Password-42!";
    private const string SecondaryPassword = "Valid-Secondary-Password-42!";
    private const string TargetHost = "app.test";

    [TestMethod]
    public async Task ForwardsToTheUpstreamStripsFramingHeadersAndDoesNotLeakTheJulOsSession()
    {
        var databasePath = CreateDatabasePath();
        await using var upstream = await StartUpstreamAsync().ConfigureAwait(false);
        try
        {
            using var host = CreateHost(databasePath, upstream.Urls.First());
            using var client = host.CreateClient();
            var cookie = await SetupAdministratorAsync(client).ConfigureAwait(false);

            using var request = TargetRequest("/panel?tab=devices", cookie);
            using var response = await client.SendAsync(request).ConfigureAwait(false);

            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            Assert.AreEqual("UPSTREAM-OK:/panel", await response.Content.ReadAsStringAsync().ConfigureAwait(false));
            Assert.IsFalse(response.Headers.Contains("X-Frame-Options"));

            Assert.IsTrue(response.Headers.TryGetValues("Content-Security-Policy", out var csp));
            var policy = string.Join("; ", csp!);
            Assert.IsFalse(policy.Contains("frame-ancestors", StringComparison.OrdinalIgnoreCase), policy);

            // The upstream must be reached with its own Host and the forwarded metadata, and must
            // never receive JulOS's own session cookie.
            Assert.AreEqual(new Uri(upstream.Urls.First()).Authority, Single(response, "X-Echo-Host"));
            Assert.AreEqual(TargetHost, Single(response, "X-Echo-Forwarded-Host"));
            Assert.AreEqual("http", Single(response, "X-Echo-Forwarded-Proto"));
            Assert.IsFalse(
                Single(response, "X-Echo-Cookie").Contains(".JulOS.", StringComparison.OrdinalIgnoreCase),
                $"JulOS cookie leaked to upstream: {Single(response, "X-Echo-Cookie")}");
        }
        finally
        {
            DeleteDatabase(databasePath);
        }
    }

    [TestMethod]
    public async Task ProxiesAWebSocketToTheUpstreamForAnAuthenticatedUser()
    {
        var databasePath = CreateDatabasePath();
        await using var upstream = await StartUpstreamAsync().ConfigureAwait(false);
        try
        {
            using var host = CreateHost(databasePath, upstream.Urls.First());
            using var client = host.CreateClient();
            var cookie = await SetupAdministratorAsync(client).ConfigureAwait(false);

            var webSocketClient = host.Server.CreateWebSocketClient();
            webSocketClient.SubProtocols.Add("echo");
            webSocketClient.ConfigureRequest = configuredRequest =>
                configuredRequest.Headers["Cookie"] = cookie;

            using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            using var socket = await webSocketClient
                .ConnectAsync(new Uri($"ws://{TargetHost}/socket"), cancellation.Token)
                .ConfigureAwait(false);

            Assert.AreEqual("echo", socket.SubProtocol);

            var payload = Encoding.UTF8.GetBytes("web-app-socket-roundtrip");
            await socket.SendAsync(payload, WebSocketMessageType.Binary, endOfMessage: true, cancellation.Token)
                .ConfigureAwait(false);

            var received = new byte[payload.Length];
            var result = await socket.ReceiveAsync(received, cancellation.Token).ConfigureAwait(false);

            Assert.AreEqual(WebSocketMessageType.Binary, result.MessageType);
            Assert.AreEqual(payload.Length, result.Count);
            CollectionAssert.AreEqual(payload, received);
        }
        finally
        {
            DeleteDatabase(databasePath);
        }
    }

    [TestMethod]
    public async Task RejectsAnUnauthenticatedRequestToATargetHost()
    {
        var databasePath = CreateDatabasePath();
        await using var upstream = await StartUpstreamAsync().ConfigureAwait(false);
        try
        {
            using var host = CreateHost(databasePath, upstream.Urls.First());
            using var client = host.CreateClient();

            using var request = TargetRequest("/panel", cookie: null);
            using var response = await client.SendAsync(request).ConfigureAwait(false);

            Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        }
        finally
        {
            DeleteDatabase(databasePath);
        }
    }

    [TestMethod]
    public async Task RejectsAnAuthenticatedUserWithoutTheWebAppPermission()
    {
        var databasePath = CreateDatabasePath();
        await using var upstream = await StartUpstreamAsync().ConfigureAwait(false);
        try
        {
            using var host = CreateHost(databasePath, upstream.Urls.First());
            using var administratorClient = host.CreateClient();
            _ = await SetupAdministratorAsync(administratorClient).ConfigureAwait(false);

            await CreateUserAsync(host, "viewer", "Viewer").ConfigureAwait(false);
            using var viewerClient = host.CreateClient();
            var cookie = await LoginAndReadCookieAsync(viewerClient, "viewer", SecondaryPassword)
                .ConfigureAwait(false);

            // The discovery endpoint is gated on the web-application permission.
            using var listRequest = new HttpRequestMessage(HttpMethod.Get, "/api/v1/webapps");
            listRequest.Headers.TryAddWithoutValidation("Cookie", cookie);
            using var listResponse = await viewerClient.SendAsync(listRequest).ConfigureAwait(false);
            Assert.AreEqual(HttpStatusCode.Forbidden, listResponse.StatusCode);

            // The transparent proxy is gated on the same permission, so an authenticated
            // user without it cannot reach the internal target.
            using var proxyRequest = TargetRequest("/panel", cookie);
            using var proxyResponse = await viewerClient.SendAsync(proxyRequest).ConfigureAwait(false);
            Assert.AreEqual(HttpStatusCode.Forbidden, proxyResponse.StatusCode);
        }
        finally
        {
            DeleteDatabase(databasePath);
        }
    }

    [TestMethod]
    public async Task ListsConfiguredProxyTargetsForAnAuthenticatedUser()
    {
        var databasePath = CreateDatabasePath();
        await using var upstream = await StartUpstreamAsync().ConfigureAwait(false);
        try
        {
            using var host = CreateHost(databasePath, upstream.Urls.First());
            using var client = host.CreateClient();
            var cookie = await SetupAdministratorAsync(client).ConfigureAwait(false);

            using var request = new HttpRequestMessage(HttpMethod.Get, "http://localhost/api/v1/webapps");
            request.Headers.TryAddWithoutValidation("Cookie", cookie);
            using var response = await client.SendAsync(request).ConfigureAwait(false);

            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            var summaries = await response.Content
                .ReadFromJsonAsync<WebAppSummaryResponse[]>()
                .ConfigureAwait(false);
            Assert.IsNotNull(summaries);
            Assert.AreEqual(1, summaries!.Length);
            Assert.AreEqual(TargetHost, summaries[0].Host);
        }
        finally
        {
            DeleteDatabase(databasePath);
        }
    }

    [TestMethod]
    public async Task ScopesTheSessionCookieToTheConfiguredParentDomain()
    {
        var databasePath = CreateDatabasePath();
        try
        {
            using var host = new ServerHost(
                $"Data Source={databasePath};Cache=Shared",
                new Dictionary<string, string?>
                {
                    ["Database:Provider"] = "sqlite",
                    ["Authentication:CookieDomain"] = ".example.test",
                });
            using var client = host.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

            using var response = await client.PostAsJsonAsync(
                "/api/v1/auth/setup",
                new InitialAdministratorRequest("admin", "Administrator", AdministratorPassword))
                .ConfigureAwait(false);
            Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);

            var session = response.Headers.GetValues("Set-Cookie")
                .Single(value => value.StartsWith(".JulOS.Session=", StringComparison.Ordinal));
            StringAssert.Contains(session.ToLowerInvariant(), "domain=.example.test");
        }
        finally
        {
            DeleteDatabase(databasePath);
        }
    }

    [TestMethod]
    public async Task DynamicHostForwardsToTheDecodedUpstreamAndRewritesCookieAndRedirect()
    {
        var databasePath = CreateDatabasePath();
        await using var upstream = await StartUpstreamAsync().ConfigureAwait(false);
        try
        {
            var encodedHost = EncodedHost(upstream.Urls.First());
            using var host = CreateDynamicHost(databasePath);
            using var client = host.CreateClient(new WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false,
                HandleCookies = false,
            });
            var cookie = await SetupAdministratorAsync(client).ConfigureAwait(false);

            using (var forward = DynamicRequest(encodedHost, "/panel?x=1", cookie))
            using (var response = await client.SendAsync(forward).ConfigureAwait(false))
            {
                Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
                Assert.AreEqual("UPSTREAM-OK:/panel", await response.Content.ReadAsStringAsync().ConfigureAwait(false));
                Assert.IsFalse(response.Headers.Contains("X-Frame-Options"));
                Assert.AreEqual(new Uri(upstream.Urls.First()).Authority, Single(response, "X-Echo-Host"));
            }

            using (var redirect = DynamicRequest(encodedHost, "/redirect", cookie))
            using (var response = await client.SendAsync(redirect).ConfigureAwait(false))
            {
                Assert.AreEqual(HttpStatusCode.Redirect, response.StatusCode);
                Assert.AreEqual($"http://{encodedHost}/dashboard", Single(response, "Location"));
                var setCookie = Single(response, "Set-Cookie");
                Assert.IsFalse(setCookie.Contains("domain", StringComparison.OrdinalIgnoreCase), setCookie);
                StringAssert.Contains(setCookie, "app=1");
            }
        }
        finally
        {
            DeleteDatabase(databasePath);
        }
    }

    [TestMethod]
    public async Task DynamicHostUsesForwardedHttpsForProxyResponseWithoutForwardingProxyHeadersUpstream()
    {
        var databasePath = CreateDatabasePath();
        await using var upstream = await StartUpstreamAsync().ConfigureAwait(false);
        try
        {
            var encodedHost = EncodedHost(upstream.Urls.First());
            using var host = CreateDynamicHost(databasePath);
            using var client = host.CreateClient(new WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false,
                HandleCookies = false,
            });
            var cookie = await SetupAdministratorAsync(client).ConfigureAwait(false);

            using (var forward = DynamicRequest(encodedHost, "/panel", cookie))
            {
                forward.Headers.TryAddWithoutValidation("X-Forwarded-Proto", "https");
                using var response = await client.SendAsync(forward).ConfigureAwait(false);

                Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
                Assert.AreEqual(string.Empty, Single(response, "X-Echo-Forwarded-Host"));
                Assert.AreEqual(string.Empty, Single(response, "X-Echo-Forwarded-Proto"));
            }

            using (var redirect = DynamicRequest(encodedHost, "/redirect", cookie))
            {
                redirect.Headers.TryAddWithoutValidation("X-Forwarded-Proto", "https");
                using var response = await client.SendAsync(redirect).ConfigureAwait(false);

                Assert.AreEqual(HttpStatusCode.Redirect, response.StatusCode);
                Assert.AreEqual($"https://{encodedHost}/dashboard", Single(response, "Location"));
                StringAssert.Contains(Single(response, "Set-Cookie"), "Secure");
            }
        }
        finally
        {
            DeleteDatabase(databasePath);
        }
    }

    [TestMethod]
    public async Task DynamicHostWebSocketUsesTheValidatedPinnedAddress()
    {
        var databasePath = CreateDatabasePath();
        await using var upstream = await StartUpstreamAsync().ConfigureAwait(false);
        try
        {
            var encodedHost = EncodedHost(upstream.Urls.First());
            using var host = CreateDynamicHost(databasePath);
            using var client = host.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
            var cookie = await SetupAdministratorAsync(client).ConfigureAwait(false);

            var webSocketClient = host.Server.CreateWebSocketClient();
            webSocketClient.SubProtocols.Add("echo");
            webSocketClient.ConfigureRequest = configuredRequest =>
                configuredRequest.Headers["Cookie"] = cookie;

            using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            using var socket = await webSocketClient
                .ConnectAsync(new Uri($"ws://{encodedHost}/socket"), cancellation.Token)
                .ConfigureAwait(false);

            var payload = Encoding.UTF8.GetBytes("dynamic-pinned-websocket");
            await socket.SendAsync(payload, WebSocketMessageType.Binary, endOfMessage: true, cancellation.Token)
                .ConfigureAwait(false);

            var received = new byte[payload.Length];
            var result = await socket.ReceiveAsync(received, cancellation.Token).ConfigureAwait(false);

            Assert.AreEqual(WebSocketMessageType.Binary, result.MessageType);
            Assert.AreEqual(payload.Length, result.Count);
            CollectionAssert.AreEqual(payload, received);
        }
        finally
        {
            DeleteDatabase(databasePath);
        }
    }

    [TestMethod]
    public async Task DynamicDnsTargetIsDeniedWhenItsResolvedAddressIsOutsideTheAllowedCidr()
    {
        var databasePath = CreateDatabasePath();
        await using var upstream = await StartUpstreamAsync().ConfigureAwait(false);
        try
        {
            var upstreamPort = new Uri(upstream.Urls.First()).Port;
            var encodedHost = EncodedHost($"http://localhost:{upstreamPort}");
            using var host = CreateDynamicDnsHost(databasePath, "10.0.0.0/8");
            using var client = host.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
            var cookie = await SetupAdministratorAsync(client).ConfigureAwait(false);

            using var request = DynamicRequest(encodedHost, "/panel", cookie);
            using var response = await client.SendAsync(request).ConfigureAwait(false);

            Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
        }
        finally
        {
            DeleteDatabase(databasePath);
        }
    }

    [TestMethod]
    public async Task RejectsADynamicHostForAPublicUpstreamOrigin()
    {
        var databasePath = CreateDatabasePath();
        await using var upstream = await StartUpstreamAsync().ConfigureAwait(false);
        try
        {
            using var host = CreateDynamicHost(databasePath);
            using var client = host.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
            var cookie = await SetupAdministratorAsync(client).ConfigureAwait(false);

            using var request = DynamicRequest(EncodedHost("https://youtube.com"), "/", cookie);
            using var response = await client.SendAsync(request).ConfigureAwait(false);

            Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
        }
        finally
        {
            DeleteDatabase(databasePath);
        }
    }

    private static string EncodedHost(string upstreamUrl) =>
        WebAppOriginCodec.EncodeHost(new Uri(upstreamUrl), "p.localtest.me")!;

    private static ServerHost CreateDynamicHost(string databasePath) =>
        new(
            $"Data Source={databasePath};Cache=Shared",
            new Dictionary<string, string?>
            {
                ["Database:Provider"] = "sqlite",
                ["Authentication:CookieDomain"] = ".localtest.me",
                ["WebApps:Dynamic:Enabled"] = "true",
                ["WebApps:Dynamic:ProxyZone"] = "p.localtest.me",
                ["WebApps:Dynamic:AllowPublicInternet"] = "false",
                ["WebApps:Dynamic:AllowedHosts:0"] = "127.0.0.0/8",
                ["WebApps:AllowInvalidUpstreamCertificates"] = "true",
            });

    private static ServerHost CreateDynamicDnsHost(string databasePath, string allowedCidr) =>
        new(
            $"Data Source={databasePath};Cache=Shared",
            new Dictionary<string, string?>
            {
                ["Database:Provider"] = "sqlite",
                ["Authentication:CookieDomain"] = ".localtest.me",
                ["WebApps:Dynamic:Enabled"] = "true",
                ["WebApps:Dynamic:ProxyZone"] = "p.localtest.me",
                ["WebApps:Dynamic:AllowPublicInternet"] = "false",
                ["WebApps:Dynamic:AllowedHosts:0"] = "localhost",
                ["WebApps:Dynamic:AllowedHosts:1"] = allowedCidr,
                ["WebApps:AllowInvalidUpstreamCertificates"] = "true",
            });

    private static HttpRequestMessage DynamicRequest(string encodedHost, string pathAndQuery, string cookie)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"http://{encodedHost}{pathAndQuery}");
        request.Headers.TryAddWithoutValidation("Cookie", cookie);
        return request;
    }

    private static string Single(HttpResponseMessage response, string header) =>
        response.Headers.TryGetValues(header, out var values) ? string.Concat(values) : string.Empty;

    private static ServerHost CreateHost(string databasePath, string upstreamUrl) =>
        new(
            $"Data Source={databasePath};Cache=Shared",
            new Dictionary<string, string?>
            {
                ["Database:Provider"] = "sqlite",
                ["WebApps:Targets:0:Host"] = TargetHost,
                ["WebApps:Targets:0:Upstream"] = upstreamUrl,
            });

    private static HttpRequestMessage TargetRequest(string pathAndQuery, string? cookie)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"http://{TargetHost}{pathAndQuery}");
        if (cookie is not null)
        {
            request.Headers.TryAddWithoutValidation("Cookie", cookie);
        }

        return request;
    }

    private static async Task<string> SetupAdministratorAsync(HttpClient client)
    {
        using var response = await client.PostAsJsonAsync(
            "/api/v1/auth/setup",
            new InitialAdministratorRequest("admin", "Administrator", AdministratorPassword))
            .ConfigureAwait(false);
        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);

        return string.Join(
            "; ",
            response.Headers.GetValues("Set-Cookie")
                .Select(value => value.Split(';', 2, StringSplitOptions.None)[0]));
    }

    private static async Task CreateUserAsync(ServerHost host, string userName, string displayName)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var manager = scope.ServiceProvider.GetRequiredService<UserManager<LocalUser>>();
        var now = TimeProvider.System.GetUtcNow();
        var user = new LocalUser
        {
            Id = Guid.CreateVersion7(now),
            UserName = userName,
            DisplayName = displayName,
            PreferredLanguage = "en",
            TimeZone = "UTC",
            Theme = "system",
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            Revision = 1,
        };
        var result = await manager.CreateAsync(user, SecondaryPassword).ConfigureAwait(false);
        Assert.IsTrue(result.Succeeded, string.Join(", ", result.Errors.Select(error => error.Code)));
    }

    private static async Task<string> LoginAndReadCookieAsync(HttpClient client, string userName, string password)
    {
        using var response = await client.PostAsJsonAsync(
            "/api/v1/auth/login",
            new LocalLoginRequest(userName, password))
            .ConfigureAwait(false);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);

        return string.Join(
            "; ",
            response.Headers.GetValues("Set-Cookie")
                .Select(value => value.Split(';', 2, StringSplitOptions.None)[0]));
    }

    private static async Task<WebApplication> StartUpstreamAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        var app = builder.Build();
        app.UseWebSockets();
        app.Run(async context =>
        {
            if (context.WebSockets.IsWebSocketRequest)
            {
                await EchoWebSocketAsync(context).ConfigureAwait(false);
                return;
            }

            if (context.Request.Path == "/redirect")
            {
                context.Response.StatusCode = StatusCodes.Status302Found;
                context.Response.Headers.Location =
                    $"{context.Request.Scheme}://{context.Request.Host}/dashboard";
                context.Response.Headers.SetCookie = "app=1; Domain=127.0.0.1; Path=/; SameSite=Lax";
                return;
            }

            context.Response.StatusCode = StatusCodes.Status200OK;
            context.Response.Headers["X-Frame-Options"] = "DENY";
            context.Response.Headers["Content-Security-Policy"] = "default-src 'self'; frame-ancestors 'none'";
            context.Response.Headers["X-Echo-Host"] = context.Request.Host.Value;
            context.Response.Headers["X-Echo-Forwarded-Host"] = context.Request.Headers["X-Forwarded-Host"].ToString();
            context.Response.Headers["X-Echo-Forwarded-Proto"] = context.Request.Headers["X-Forwarded-Proto"].ToString();
            context.Response.Headers["X-Echo-Cookie"] = context.Request.Headers.Cookie.ToString();
            await context.Response.WriteAsync($"UPSTREAM-OK:{context.Request.Path}").ConfigureAwait(false);
        });
        await app.StartAsync().ConfigureAwait(false);
        return app;
    }

    private static async Task EchoWebSocketAsync(HttpContext context)
    {
        var subprotocol = context.WebSockets.WebSocketRequestedProtocols.Count > 0
            ? context.WebSockets.WebSocketRequestedProtocols[0]
            : null;
        using var socket = await context.WebSockets.AcceptWebSocketAsync(subprotocol).ConfigureAwait(false);
        var buffer = new byte[4 * 1024];
        while (socket.State == WebSocketState.Open)
        {
            var result = await socket.ReceiveAsync(buffer, context.RequestAborted).ConfigureAwait(false);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                await socket.CloseAsync(
                    WebSocketCloseStatus.NormalClosure,
                    result.CloseStatusDescription,
                    context.RequestAborted).ConfigureAwait(false);
                return;
            }

            await socket.SendAsync(
                buffer.AsMemory(0, result.Count),
                result.MessageType,
                result.EndOfMessage,
                context.RequestAborted).ConfigureAwait(false);
        }
    }

    private static string CreateDatabasePath()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "julos-webapp-proxy-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, "core.db");
    }

    private static void DeleteDatabase(string databasePath)
    {
        SqliteConnection.ClearAllPools();
        var directory = Path.GetDirectoryName(databasePath);
        if (directory is not null && Directory.Exists(directory))
        {
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }
}
