using System.Net;
using System.Net.Http.Json;

using JulOS.Contracts.Auditing;
using JulOS.Contracts.Authentication;
using JulOS.Contracts.Authorization;
using JulOS.Infrastructure.Persistence.Core;
using JulOS.Integration.Tests.Persistence;

using Microsoft.AspNetCore.Mvc.Testing;

namespace JulOS.Integration.Tests.Auditing;

[TestClass]
[DoNotParallelize]
public sealed class AuditTests
{
    private const string AdministratorPassword = "Valid-Initial-Password-42!";
    private const string InvalidPassword = "Invalid-Password-That-Must-Never-Be-Audited!";

    private static readonly WebApplicationFactoryClientOptions ClientOptions = new()
    {
        BaseAddress = new Uri("https://localhost"),
        AllowAutoRedirect = false,
        HandleCookies = true,
    };

    [TestMethod]
    public async Task QueryIsProtectedCursorPagedAndSanitized()
    {
        await using var database = await CreateMigratedDatabaseAsync().ConfigureAwait(false);
        using var host = new ServerHost(database.ConnectionString);
        using var client = host.CreateClient(ClientOptions);

        using var anonymous = await client.GetAsync("/api/v1/audit-events").ConfigureAwait(false);
        Assert.AreEqual(HttpStatusCode.Unauthorized, anonymous.StatusCode);

        _ = await SetupAdministratorAsync(client).ConfigureAwait(false);
        var antiforgery = await ReadAntiforgeryAsync(client).ConfigureAwait(false);

        using var firstRole = await SendMutationAsync(
            client,
            HttpMethod.Post,
            "/api/v1/authorization/roles",
            new CreateAuthorizationRoleRequest("Operators", "Infrastructure operators."),
            antiforgery).ConfigureAwait(false);
        Assert.AreEqual(HttpStatusCode.Created, firstRole.StatusCode);

        using var secondRole = await SendMutationAsync(
            client,
            HttpMethod.Post,
            "/api/v1/authorization/roles",
            new CreateAuthorizationRoleRequest("Auditors", "Read-only audit reviewers."),
            antiforgery).ConfigureAwait(false);
        Assert.AreEqual(HttpStatusCode.Created, secondRole.StatusCode);

        var firstPage = await client
            .GetFromJsonAsync<AuditEventPageResponse>("/api/v1/audit-events?limit=2")
            .ConfigureAwait(false);
        Assert.IsNotNull(firstPage);
        Assert.AreEqual(2, firstPage.Events.Count);
        Assert.IsFalse(string.IsNullOrWhiteSpace(firstPage.NextCursor));

        var secondPage = await client.GetFromJsonAsync<AuditEventPageResponse>(
            "/api/v1/audit-events?limit=2&cursor="
            + Uri.EscapeDataString(firstPage.NextCursor)).ConfigureAwait(false);
        Assert.IsNotNull(secondPage);
        Assert.IsTrue(secondPage.Events.Count >= 1);

        var firstIds = firstPage.Events.Select(auditEvent => auditEvent.AuditEventId).ToHashSet();
        Assert.IsFalse(secondPage.Events.Any(auditEvent => firstIds.Contains(auditEvent.AuditEventId)));

        var combined = firstPage.Events.Concat(secondPage.Events).ToArray();
        Assert.IsTrue(combined.Any(auditEvent =>
            string.Equals(auditEvent.Action, "authentication.setup", StringComparison.Ordinal)));
        Assert.IsTrue(combined.Count(auditEvent =>
            string.Equals(auditEvent.Action, "authorization.role.create", StringComparison.Ordinal)) >= 2);

        var serialized = string.Join(
            '\n',
            combined.Select(auditEvent => string.Join(
                '|',
                auditEvent.Summary,
                auditEvent.SafeDetails,
                auditEvent.TargetId)));
        Assert.IsFalse(serialized.Contains(AdministratorPassword, StringComparison.Ordinal));

        using var invalidCursor = await client
            .GetAsync("/api/v1/audit-events?cursor=not-a-valid-cursor")
            .ConfigureAwait(false);
        Assert.AreEqual(HttpStatusCode.BadRequest, invalidCursor.StatusCode);
    }

    [TestMethod]
    public async Task DeniedAndSuccessfulLoginsAreAuditedWithoutCredentialValues()
    {
        await using var database = await CreateMigratedDatabaseAsync().ConfigureAwait(false);
        using var host = new ServerHost(database.ConnectionString);
        using var client = host.CreateClient(ClientOptions);

        var administrator = await SetupAdministratorAsync(client).ConfigureAwait(false);
        var antiforgery = await ReadAntiforgeryAsync(client).ConfigureAwait(false);

        using var logout = await SendMutationAsync<object?>(
            client,
            HttpMethod.Post,
            "/api/v1/auth/logout",
            body: null,
            antiforgery).ConfigureAwait(false);
        Assert.AreEqual(HttpStatusCode.NoContent, logout.StatusCode);

        using var denied = await client.PostAsJsonAsync(
            "/api/v1/auth/login",
            new LocalLoginRequest(administrator.UserName, InvalidPassword)).ConfigureAwait(false);
        Assert.AreEqual(HttpStatusCode.Unauthorized, denied.StatusCode);

        using var succeeded = await client.PostAsJsonAsync(
            "/api/v1/auth/login",
            new LocalLoginRequest(administrator.UserName, AdministratorPassword)).ConfigureAwait(false);
        Assert.AreEqual(HttpStatusCode.OK, succeeded.StatusCode);

        var page = await client.GetFromJsonAsync<AuditEventPageResponse>(
            "/api/v1/audit-events?limit=20&action=authentication.login")
            .ConfigureAwait(false);
        Assert.IsNotNull(page);
        Assert.IsTrue(page.Events.Any(auditEvent =>
            string.Equals(auditEvent.Outcome, AuditOutcomeNames.Denied, StringComparison.Ordinal)));
        Assert.IsTrue(page.Events.Any(auditEvent =>
            string.Equals(auditEvent.Outcome, AuditOutcomeNames.Succeeded, StringComparison.Ordinal)));

        var serialized = string.Join(
            '\n',
            page.Events.Select(auditEvent => string.Join(
                '|',
                auditEvent.Summary,
                auditEvent.SafeDetails,
                auditEvent.TargetId)));
        Assert.IsFalse(serialized.Contains(AdministratorPassword, StringComparison.Ordinal));
        Assert.IsFalse(serialized.Contains(InvalidPassword, StringComparison.Ordinal));
        Assert.IsFalse(serialized.Contains(administrator.UserName, StringComparison.Ordinal));
    }

    private static async Task<AuthenticatedUserResponse> SetupAdministratorAsync(HttpClient client)
    {
        using var response = await client.PostAsJsonAsync(
            "/api/v1/auth/setup",
            new InitialAdministratorRequest("admin", "Administrator", AdministratorPassword)).ConfigureAwait(false);
        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<AuthenticatedUserResponse>().ConfigureAwait(false)
            ?? throw new AssertFailedException("Initial setup returned no user.");
    }

    private static async Task<AntiforgeryTokenResponse> ReadAntiforgeryAsync(HttpClient client) =>
        await client.GetFromJsonAsync<AntiforgeryTokenResponse>("/api/v1/auth/antiforgery").ConfigureAwait(false)
        ?? throw new AssertFailedException("The antiforgery endpoint returned no token.");

    private static async Task<HttpResponseMessage> SendMutationAsync<T>(
        HttpClient client,
        HttpMethod method,
        string path,
        T body,
        AntiforgeryTokenResponse antiforgery)
    {
        using var message = new HttpRequestMessage(method, path);
        if (body is not null)
        {
            message.Content = JsonContent.Create(body);
        }

        message.Headers.Add(antiforgery.HeaderName, antiforgery.Token);
        return await client.SendAsync(message).ConfigureAwait(false);
    }

    private static async Task<PostgreSqlTestDatabase> CreateMigratedDatabaseAsync()
    {
        var database = await PostgreSqlTestDatabase.CreateAsync().ConfigureAwait(false);
        await CoreDatabaseMigrator.MigrateAsync(database.ConnectionString).ConfigureAwait(false);
        return database;
    }
}
