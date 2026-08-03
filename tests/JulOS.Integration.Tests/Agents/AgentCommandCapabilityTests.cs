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
public sealed class AgentCommandCapabilityTests
{
    private static readonly WebApplicationFactoryClientOptions ClientOptions = new()
    {
        BaseAddress = new Uri("https://localhost"),
        AllowAutoRedirect = false,
        HandleCookies = true,
    };

    [TestMethod]
    public async Task CommandQueueRequiresCurrentValidAdvertisement()
    {
        await using var database = await CreateMigratedDatabaseAsync().ConfigureAwait(false);
        using var host = new ServerHost(database.ConnectionString);
        using var client = host.CreateClient(ClientOptions);
        var enrollment = await CreateEnrolledAgentAsync(client).ConfigureAwait(false);

        using var unavailable = await CreateCommandAsync(
            client,
            enrollment.AgentId,
            "missing-capability",
            "diagnostics.snapshot").ConfigureAwait(false);
        Assert.AreEqual(HttpStatusCode.BadRequest, unavailable.StatusCode);
        Assert.AreEqual(
            "agent.command_capability_unavailable",
            await ReadErrorCodeAsync(unavailable).ConfigureAwait(false));

        using var enabledHeartbeat = await SendHeartbeatAsync(
            client,
            enrollment,
            enabled: true,
            capabilityVersion: 1,
            metadataVersion: 1,
            JsonSerializer.SerializeToElement(new
            {
                commands = AgentTestData.DiagnosticCommands,
            })).ConfigureAwait(false);
        Assert.AreEqual(HttpStatusCode.OK, enabledHeartbeat.StatusCode);

        using var accepted = await CreateCommandAsync(
            client,
            enrollment.AgentId,
            "advertised-command",
            "diagnostics.snapshot").ConfigureAwait(false);
        Assert.AreEqual(HttpStatusCode.Accepted, accepted.StatusCode);

        using var unadvertised = await CreateCommandAsync(
            client,
            enrollment.AgentId,
            "unadvertised-command",
            "service.restart").ConfigureAwait(false);
        Assert.AreEqual(HttpStatusCode.BadRequest, unadvertised.StatusCode);
        Assert.AreEqual(
            "agent.command_not_advertised",
            await ReadErrorCodeAsync(unadvertised).ConfigureAwait(false));

        using var disabledHeartbeat = await SendHeartbeatAsync(
            client,
            enrollment,
            enabled: false,
            capabilityVersion: 1,
            metadataVersion: 1,
            JsonSerializer.SerializeToElement(new
            {
                commands = AgentTestData.DiagnosticCommands,
            })).ConfigureAwait(false);
        Assert.AreEqual(HttpStatusCode.OK, disabledHeartbeat.StatusCode);

        using var disabled = await CreateCommandAsync(
            client,
            enrollment.AgentId,
            "disabled-capability",
            "diagnostics.snapshot").ConfigureAwait(false);
        Assert.AreEqual(HttpStatusCode.BadRequest, disabled.StatusCode);
        Assert.AreEqual(
            "agent.command_capability_disabled",
            await ReadErrorCodeAsync(disabled).ConfigureAwait(false));

        using var incompatibleHeartbeat = await SendHeartbeatAsync(
            client,
            enrollment,
            enabled: true,
            capabilityVersion: 2,
            metadataVersion: 1,
            JsonSerializer.SerializeToElement(new
            {
                commands = AgentTestData.DiagnosticCommands,
            })).ConfigureAwait(false);
        Assert.AreEqual(HttpStatusCode.OK, incompatibleHeartbeat.StatusCode);

        using var incompatible = await CreateCommandAsync(
            client,
            enrollment.AgentId,
            "incompatible-capability",
            "diagnostics.snapshot").ConfigureAwait(false);
        Assert.AreEqual(HttpStatusCode.BadRequest, incompatible.StatusCode);
        Assert.AreEqual(
            "agent.command_capability_incompatible",
            await ReadErrorCodeAsync(incompatible).ConfigureAwait(false));

        using var malformedHeartbeat = await SendHeartbeatAsync(
            client,
            enrollment,
            enabled: true,
            capabilityVersion: 1,
            metadataVersion: 1,
            JsonSerializer.SerializeToElement(new
            {
                commands = "diagnostics.snapshot",
            })).ConfigureAwait(false);
        Assert.AreEqual(HttpStatusCode.OK, malformedHeartbeat.StatusCode);

        using var malformed = await CreateCommandAsync(
            client,
            enrollment.AgentId,
            "malformed-capability",
            "diagnostics.snapshot").ConfigureAwait(false);
        Assert.AreEqual(HttpStatusCode.BadRequest, malformed.StatusCode);
        Assert.AreEqual(
            "agent.command_capability_invalid",
            await ReadErrorCodeAsync(malformed).ConfigureAwait(false));
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
            new CreateAgentEnrollmentTokenRequest("Command capability test", 10))
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
                "command-test-agent",
                "machine-identity-command-test",
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

    private static Task<HttpResponseMessage> SendHeartbeatAsync(
        HttpClient client,
        RedeemAgentEnrollmentResponse enrollment,
        bool enabled,
        int capabilityVersion,
        int metadataVersion,
        JsonElement metadata) =>
        SendAgentRequestAsync(
            client,
            HttpMethod.Post,
            "/api/v1/agent/heartbeat",
            enrollment,
            new AgentHeartbeatRequest(
                "1.0.0",
                [
                    new AgentCapabilityContract(
                        "agent.commands.core",
                        capabilityVersion,
                        enabled,
                        metadataVersion,
                        metadata),
                ],
                DateTimeOffset.UtcNow));

    private static Task<HttpResponseMessage> CreateCommandAsync(
        HttpClient client,
        Guid agentId,
        string operationKey,
        string commandType) =>
        SendAdministratorMutationAsync(
            client,
            $"/api/v1/agents/{agentId:D}/commands",
            new CreateAgentCommandRequest(
                operationKey,
                commandType,
                JsonSerializer.SerializeToElement(new { version = 1 }),
                60));

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
