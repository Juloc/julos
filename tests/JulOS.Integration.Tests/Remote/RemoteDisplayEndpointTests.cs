using System.Net;
using System.Net.WebSockets;
using System.Net.Http.Json;
using System.Text;

using JulOS.Contracts.Authentication;
using JulOS.Contracts.Remote;
using JulOS.Infrastructure.Persistence.Core;
using JulOS.Infrastructure.Remote;
using JulOS.Integration.Tests.Persistence;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace JulOS.Integration.Tests.Remote;

[TestClass]
[DoNotParallelize]
public sealed class RemoteDisplayEndpointTests
{
    private const string AdministratorPassword = "Valid-Initial-Password-42!";
    private const string CallerPackageId = "de.juloc.julos.remote";
    private const string PublicOrigin = "https://localhost";
    private const string RuntimeId = "remote-55555555555545558555555555555555";

    private static readonly WebApplicationFactoryClientOptions ClientOptions = new()
    {
        BaseAddress = new Uri(PublicOrigin),
        AllowAutoRedirect = false,
        HandleCookies = false,
    };

    [TestMethod]
    public async Task EndpointRejectsWrongOriginPackageStaleExpiredAndTerminalDescriptors()
    {
        await using var database = await CreateMigratedDatabaseAsync().ConfigureAwait(false);
        await using var provider = await EchoProvider.StartAsync().ConfigureAwait(false);
        var clock = new MutableTimeProvider(
            new DateTimeOffset(2026, 8, 4, 20, 0, 0, TimeSpan.Zero));
        using var host = CreateHost(database.ConnectionString, provider.EndpointTemplate, clock);
        using var client = host.CreateClient(ClientOptions);
        var authentication = await SetupAdministratorAsync(client).ConfigureAwait(false);
        var session = await InsertConnectedSessionAsync(
            host,
            authentication.User.UserId,
            clock).ConfigureAwait(false);

        using (var current = DisplayRequest(session.Descriptor.Endpoint, authentication.Cookie, PublicOrigin))
        using (var response = await client.SendAsync(current).ConfigureAwait(false))
        {
            Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        }

        using (var wrongOrigin = DisplayRequest(
            session.Descriptor.Endpoint,
            authentication.Cookie,
            "https://attacker.example"))
        using (var response = await client.SendAsync(wrongOrigin).ConfigureAwait(false))
        {
            Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
        }

        using (var wrongPackage = DisplayRequest(
            session.Descriptor.Endpoint.Replace(
                CallerPackageId,
                "de.juloc.other",
                StringComparison.Ordinal),
            authentication.Cookie,
            PublicOrigin))
        using (var response = await client.SendAsync(wrongPackage).ConfigureAwait(false))
        {
            Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        using (var anonymous = DisplayRequest(session.Descriptor.Endpoint, cookie: null, PublicOrigin))
        using (var response = await client.SendAsync(anonymous).ConfigureAwait(false))
        {
            Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        await UpdateSessionAsync(
            host,
            session.SessionId,
            row => row.Revision++).ConfigureAwait(false);
        using (var stale = DisplayRequest(session.Descriptor.Endpoint, authentication.Cookie, PublicOrigin))
        using (var response = await client.SendAsync(stale).ConfigureAwait(false))
        {
            Assert.AreEqual(HttpStatusCode.Conflict, response.StatusCode);
        }

        await UpdateSessionAsync(
            host,
            session.SessionId,
            row => row.Revision--).ConfigureAwait(false);
        clock.Advance(TimeSpan.FromSeconds(61));
        using (var expired = DisplayRequest(session.Descriptor.Endpoint, authentication.Cookie, PublicOrigin))
        using (var response = await client.SendAsync(expired).ConfigureAwait(false))
        {
            Assert.AreEqual(HttpStatusCode.Gone, response.StatusCode);
        }

        await UpdateSessionAsync(
            host,
            session.SessionId,
            row =>
            {
                row.State = RemoteSessionStates.Disconnected;
                row.EndedAtUtc = clock.GetUtcNow();
                row.DisplayKind = null;
                row.DisplayContractVersion = null;
                row.DisplayEndpoint = null;
                row.DisplayExpiresAtUtc = null;
            }).ConfigureAwait(false);
        using (var terminal = DisplayRequest(session.Descriptor.Endpoint, authentication.Cookie, PublicOrigin))
        using (var response = await client.SendAsync(terminal).ConfigureAwait(false))
        {
            Assert.AreEqual(HttpStatusCode.Conflict, response.StatusCode);
        }
    }

    [TestMethod]
    public async Task WebSocketProxiesBinaryFramesToTheHiddenProvider()
    {
        await using var database = await CreateMigratedDatabaseAsync().ConfigureAwait(false);
        await using var provider = await EchoProvider.StartAsync().ConfigureAwait(false);
        var clock = new MutableTimeProvider(
            new DateTimeOffset(2026, 8, 4, 21, 0, 0, TimeSpan.Zero));
        using var host = CreateHost(database.ConnectionString, provider.EndpointTemplate, clock);
        using var client = host.CreateClient(ClientOptions);
        var authentication = await SetupAdministratorAsync(client).ConfigureAwait(false);
        var session = await InsertConnectedSessionAsync(
            host,
            authentication.User.UserId,
            clock).ConfigureAwait(false);

        var webSocketClient = host.Server.CreateWebSocketClient();
        webSocketClient.ConfigureRequest = request =>
        {
            request.Headers["Cookie"] = authentication.Cookie;
            request.Headers["Origin"] = PublicOrigin;
        };

        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var socket = await webSocketClient.ConnectAsync(
            new Uri($"ws://localhost{session.Descriptor.Endpoint}"),
            cancellation.Token).ConfigureAwait(false);

        var payload = Encoding.UTF8.GetBytes("remote-display-roundtrip");
        await socket.SendAsync(
            payload,
            WebSocketMessageType.Binary,
            endOfMessage: true,
            cancellation.Token).ConfigureAwait(false);

        var received = new byte[payload.Length];
        var result = await socket.ReceiveAsync(received, cancellation.Token).ConfigureAwait(false);

        Assert.AreEqual(WebSocketMessageType.Binary, result.MessageType);
        Assert.AreEqual(payload.Length, result.Count);
        CollectionAssert.AreEqual(payload, received);

    }

    private static ServerHost CreateHost(
        string connectionString,
        string providerEndpointTemplate,
        MutableTimeProvider clock) =>
        new(
            connectionString,
            new Dictionary<string, string?>
            {
                ["Remote:Display:ProviderEndpointTemplate"] = providerEndpointTemplate,
                ["Remote:Display:PublicOrigin"] = PublicOrigin,
                ["Remote:Display:GrantLifetimeSeconds"] = "60",
            },
            services =>
            {
                services.RemoveAll<TimeProvider>();
                services.AddSingleton<TimeProvider>(clock);
            });

    private static async Task<AuthenticationResult> SetupAdministratorAsync(HttpClient client)
    {
        using var response = await client.PostAsJsonAsync(
            "/api/v1/auth/setup",
            new InitialAdministratorRequest(
                "admin",
                "Administrator",
                AdministratorPassword)).ConfigureAwait(false);
        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);

        var user = await response.Content
            .ReadFromJsonAsync<AuthenticatedUserResponse>()
            .ConfigureAwait(false)
            ?? throw new AssertFailedException("Initial setup returned no user.");
        var cookie = string.Join(
            "; ",
            response.Headers.GetValues("Set-Cookie")
                .Select(value => value.Split(';', 2, StringSplitOptions.None)[0]));

        Assert.IsFalse(string.IsNullOrWhiteSpace(cookie));
        return new AuthenticationResult(user, cookie);
    }

    private static async Task<DisplaySession> InsertConnectedSessionAsync(
        ServerHost host,
        Guid ownerUserId,
        MutableTimeProvider clock)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var gateway = scope.ServiceProvider.GetRequiredService<RemoteDisplayGateway>();
        var context = scope.ServiceProvider.GetRequiredService<CoreDbContext>();
        var sessionId = Guid.Parse("55555555-5555-4555-8555-555555555555");
        const int revision = 5;
        var descriptor = gateway.Issue(
            sessionId,
            ownerUserId,
            CallerPackageId,
            RuntimeId,
            revision,
            clock.GetUtcNow().AddMinutes(10));

        context.RemoteSessions.Add(new RemoteSessionRow
        {
            Id = sessionId,
            OwnerUserId = ownerUserId,
            CallerPackageId = CallerPackageId,
            OperationKey = "remote-display-endpoint",
            RequestIdentity = new string('a', 64),
            Protocol = "rdp",
            TargetHost = "server.example.test",
            TargetPort = 3389,
            SecretReferenceId = Guid.Parse("66666666-6666-4666-8666-666666666666"),
            ViewportWidth = 1440,
            ViewportHeight = 900,
            DeviceScaleFactor = 1m,
            IdleTimeoutSeconds = 120,
            MaximumSessionSeconds = 600,
            State = RemoteSessionStates.Connected,
            CreatedAtUtc = clock.GetUtcNow().AddMinutes(-1),
            UpdatedAtUtc = clock.GetUtcNow(),
            LastActivityAtUtc = clock.GetUtcNow(),
            ExpiresAtUtc = clock.GetUtcNow().AddMinutes(10),
            ConnectedAtUtc = clock.GetUtcNow(),
            RuntimeId = RuntimeId,
            DisplayKind = descriptor.Kind,
            DisplayContractVersion = descriptor.ContractVersion,
            DisplayEndpoint = descriptor.Endpoint,
            DisplayExpiresAtUtc = descriptor.ExpiresAtUtc,
            Revision = revision,
        });
        await context.SaveChangesAsync().ConfigureAwait(false);
        return new DisplaySession(sessionId, descriptor);
    }

    private static async Task UpdateSessionAsync(
        ServerHost host,
        Guid sessionId,
        Action<RemoteSessionRow> update)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<CoreDbContext>();
        var row = await context.RemoteSessions
            .SingleAsync(candidate => candidate.Id == sessionId)
            .ConfigureAwait(false);
        update(row);
        await context.SaveChangesAsync().ConfigureAwait(false);
    }

    private static HttpRequestMessage DisplayRequest(
        string endpoint,
        string? cookie,
        string origin)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
        if (cookie is not null)
        {
            request.Headers.TryAddWithoutValidation("Cookie", cookie);
        }
        request.Headers.TryAddWithoutValidation("Origin", origin);
        return request;
    }

    private static async Task<PostgreSqlTestDatabase> CreateMigratedDatabaseAsync()
    {
        var database = await PostgreSqlTestDatabase.CreateAsync().ConfigureAwait(false);
        await CoreDatabaseMigrator.MigrateAsync(database.ConnectionString).ConfigureAwait(false);
        return database;
    }

    private sealed record AuthenticationResult(
        AuthenticatedUserResponse User,
        string Cookie);

    private sealed record DisplaySession(
        Guid SessionId,
        RemoteDisplayTransportResponse Descriptor);

    private sealed class MutableTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private DateTimeOffset utcNow = utcNow;

        public override DateTimeOffset GetUtcNow() => this.utcNow;

        internal void Advance(TimeSpan duration) => this.utcNow = this.utcNow.Add(duration);
    }

    private sealed class EchoProvider : IAsyncDisposable
    {
        private readonly WebApplication application;

        private EchoProvider(WebApplication application, string endpointTemplate)
        {
            this.application = application;
            this.EndpointTemplate = endpointTemplate;
        }

        internal string EndpointTemplate { get; }

        internal static async Task<EchoProvider> StartAsync()
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                EnvironmentName = Environments.Development,
            });
            builder.WebHost.ConfigureKestrel(options =>
                options.Listen(IPAddress.Loopback, 0));

            var application = builder.Build();
            application.UseWebSockets();
            application.MapGet(
                "/runtime/{runtimeId}",
                async context =>
                {
                    if (!context.WebSockets.IsWebSocketRequest)
                    {
                        context.Response.StatusCode = StatusCodes.Status400BadRequest;
                        return;
                    }

                    using var socket = await context.WebSockets
                        .AcceptWebSocketAsync()
                        .ConfigureAwait(false);
                    var buffer = new byte[1024];
                    var result = await socket
                        .ReceiveAsync(buffer, context.RequestAborted)
                        .ConfigureAwait(false);
                    await socket.SendAsync(
                        buffer.AsMemory(0, result.Count),
                        result.MessageType,
                        result.EndOfMessage,
                        context.RequestAborted).ConfigureAwait(false);
                });

            await application.StartAsync().ConfigureAwait(false);
            var addresses = application.Services
                .GetRequiredService<IServer>()
                .Features
                .Get<IServerAddressesFeature>()
                ?.Addresses
                ?? throw new InvalidOperationException("Echo provider has no listening address.");
            var address = new Uri(addresses.Single());
            return new EchoProvider(
                application,
                $"ws://127.0.0.1:{address.Port}/runtime/{{runtimeId}}");
        }

        public async ValueTask DisposeAsync()
        {
            await this.application.StopAsync().ConfigureAwait(false);
            await this.application.DisposeAsync().ConfigureAwait(false);
        }
    }
}
