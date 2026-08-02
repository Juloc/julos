using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using JulOS.Application.Events;
using JulOS.Contracts.Authentication;
using JulOS.Contracts.Events;
using JulOS.Infrastructure.Persistence.Core;
using JulOS.Integration.Tests.Persistence;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace JulOS.Integration.Tests.Events;

[TestClass]
[DoNotParallelize]
public sealed class RealtimeEventTests
{
    private const string AdministratorPassword = "Valid-Initial-Password-42!";

    private static readonly WebApplicationFactoryClientOptions ClientOptions = new()
    {
        BaseAddress = new Uri("https://localhost"),
        AllowAutoRedirect = false,
        HandleCookies = true,
    };

    [TestMethod]
    public async Task HubNegotiationRequiresAuthenticationAndPublisherAcceptsVersionedEvents()
    {
        await using var database = await CreateMigratedDatabaseAsync().ConfigureAwait(false);
        using var host = new ServerHost(database.ConnectionString);
        using var client = host.CreateClient(ClientOptions);

        using var anonymous = await client.PostAsync(
            RealtimeEventContract.HubPath + "/negotiate?negotiateVersion=1",
            content: null).ConfigureAwait(false);
        Assert.AreEqual(HttpStatusCode.Unauthorized, anonymous.StatusCode);

        using var setup = await client.PostAsJsonAsync(
            "/api/v1/auth/setup",
            new InitialAdministratorRequest(
                "admin",
                "Administrator",
                AdministratorPassword)).ConfigureAwait(false);
        Assert.AreEqual(HttpStatusCode.Created, setup.StatusCode);

        using var negotiation = await client.PostAsync(
            RealtimeEventContract.HubPath + "/negotiate?negotiateVersion=1",
            content: null).ConfigureAwait(false);
        Assert.AreEqual(HttpStatusCode.OK, negotiation.StatusCode);

        using var negotiationBody = JsonDocument.Parse(
            await negotiation.Content.ReadAsStringAsync().ConfigureAwait(false));
        Assert.IsTrue(negotiationBody.RootElement.TryGetProperty("connectionToken", out var token));
        Assert.IsFalse(string.IsNullOrWhiteSpace(token.GetString()));
        Assert.IsTrue(negotiationBody.RootElement
            .GetProperty("availableTransports")
            .EnumerateArray()
            .Any(transport =>
                string.Equals(
                    transport.GetProperty("transport").GetString(),
                    "WebSockets",
                    StringComparison.Ordinal)
                && transport.GetProperty("transferFormats")
                    .EnumerateArray()
                    .Any(format => string.Equals(
                        format.GetString(),
                        "Text",
                        StringComparison.Ordinal))));

        using var payloadDocument = JsonDocument.Parse("{\"changed\":true}");
        var publisher = host.Services.GetRequiredService<IRealtimeEventPublisher>();
        await publisher.PublishAsync(new RealtimeEventNotification(
            "problem.changed",
            "api010-test",
            "problem-1",
            Revision: 3,
            payloadDocument.RootElement)).ConfigureAwait(false);

        await Assert.ThrowsExactlyAsync<ArgumentException>(() => publisher.PublishAsync(
            new RealtimeEventNotification(
                "invalid event type",
                "api010-test",
                "problem-1",
                Revision: 3,
                payloadDocument.RootElement))).ConfigureAwait(false);
    }

    private static async Task<PostgreSqlTestDatabase> CreateMigratedDatabaseAsync()
    {
        var database = await PostgreSqlTestDatabase.CreateAsync().ConfigureAwait(false);
        await CoreDatabaseMigrator.MigrateAsync(database.ConnectionString).ConfigureAwait(false);
        return database;
    }
}
