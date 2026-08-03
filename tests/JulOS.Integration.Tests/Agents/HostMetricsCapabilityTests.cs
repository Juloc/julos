using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using JulOS.Contracts.Agents;
using JulOS.Contracts.Authentication;
using JulOS.Contracts.Packages;
using JulOS.Infrastructure.Packages;
using JulOS.Infrastructure.Persistence.Core;
using JulOS.Integration.Tests.Persistence;
using JulOS.PackageSdk;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace JulOS.Integration.Tests.Agents;

[TestClass]
[DoNotParallelize]
public sealed class HostMetricsCapabilityTests
{
    private const string PackageId = "de.juloc.julos.hostmetrics";
    private static readonly WebApplicationFactoryClientOptions ClientOptions = new()
    {
        BaseAddress = new Uri("https://localhost"),
        AllowAutoRedirect = false,
        HandleCookies = true,
    };

    [TestMethod]
    public async Task PersistedMetricsResolveThroughAuthorizedBroker()
    {
        await using var database = await CreateMigratedDatabaseAsync().ConfigureAwait(false);
        using var host = new ServerHost(database.ConnectionString);
        using var client = host.CreateClient(ClientOptions);
        var enrollment = await CreateEnrolledAgentAsync(client).ConfigureAwait(false);
        var observedAt = DateTimeOffset.UtcNow;

        using var heartbeat = await SendAgentRequestAsync(
            client,
            HttpMethod.Post,
            "/api/v1/agent/heartbeat",
            enrollment,
            new AgentHeartbeatRequest(
                "1.0.0",
                [
                    new AgentCapabilityContract(
                        "system.metrics",
                        1,
                        Enabled: true,
                        MetadataVersion: 1,
                        JsonSerializer.SerializeToElement(new { platform = "linux" })),
                ],
                observedAt)).ConfigureAwait(false);
        Assert.AreEqual(HttpStatusCode.OK, heartbeat.StatusCode);

        using var metrics = await SendAgentRequestAsync(
            client,
            HttpMethod.Post,
            "/api/v1/agent/metrics",
            enrollment,
            new AgentMetricBatchRequest(
                [
                    new AgentMetricContract(
                        "host.cpu.utilization",
                        0.42,
                        "ratio",
                        new Dictionary<string, string>(StringComparer.Ordinal),
                        observedAt),
                    new AgentMetricContract(
                        "host.memory.used_bytes",
                        null,
                        "bytes",
                        new Dictionary<string, string>(StringComparer.Ordinal),
                        observedAt),
                ])).ConfigureAwait(false);
        Assert.AreEqual(HttpStatusCode.NoContent, metrics.StatusCode);

        using var scope = host.Services.CreateScope();
        var broker = scope.ServiceProvider.GetRequiredService<CapabilityBroker>();
        broker.SetPackageGrants(PackageId, [HostMetricsCapabilityContract.Name]);
        var request = new CapabilityRequest(
            HostMetricsCapabilityContract.Name,
            HostMetricsCapabilityContract.Version,
            HostMetricsCapabilityContract.LatestOperation,
            Guid.NewGuid().ToString("N"),
            JsonSerializer.SerializeToElement(new HostMetricsReadRequest(
                enrollment.AgentId,
                MaximumAgeSeconds: 120)),
            DateTimeOffset.UtcNow.AddSeconds(5));

        var response = await broker.InvokeAsync(
            PackageId,
            request,
            CancellationToken.None).ConfigureAwait(false);

        Assert.IsTrue(response.Succeeded);
        var snapshot = response.Payload.Deserialize<HostMetricsSnapshotResponse>();
        Assert.IsNotNull(snapshot);
        Assert.AreEqual(HostMetricsSnapshotStates.Live, snapshot.State);
        Assert.IsFalse(snapshot.Stale);
        Assert.AreEqual(
            0.42,
            snapshot.Metrics
                .Single(metric => metric.Name == "host.cpu.utilization")
                .Value
                .GetValueOrDefault(),
            0.0001);
        Assert.IsNull(
            snapshot.Metrics.Single(metric => metric.Name == "host.memory.used_bytes").Value);

        var failure = await Assert.ThrowsExactlyAsync<CapabilityBrokerException>(
            async () =>
            {
                _ = await broker.InvokeAsync(
                    "de.juloc.untrusted",
                    request,
                    CancellationToken.None).ConfigureAwait(false);
            }).ConfigureAwait(false);
        Assert.AreEqual("capability.permission_denied", failure.Code);
    }

    private static async Task<RedeemAgentEnrollmentResponse> CreateEnrolledAgentAsync(HttpClient client)
    {
        using var setup = await client.PostAsJsonAsync(
            "/api/v1/auth/setup",
            new InitialAdministratorRequest(
                "admin",
                "Administrator",
                "Valid-Initial-Password-42!"))
            .ConfigureAwait(false);
        Assert.AreEqual(HttpStatusCode.Created, setup.StatusCode);

        using var tokenResponse = await SendAdministratorMutationAsync(
            client,
            "/api/v1/agents/enrollment-tokens",
            new CreateAgentEnrollmentTokenRequest("Host Metrics integration test", 10))
            .ConfigureAwait(false);
        Assert.AreEqual(HttpStatusCode.OK, tokenResponse.StatusCode);
        var token = await tokenResponse.Content
            .ReadFromJsonAsync<AgentEnrollmentTokenResponse>()
            .ConfigureAwait(false);
        Assert.IsNotNull(token);

        using var enrollmentResponse = await client.PostAsJsonAsync(
            "/api/v1/agent/enroll",
            new RedeemAgentEnrollmentRequest(
                token.Token,
                CreateCredential(),
                "host-metrics-agent",
                "machine-identity-host-metrics",
                "Debian 13",
                "x86_64",
                "1.0.0"))
            .ConfigureAwait(false);
        Assert.AreEqual(HttpStatusCode.OK, enrollmentResponse.StatusCode);
        return await enrollmentResponse.Content
            .ReadFromJsonAsync<RedeemAgentEnrollmentResponse>()
            .ConfigureAwait(false)
            ?? throw new AssertFailedException("Enrollment response is empty.");
    }

    private static async Task<HttpResponseMessage> SendAdministratorMutationAsync(
        HttpClient client,
        string path,
        object request)
    {
        var antiforgery = await client
            .GetFromJsonAsync<AntiforgeryTokenResponse>("/api/v1/auth/antiforgery")
            .ConfigureAwait(false);
        Assert.IsNotNull(antiforgery);
        var message = new HttpRequestMessage(HttpMethod.Post, path);
        message.Headers.Add(antiforgery.HeaderName, antiforgery.Token);
        AddJsonContent(message, request);
        return await client.SendAsync(message).ConfigureAwait(false);
    }

    private static async Task<HttpResponseMessage> SendAgentRequestAsync(
        HttpClient client,
        HttpMethod method,
        string path,
        RedeemAgentEnrollmentResponse enrollment,
        object request)
    {
        var message = new HttpRequestMessage(method, path);
        message.Headers.Add("X-JulOS-Agent-Id", enrollment.AgentId.ToString("D"));
        message.Headers.Add("X-JulOS-Agent-Credential", enrollment.Credential);
        AddJsonContent(message, request);
        return await client.SendAsync(message).ConfigureAwait(false);
    }

    private static void AddJsonContent(HttpRequestMessage message, object request)
    {
        message.Content = new StringContent(
            JsonSerializer.Serialize(request, request.GetType()),
            Encoding.UTF8,
            "application/json");
    }

    private static string CreateCredential()
    {
        var bytes = RandomNumberGenerator.GetBytes(48);
        try
        {
            return Convert.ToBase64String(bytes)
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }

    private static async Task<PostgreSqlTestDatabase> CreateMigratedDatabaseAsync()
    {
        var database = await PostgreSqlTestDatabase.CreateAsync().ConfigureAwait(false);
        await CoreDatabaseMigrator.MigrateAsync(database.ConnectionString).ConfigureAwait(false);
        return database;
    }
}
