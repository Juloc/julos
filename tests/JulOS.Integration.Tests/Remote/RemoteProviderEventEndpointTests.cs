using System.Net;
using System.Net.Http.Json;

using JulOS.Application.Remote;
using JulOS.Contracts.Remote;
using JulOS.Infrastructure.Remote;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace JulOS.Integration.Tests.Remote;

[TestClass]
public sealed class RemoteProviderEventEndpointTests
{
    private const string UnreachableDatabase =
        "Host=127.0.0.1;Port=9;Database=julos_tests;Username=julos;Password=test-only;Timeout=1;Command Timeout=1";
    private static readonly WebApplicationFactoryClientOptions ClientOptions = new()
    {
        BaseAddress = new Uri("https://localhost"),
        AllowAutoRedirect = false,
        HandleCookies = false,
    };

    [TestMethod]
    public async Task ValidTokenRoutesConnectedEvent()
    {
        var connections = new RecordingConnectionService();
        using var host = CreateHost(connections);
        using var client = host.CreateClient(ClientOptions);
        var sessionId = Guid.Parse("99999999-9999-4999-8999-999999999999");
        const string runtimeId = "remote-99999999999949998999999999999999";
        var token = Issue(host, sessionId, runtimeId);

        using var request = ProviderRequest(
            token,
            new RemoteProviderEventRequest(
                sessionId,
                runtimeId,
                RemoteProviderEventContract.Connected,
                ExpectedRevision: 4,
                FailureCode: null,
                FailureDetail: null,
                Retryable: false));
        using var response = await client.SendAsync(request).ConfigureAwait(false);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.IsNotNull(connections.Connected);
        Assert.AreEqual(sessionId, connections.Connected.SessionId);
        Assert.AreEqual(runtimeId, connections.Connected.RuntimeId);
        Assert.AreEqual(4, connections.Connected.ExpectedRevision);
    }

    [TestMethod]
    public async Task InvalidTokenIsRejectedBeforeMutation()
    {
        var connections = new RecordingConnectionService();
        using var host = CreateHost(connections);
        using var client = host.CreateClient(ClientOptions);
        var sessionId = Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa");
        const string runtimeId = "remote-aaaaaaaaaaaa4aaa8aaaaaaaaaaaaaaa";

        using var request = ProviderRequest(
            "v1.1.invalid",
            new RemoteProviderEventRequest(
                sessionId,
                runtimeId,
                RemoteProviderEventContract.Connected,
                ExpectedRevision: 4,
                FailureCode: null,
                FailureDetail: null,
                Retryable: false));
        using var response = await client.SendAsync(request).ConfigureAwait(false);

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.IsNull(connections.Connected);
        Assert.IsNull(connections.Failed);
        Assert.IsNull(connections.Activity);
    }

    [TestMethod]
    public async Task FailedAndActivityEventsUseTheSameAuthenticatedEndpoint()
    {
        var connections = new RecordingConnectionService();
        using var host = CreateHost(connections);
        using var client = host.CreateClient(ClientOptions);
        var sessionId = Guid.Parse("bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb");
        const string runtimeId = "remote-bbbbbbbbbbbb4bbb8bbbbbbbbbbbbbbb";
        var token = Issue(host, sessionId, runtimeId);

        using (var failedRequest = ProviderRequest(
            token,
            new RemoteProviderEventRequest(
                sessionId,
                runtimeId,
                RemoteProviderEventContract.Failed,
                ExpectedRevision: 5,
                RemoteSessionFailureCodes.ConnectionLost,
                "The provider connection closed unexpectedly.",
                Retryable: true)))
        using (var failedResponse = await client.SendAsync(failedRequest).ConfigureAwait(false))
        {
            Assert.AreEqual(HttpStatusCode.OK, failedResponse.StatusCode);
        }
        Assert.IsNotNull(connections.Failed);
        Assert.AreEqual(RemoteSessionFailureCodes.ConnectionLost, connections.Failed.Code);

        using (var activityRequest = ProviderRequest(
            token,
            new RemoteProviderEventRequest(
                sessionId,
                runtimeId,
                RemoteProviderEventContract.Activity,
                ExpectedRevision: 0,
                FailureCode: null,
                FailureDetail: null,
                Retryable: false)))
        using (var activityResponse = await client.SendAsync(activityRequest).ConfigureAwait(false))
        {
            Assert.AreEqual(HttpStatusCode.NoContent, activityResponse.StatusCode);
        }
        Assert.IsNotNull(connections.Activity);
        Assert.AreEqual(sessionId, connections.Activity.SessionId);
    }

    private static ServerHost CreateHost(RecordingConnectionService connections) =>
        new(
            UnreachableDatabase,
            new Dictionary<string, string?>(),
            services =>
            {
                services.RemoveAll<IRemoteSessionConnectionService>();
                services.AddSingleton<IRemoteSessionConnectionService>(connections);
            });

    private static string Issue(ServerHost host, Guid sessionId, string runtimeId)
    {
        var authenticator = host.Services.GetRequiredService<RemoteProviderCallbackAuthenticator>();
        return authenticator.Issue(sessionId, runtimeId, DateTimeOffset.UtcNow.AddMinutes(5));
    }

    private static HttpRequestMessage ProviderRequest(
        string token,
        RemoteProviderEventRequest payload)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/v1/internal/remote/provider-events")
        {
            Content = JsonContent.Create(payload),
        };
        request.Headers.TryAddWithoutValidation(RemoteProviderEventContract.TokenHeader, token);
        return request;
    }

    private sealed class RecordingConnectionService : IRemoteSessionConnectionService
    {
        internal ConnectRemoteSessionCommand? Connected { get; private set; }

        internal FailRemoteSessionCommand? Failed { get; private set; }

        internal RecordRemoteSessionActivityCommand? Activity { get; private set; }

        public Task<RemoteSessionResponse> ConnectAsync(
            ConnectRemoteSessionCommand command,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            this.Connected = command;
            return Task.FromResult(Response(command.SessionId, command.ExpectedRevision + 1));
        }

        public Task<RemoteSessionResponse> FailAsync(
            FailRemoteSessionCommand command,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            this.Failed = command;
            return Task.FromResult(Response(command.SessionId, command.ExpectedRevision + 1));
        }

        public Task RecordActivityAsync(
            RecordRemoteSessionActivityCommand command,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            this.Activity = command;
            return Task.CompletedTask;
        }

        private static RemoteSessionResponse Response(Guid sessionId, long revision) =>
            new(
                sessionId,
                "provider-event-test",
                "request-identity",
                "rdp",
                new RemoteTargetContract("server.example.test", 3389),
                RemoteSessionStates.Connected,
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow,
                EndedAtUtc: null,
                Display: null,
                Failure: null,
                revision);
    }
}
