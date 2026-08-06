using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

using JulOS.Contracts.Authentication;
using JulOS.Contracts.Authorization;
using JulOS.Contracts.Errors;
using JulOS.Infrastructure.Authentication;
using JulOS.Infrastructure.Persistence.Core;
using JulOS.Integration.Tests.Persistence;

using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace JulOS.Integration.Tests.Authorization;

[TestClass]
[DoNotParallelize]
public sealed class AuthorizationTests
{
    private const string SecondaryPassword = "Valid-Secondary-Password-42!";

    private static readonly WebApplicationFactoryClientOptions ClientOptions = new()
    {
        BaseAddress = new Uri("https://localhost"),
        AllowAutoRedirect = false,
        HandleCookies = true,
    };

    [TestMethod]
    public async Task VersionRequiresItsExplicitPermissionAndReturns401Or403Correctly()
    {
        await using var database = await CreateMigratedDatabaseAsync().ConfigureAwait(false);
        using var host = new ServerHost(database.ConnectionString);
        using var anonymousClient = host.CreateClient(ClientOptions);

        using var anonymousVersion = await anonymousClient
            .GetAsync("/api/v1/system/version")
            .ConfigureAwait(false);
        Assert.AreEqual(HttpStatusCode.Unauthorized, anonymousVersion.StatusCode);

        using var administratorClient = host.CreateClient(ClientOptions);
        _ = await SetupAdministratorAsync(administratorClient).ConfigureAwait(false);

        using var administratorVersion = await administratorClient
            .GetAsync("/api/v1/system/version")
            .ConfigureAwait(false);
        Assert.AreEqual(HttpStatusCode.OK, administratorVersion.StatusCode);

        var user = await CreateUserAsync(host, "viewer", "Viewer").ConfigureAwait(false);
        using var viewerClient = host.CreateClient(ClientOptions);
        await LoginAsync(viewerClient, user.UserName!, SecondaryPassword).ConfigureAwait(false);

        using var forbiddenVersion = await viewerClient
            .GetAsync("/api/v1/system/version")
            .ConfigureAwait(false);
        Assert.AreEqual(HttpStatusCode.Forbidden, forbiddenVersion.StatusCode);
        Assert.AreEqual(
            PlatformErrorCodes.Forbidden,
            await ReadProblemCodeAsync(forbiddenVersion).ConfigureAwait(false));
    }

    [TestMethod]
    public async Task AdministratorCanCreateRoleAddMemberAndGrantVersionPermission()
    {
        await using var database = await CreateMigratedDatabaseAsync().ConfigureAwait(false);
        using var host = new ServerHost(database.ConnectionString);
        using var administratorClient = host.CreateClient(ClientOptions);
        _ = await SetupAdministratorAsync(administratorClient).ConfigureAwait(false);
        var antiforgery = await ReadAntiforgeryAsync(administratorClient).ConfigureAwait(false);

        using var createRole = await SendJsonMutationAsync(
            administratorClient,
            HttpMethod.Post,
            "/api/v1/authorization/roles",
            new CreateAuthorizationRoleRequest("Version readers", "May read the running JulOS version."),
            antiforgery).ConfigureAwait(false);
        Assert.AreEqual(HttpStatusCode.Created, createRole.StatusCode);
        var role = await createRole.Content
            .ReadFromJsonAsync<AuthorizationRoleResponse>()
            .ConfigureAwait(false);
        Assert.IsNotNull(role);
        Assert.IsFalse(role.IsSystemRole);
        Assert.AreEqual(1, role.Revision);

        var user = await CreateUserAsync(host, "version-viewer", "Version Viewer").ConfigureAwait(false);

        using var addMember = await SendMutationAsync(
            administratorClient,
            HttpMethod.Post,
            $"/api/v1/authorization/roles/{role.RoleId}/members/{user.Id}",
            antiforgery).ConfigureAwait(false);
        Assert.AreEqual(HttpStatusCode.NoContent, addMember.StatusCode);

        using var grant = await SendJsonMutationAsync(
            administratorClient,
            HttpMethod.Post,
            "/api/v1/authorization/assignments",
            new GrantPermissionRequest(
                AuthorizationSubjectTypes.Role,
                role.RoleId,
                AuthorizationPermissionNames.SystemVersionRead,
                AuthorizationScopeTypes.Global,
                ScopeId: null),
            antiforgery).ConfigureAwait(false);
        Assert.AreEqual(HttpStatusCode.Created, grant.StatusCode);

        using var viewerClient = host.CreateClient(ClientOptions);
        await LoginAsync(viewerClient, user.UserName!, SecondaryPassword).ConfigureAwait(false);

        using var version = await viewerClient
            .GetAsync("/api/v1/system/version")
            .ConfigureAwait(false);
        Assert.AreEqual(HttpStatusCode.OK, version.StatusCode);

        using var roles = await administratorClient
            .GetAsync("/api/v1/authorization/roles")
            .ConfigureAwait(false);
        Assert.AreEqual(HttpStatusCode.OK, roles.StatusCode);
    }

    [TestMethod]
    public async Task AdministratorSystemRoleIsImmutableAndCannotLoseItsLastMember()
    {
        await using var database = await CreateMigratedDatabaseAsync().ConfigureAwait(false);
        using var host = new ServerHost(database.ConnectionString);
        using var client = host.CreateClient(ClientOptions);
        var administrator = await SetupAdministratorAsync(client).ConfigureAwait(false);
        var antiforgery = await ReadAntiforgeryAsync(client).ConfigureAwait(false);

        var roles = await client
            .GetFromJsonAsync<AuthorizationRoleResponse[]>("/api/v1/authorization/roles")
            .ConfigureAwait(false);
        Assert.IsNotNull(roles);
        var administratorRole = roles.Single(role => role.IsSystemRole);

        using var update = await SendJsonMutationAsync(
            client,
            HttpMethod.Put,
            $"/api/v1/authorization/roles/{administratorRole.RoleId}",
            new UpdateAuthorizationRoleRequest(
                "Changed administrator",
                "This must not be accepted.",
                administratorRole.Revision),
            antiforgery).ConfigureAwait(false);
        Assert.AreEqual(HttpStatusCode.Conflict, update.StatusCode);
        Assert.AreEqual(
            AuthorizationErrorCodes.SystemRoleImmutable,
            await ReadProblemCodeAsync(update).ConfigureAwait(false));

        using var remove = await SendMutationAsync(
            client,
            HttpMethod.Delete,
            $"/api/v1/authorization/roles/{administratorRole.RoleId}/members/{administrator.UserId}",
            antiforgery).ConfigureAwait(false);
        Assert.AreEqual(HttpStatusCode.Conflict, remove.StatusCode);
        Assert.AreEqual(
            AuthorizationErrorCodes.LastAdministrator,
            await ReadProblemCodeAsync(remove).ConfigureAwait(false));
    }

    [TestMethod]
    public async Task AuthorizationMutationsRequirePermissionAndAntiforgeryMetadata()
    {
        await using var database = await CreateMigratedDatabaseAsync().ConfigureAwait(false);
        using var host = new ServerHost(database.ConnectionString);
        using var administratorClient = host.CreateClient(ClientOptions);
        _ = await SetupAdministratorAsync(administratorClient).ConfigureAwait(false);

        using var missingToken = await administratorClient.PostAsJsonAsync(
            "/api/v1/authorization/roles",
            new CreateAuthorizationRoleRequest("Operators", "Infrastructure operators."))
            .ConfigureAwait(false);
        Assert.AreEqual(HttpStatusCode.BadRequest, missingToken.StatusCode);

        var user = await CreateUserAsync(host, "member", "Member").ConfigureAwait(false);
        using var memberClient = host.CreateClient(ClientOptions);
        await LoginAsync(memberClient, user.UserName!, SecondaryPassword).ConfigureAwait(false);
        var memberToken = await ReadAntiforgeryAsync(memberClient).ConfigureAwait(false);

        using var forbidden = await SendJsonMutationAsync(
            memberClient,
            HttpMethod.Post,
            "/api/v1/authorization/roles",
            new CreateAuthorizationRoleRequest("Forbidden role", "Must not be created."),
            memberToken).ConfigureAwait(false);
        Assert.AreEqual(HttpStatusCode.Forbidden, forbidden.StatusCode);

        var dataSource = host.Services.GetRequiredService<EndpointDataSource>();
        var mutationEndpoints = dataSource.Endpoints
            .OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.RoutePattern.RawText?.StartsWith(
                "/api/v1/authorization",
                StringComparison.Ordinal) == true)
            .Where(endpoint => endpoint.Metadata
                .GetMetadata<IHttpMethodMetadata>()?
                .HttpMethods
                .Any(method => !string.Equals(method, HttpMethods.Get, StringComparison.Ordinal)) == true)
            .ToArray();

        Assert.IsGreaterThan(0, mutationEndpoints.Length);
        foreach (var endpoint in mutationEndpoints)
        {
            Assert.IsNotNull(endpoint.Metadata.GetMetadata<IAuthorizeData>(), endpoint.DisplayName);
            Assert.IsNotNull(endpoint.Metadata.GetMetadata<IAntiforgeryMetadata>(), endpoint.DisplayName);
        }
    }

    private static async Task<AuthenticatedUserResponse> SetupAdministratorAsync(HttpClient client)
    {
        using var response = await client.PostAsJsonAsync(
            "/api/v1/auth/setup",
            new InitialAdministratorRequest(
                "admin",
                "Administrator",
                "Valid-Initial-Password-42!"))
            .ConfigureAwait(false);
        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);
        var administrator = await response.Content
            .ReadFromJsonAsync<AuthenticatedUserResponse>()
            .ConfigureAwait(false);
        return administrator ?? throw new AssertFailedException("Setup returned no administrator.");
    }

    private static async Task<LocalUser> CreateUserAsync(
        ServerHost host,
        string userName,
        string displayName)
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
        return user;
    }

    private static async Task LoginAsync(HttpClient client, string userName, string password)
    {
        using var response = await client.PostAsJsonAsync(
            "/api/v1/auth/login",
            new LocalLoginRequest(userName, password))
            .ConfigureAwait(false);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
    }

    private static async Task<AntiforgeryTokenResponse> ReadAntiforgeryAsync(HttpClient client)
    {
        var token = await client
            .GetFromJsonAsync<AntiforgeryTokenResponse>("/api/v1/auth/antiforgery")
            .ConfigureAwait(false);
        return token ?? throw new AssertFailedException("No antiforgery token was returned.");
    }

    private static async Task<HttpResponseMessage> SendJsonMutationAsync<T>(
        HttpClient client,
        HttpMethod method,
        string requestUri,
        T value,
        AntiforgeryTokenResponse token)
    {
        var request = new HttpRequestMessage(method, requestUri)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(value, JsonSerializerOptions.Web),
                Encoding.UTF8,
                "application/json"),
        };
        request.Headers.Add(token.HeaderName, token.Token);
        return await client.SendAsync(request).ConfigureAwait(false);
    }

    private static async Task<HttpResponseMessage> SendMutationAsync(
        HttpClient client,
        HttpMethod method,
        string requestUri,
        AntiforgeryTokenResponse token)
    {
        var request = new HttpRequestMessage(method, requestUri);
        request.Headers.Add(token.HeaderName, token.Token);
        return await client.SendAsync(request).ConfigureAwait(false);
    }

    private static async Task<string> ReadProblemCodeAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync().ConfigureAwait(false));
        return document.RootElement.GetProperty(ProblemExtensionNames.Code).GetString()
            ?? throw new AssertFailedException("The problem response has no code.");
    }

    private static async Task<PostgreSqlTestDatabase> CreateMigratedDatabaseAsync()
    {
        var database = await PostgreSqlTestDatabase.CreateAsync().ConfigureAwait(false);
        await CoreDatabaseMigrator.MigrateAsync(database.ConnectionString).ConfigureAwait(false);
        return database;
    }
}
