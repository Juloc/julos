using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using JulOS.Contracts.Agents;
using JulOS.Contracts.Authentication;
using JulOS.Infrastructure.Persistence.Core;
using JulOS.Integration.Tests.Persistence;

using Microsoft.AspNetCore.Mvc.Testing;

namespace JulOS.Integration.Tests.Agents;

[TestClass]
[DoNotParallelize]
public sealed class AgentEndpointTests
{
    private static readonly WebApplicationFactoryClientOptions ClientOptions = new()
    {
        BaseAddress = new Uri("https://localhost"),
        AllowAutoRedirect = false,
        HandleCookies = true,
    };

    [TestMethod]
    public async Task LifecycleCoversEnrollmentTelemetryCommandsAndRevocation()
    {
        await using var database = await CreateMigratedDatabaseAsync().ConfigureAwait(false);
        using var host = new ServerHost(database.ConnectionString);
        using var client = host.CreateClient(ClientOptions);

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
            new CreateAgentEnrollmentTokenRequest("Primary homelab host", 10))
            .ConfigureAwait(false);
        Assert.AreEqual(HttpStatusCode.OK, tokenResponse.StatusCode);
        var token = await tokenResponse.Content
            .ReadFromJsonAsync<AgentEnrollmentTokenResponse>()
            .ConfigureAwait(false);
        Assert.IsNotNull(token);
        Assert.IsTrue(token.ExpiresAtUtc > DateTimeOffset.UtcNow);

        var credential = CreateCredential();
        var enrollmentRequest = new RedeemAgentEnrollmentRequest(
            token.Token,
            credential,
            "homelab-agent",
            "machine-identity-001",
            "Debian 13",
            "x86_64",
            "1.0.0");
        using var enrollmentResponse = await client.PostAsJsonAsync(
            "/api/v1/agent/enroll",
            enrollmentRequest)
            .ConfigureAwait(false);
        Assert.AreEqual(HttpStatusCode.OK, enrollmentResponse.StatusCode);
        var enrollment = await enrollmentResponse.Content
            .ReadFromJsonAsync<RedeemAgentEnrollmentResponse>()
            .ConfigureAwait(false);
        Assert.IsNotNull(enrollment);
        Assert.AreEqual(credential, enrollment.Credential);
        Assert.AreEqual(30, enrollment.HeartbeatIntervalSeconds);
        Assert.AreEqual(5, enrollment.CommandPollIntervalSeconds);

        using var retryResponse = await client.PostAsJsonAsync(
            "/api/v1/agent/enroll",
            enrollmentRequest)
            .ConfigureAwait(false);
        Assert.AreEqual(HttpStatusCode.OK, retryResponse.StatusCode);
        var retriedEnrollment = await retryResponse.Content
            .ReadFromJsonAsync<RedeemAgentEnrollmentResponse>()
            .ConfigureAwait(false);
        Assert.IsNotNull(retriedEnrollment);
        Assert.AreEqual(enrollment.AgentId, retriedEnrollment.AgentId);
        Assert.AreEqual(credential, retriedEnrollment.Credential);

        using var reusedToken = await client.PostAsJsonAsync(
            "/api/v1/agent/enroll",
            enrollmentRequest with { MachineIdentity = "machine-identity-002" })
            .ConfigureAwait(false);
        Assert.AreEqual(HttpStatusCode.BadRequest, reusedToken.StatusCode);
        Assert.AreEqual(
            "agent.enrollment_token_reused",
            await ReadErrorCodeAsync(reusedToken).ConfigureAwait(false));

        var observedAt = DateTimeOffset.UtcNow;
        var heartbeat = new AgentHeartbeatRequest(
            "1.0.0",
            [
                new AgentCapabilityContract(
                    "system.metrics",
                    1,
                    Enabled: true,
                    MetadataVersion: 1,
                    JsonSerializer.SerializeToElement(new { platform = "linux" })),
            ],
            observedAt);
        using var heartbeatResponse = await SendAgentRequestAsync(
            client,
            HttpMethod.Post,
            "/api/v1/agent/heartbeat",
            enrollment,
            heartbeat)
            .ConfigureAwait(false);
        Assert.AreEqual(HttpStatusCode.OK, heartbeatResponse.StatusCode);
        var connected = await heartbeatResponse.Content
            .ReadFromJsonAsync<AgentResponse>()
            .ConfigureAwait(false);
        Assert.IsNotNull(connected);
        Assert.AreEqual("connected", connected.State);
        Assert.IsNotNull(connected.LastSeenAtUtc);

        var metrics = new AgentMetricBatchRequest(
            [
                new AgentMetricContract(
                    "cpu.utilization",
                    42.5,
                    "%",
                    new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["host"] = "primary",
                    },
                    observedAt),
            ]);
        using var metricsResponse = await SendAgentRequestAsync(
            client,
            HttpMethod.Post,
            "/api/v1/agent/metrics",
            enrollment,
            metrics)
            .ConfigureAwait(false);
        Assert.AreEqual(HttpStatusCode.NoContent, metricsResponse.StatusCode);

        var from = Uri.EscapeDataString(
            observedAt.AddMinutes(-1).ToString("O", CultureInfo.InvariantCulture));
        var to = Uri.EscapeDataString(
            observedAt.AddMinutes(1).ToString("O", CultureInfo.InvariantCulture));
        var series = await client.GetFromJsonAsync<AgentMetricSeriesResponse[]>(
            $"/api/v1/agents/{enrollment.AgentId:D}/metrics?fromUtc={from}&toUtc={to}")
            .ConfigureAwait(false);
        Assert.IsNotNull(series);
        Assert.AreEqual(1, series.Length);
        Assert.AreEqual("cpu.utilization", series[0].Name);
        Assert.AreEqual(1, series[0].Points.Count);
        Assert.AreEqual(42.5, series[0].Points[0].Value.GetValueOrDefault());
        Assert.IsTrue(
            (series[0].Points[0].ObservedAtUtc - observedAt).Duration() < TimeSpan.FromMilliseconds(1));

        using var commandResponse = await SendAdministratorMutationAsync(
            client,
            $"/api/v1/agents/{enrollment.AgentId:D}/commands",
            new CreateAgentCommandRequest(
                "diagnostics-001",
                "diagnostics.snapshot",
                JsonSerializer.SerializeToElement(new { version = 1 }),
                60))
            .ConfigureAwait(false);
        Assert.AreEqual(HttpStatusCode.Accepted, commandResponse.StatusCode);
        var queued = await commandResponse.Content
            .ReadFromJsonAsync<AgentCommandResponse>()
            .ConfigureAwait(false);
        Assert.IsNotNull(queued);
        Assert.AreEqual("queued", queued.State);

        using var acquireResponse = await SendAgentRequestAsync(
            client,
            HttpMethod.Get,
            "/api/v1/agent/commands/next",
            enrollment)
            .ConfigureAwait(false);
        Assert.AreEqual(HttpStatusCode.OK, acquireResponse.StatusCode);
        var running = await acquireResponse.Content
            .ReadFromJsonAsync<AgentCommandResponse>()
            .ConfigureAwait(false);
        Assert.IsNotNull(running);
        Assert.AreEqual(queued.CommandId, running.CommandId);
        Assert.AreEqual("running", running.State);

        using var completionResponse = await SendAgentRequestAsync(
            client,
            HttpMethod.Post,
            $"/api/v1/agent/commands/{running.CommandId:D}/complete",
            enrollment,
            new CompleteAgentCommandRequest(
                Succeeded: true,
                JsonSerializer.SerializeToElement(new { status = "ok" }),
                ErrorCode: null,
                running.Revision))
            .ConfigureAwait(false);
        Assert.AreEqual(HttpStatusCode.OK, completionResponse.StatusCode);
        var completed = await completionResponse.Content
            .ReadFromJsonAsync<AgentCommandResponse>()
            .ConfigureAwait(false);
        Assert.IsNotNull(completed);
        Assert.AreEqual("succeeded", completed.State);
        Assert.IsNotNull(completed.CompletedAtUtc);

        using var emptyQueue = await SendAgentRequestAsync(
            client,
            HttpMethod.Get,
            "/api/v1/agent/commands/next",
            enrollment)
            .ConfigureAwait(false);
        Assert.AreEqual(HttpStatusCode.NoContent, emptyQueue.StatusCode);

        var current = await client.GetFromJsonAsync<AgentResponse>(
            $"/api/v1/agents/{enrollment.AgentId:D}")
            .ConfigureAwait(false);
        Assert.IsNotNull(current);

        using var revokeResponse = await SendAdministratorMutationAsync(
            client,
            $"/api/v1/agents/{enrollment.AgentId:D}/revoke?revision={current.Revision.ToString(CultureInfo.InvariantCulture)}")
            .ConfigureAwait(false);
        Assert.AreEqual(HttpStatusCode.OK, revokeResponse.StatusCode);
        var revoked = await revokeResponse.Content
            .ReadFromJsonAsync<AgentResponse>()
            .ConfigureAwait(false);
        Assert.IsNotNull(revoked);
        Assert.AreEqual("revoked", revoked.State);
        Assert.IsNotNull(revoked.RevokedAtUtc);

        using var rejectedHeartbeat = await SendAgentRequestAsync(
            client,
            HttpMethod.Post,
            "/api/v1/agent/heartbeat",
            enrollment,
            heartbeat)
            .ConfigureAwait(false);
        Assert.AreEqual(HttpStatusCode.Unauthorized, rejectedHeartbeat.StatusCode);
        Assert.AreEqual(
            "agent.authentication_failed",
            await ReadErrorCodeAsync(rejectedHeartbeat).ConfigureAwait(false));
    }

    private static async Task<HttpResponseMessage> SendAdministratorMutationAsync(
        HttpClient client,
        string path,
        object? request = null)
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
        object? request = null)
    {
        var message = new HttpRequestMessage(method, path);
        message.Headers.Add("X-JulOS-Agent-Id", enrollment.AgentId.ToString("D"));
        message.Headers.Add("X-JulOS-Agent-Credential", enrollment.Credential);
        AddJsonContent(message, request);
        return await client.SendAsync(message).ConfigureAwait(false);
    }

    private static void AddJsonContent(HttpRequestMessage message, object? request)
    {
        if (request is null)
        {
            return;
        }

        message.Content = new StringContent(
            JsonSerializer.Serialize(request, request.GetType()),
            Encoding.UTF8,
            "application/json");
    }

    private static async Task<string> ReadErrorCodeAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync().ConfigureAwait(false));
        return document.RootElement.GetProperty("code").GetString()
            ?? throw new AssertFailedException("The error response has no code.");
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
