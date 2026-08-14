using System.Net;
using System.Net.Http.Json;

using JulOS.Contracts.Authentication;
using JulOS.Server.WebApps;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace JulOS.Integration.Tests.WebApps;

[TestClass]
[DoNotParallelize]
public sealed class WebAppProxyEndpointTests
{
    private const string AdministratorPassword = "Valid-Initial-Password-42!";
    private const string TargetHost = "app.test";

    [TestMethod]
    public async Task ForwardsToTheUpstreamAndStripsFramingHeadersForAnAuthenticatedUser()
    {
        var databasePath = CreateDatabasePath();
        using var upstream = await StartUpstreamAsync().ConfigureAwait(false);
        try
        {
            using var host = CreateHost(databasePath, upstream);
            using var client = host.CreateClient();
            var cookie = await SetupAdministratorAsync(client).ConfigureAwait(false);

            using var request = TargetRequest("/panel?tab=devices", cookie);
            using var response = await client.SendAsync(request).ConfigureAwait(false);

            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            Assert.AreEqual("UPSTREAM-OK:/panel", body);
            Assert.IsFalse(response.Headers.Contains("X-Frame-Options"));

            Assert.IsTrue(response.Headers.TryGetValues("Content-Security-Policy", out var csp));
            var policy = string.Join("; ", csp!);
            Assert.IsFalse(
                policy.Contains("frame-ancestors", StringComparison.OrdinalIgnoreCase),
                $"CSP still restricts framing: {policy}");
            StringAssert.Contains(policy, "default-src 'self'");
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
        using var upstream = await StartUpstreamAsync().ConfigureAwait(false);
        try
        {
            using var host = CreateHost(databasePath, upstream);
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

    private static ServerHost CreateHost(string databasePath, IHost upstream)
    {
        var handler = upstream.GetTestServer().CreateHandler();
        return new ServerHost(
            $"Data Source={databasePath};Cache=Shared",
            new Dictionary<string, string?>
            {
                ["Database:Provider"] = "sqlite",
                ["WebApps:Targets:0:Host"] = TargetHost,
                ["WebApps:Targets:0:Upstream"] = "http://upstream.internal",
            },
            services => services
                .AddHttpClient(WebAppProxyMiddleware.HttpClientName)
                .ConfigurePrimaryHttpMessageHandler(() => handler));
    }

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

    private static async Task<IHost> StartUpstreamAsync()
    {
        var host = new HostBuilder()
            .ConfigureWebHost(web => web
                .UseTestServer()
                .Configure(app => app.Run(async context =>
                {
                    context.Response.StatusCode = StatusCodes.Status200OK;
                    context.Response.Headers["X-Frame-Options"] = "DENY";
                    context.Response.Headers["Content-Security-Policy"] =
                        "default-src 'self'; frame-ancestors 'none'";
                    await context.Response
                        .WriteAsync($"UPSTREAM-OK:{context.Request.Path}")
                        .ConfigureAwait(false);
                })))
            .Build();
        await host.StartAsync().ConfigureAwait(false);
        return host;
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
