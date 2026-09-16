using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using JulOS.Contracts.Authentication;
using JulOS.Contracts.Devices;
using JulOS.Contracts.Errors;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;

namespace JulOS.Integration.Tests.Devices;

/// <summary>
/// The background-execution preference over real HTTP, from <c>docs/MOBILE_PWA.md</c>
/// section 11.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class ApplicationExecutionPreferenceEndpointTests
{
    private const string AdministratorPassword = "Valid-Initial-Password-42!";

    private static readonly Guid KeepActiveCapable = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid SuspendOnly = Guid.Parse("44444444-4444-4444-4444-444444444444");

    private static readonly WebApplicationFactoryClientOptions ClientOptions = new()
    {
        BaseAddress = new Uri("https://localhost"),
        AllowAutoRedirect = false,
        HandleCookies = true,
    };

    [TestMethod]
    public async Task ThePreferenceRoundTripsAndDefaultsToSuspend()
    {
        await using var database = await SqliteServerHost.CreateAsync("execution-preferences");
        await SeedApplicationsAsync(database.ConnectionString).ConfigureAwait(false);
        using var client = database.CreateClient(ClientOptions);
        await SetupAdministratorAsync(client).ConfigureAwait(false);
        var antiforgery = await ReadAntiforgeryAsync(client).ConfigureAwait(false);

        var initial = await ReadAsync(client, KeepActiveCapable).ConfigureAwait(false);
        Assert.AreEqual(BackgroundModeNames.Suspend, initial.BackgroundMode);
        Assert.AreEqual(0, initial.Revision);
        Assert.IsTrue(initial.SupportsKeepSurfaceActive);

        using var created = await WriteAsync(
            client,
            KeepActiveCapable,
            new UpdateApplicationExecutionPreferenceRequest(BackgroundModeNames.KeepSurfaceActive, null),
            antiforgery).ConfigureAwait(false);
        Assert.AreEqual(HttpStatusCode.Created, created.StatusCode);

        var stored = await ReadAsync(client, KeepActiveCapable).ConfigureAwait(false);
        Assert.AreEqual(BackgroundModeNames.KeepSurfaceActive, stored.BackgroundMode);
        Assert.AreEqual(1, stored.Revision);

        using var replaced = await WriteAsync(
            client,
            KeepActiveCapable,
            new UpdateApplicationExecutionPreferenceRequest(BackgroundModeNames.Suspend, stored.Revision),
            antiforgery).ConfigureAwait(false);
        Assert.AreEqual(HttpStatusCode.OK, replaced.StatusCode);
    }

    [TestMethod]
    public async Task AnApplicationThatDoesNotDeclareItCannotBeKeptActive()
    {
        await using var database = await SqliteServerHost.CreateAsync("execution-preferences");
        await SeedApplicationsAsync(database.ConnectionString).ConfigureAwait(false);
        using var client = database.CreateClient(ClientOptions);
        await SetupAdministratorAsync(client).ConfigureAwait(false);
        var antiforgery = await ReadAntiforgeryAsync(client).ConfigureAwait(false);

        using var refused = await WriteAsync(
            client,
            SuspendOnly,
            new UpdateApplicationExecutionPreferenceRequest(BackgroundModeNames.KeepSurfaceActive, null),
            antiforgery).ConfigureAwait(false);

        Assert.AreEqual(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.AreEqual(
            ClientDeviceErrorCodes.BackgroundModeUnsupported,
            await ReadProblemCodeAsync(refused).ConfigureAwait(false),
            "A mode the manifest never declared must not be storable.");
    }

    [TestMethod]
    public async Task ChangingThePreferenceRequiresAnAntiforgeryTokenAndAnAuthenticatedUser()
    {
        await using var database = await SqliteServerHost.CreateAsync("execution-preferences");
        await SeedApplicationsAsync(database.ConnectionString).ConfigureAwait(false);
        using var client = database.CreateClient(ClientOptions);

        using var anonymous = await client
            .GetAsync(Path(KeepActiveCapable))
            .ConfigureAwait(false);
        Assert.AreEqual(
            HttpStatusCode.Unauthorized,
            anonymous.StatusCode,
            "A package cannot reach this surface, because it is the user's own session that answers.");

        await SetupAdministratorAsync(client).ConfigureAwait(false);
        using var unprotected = await client.PutAsJsonAsync(
            Path(KeepActiveCapable),
            new UpdateApplicationExecutionPreferenceRequest(BackgroundModeNames.KeepSurfaceActive, null))
            .ConfigureAwait(false);

        Assert.AreEqual(HttpStatusCode.BadRequest, unprotected.StatusCode);
        Assert.AreEqual(
            AuthenticationErrorCodes.AntiforgeryInvalid,
            await ReadProblemCodeAsync(unprotected).ConfigureAwait(false));
    }

    [TestMethod]
    public async Task AStaleWriteReportsTheAuthoritativeRevision()
    {
        await using var database = await SqliteServerHost.CreateAsync("execution-preferences");
        await SeedApplicationsAsync(database.ConnectionString).ConfigureAwait(false);
        using var client = database.CreateClient(ClientOptions);
        await SetupAdministratorAsync(client).ConfigureAwait(false);
        var antiforgery = await ReadAntiforgeryAsync(client).ConfigureAwait(false);

        using var created = await WriteAsync(
            client,
            KeepActiveCapable,
            new UpdateApplicationExecutionPreferenceRequest(BackgroundModeNames.KeepSurfaceActive, null),
            antiforgery).ConfigureAwait(false);
        Assert.AreEqual(HttpStatusCode.Created, created.StatusCode);

        using var stale = await WriteAsync(
            client,
            KeepActiveCapable,
            new UpdateApplicationExecutionPreferenceRequest(BackgroundModeNames.Suspend, 0),
            antiforgery).ConfigureAwait(false);

        Assert.AreEqual(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.AreEqual(
            PlatformErrorCodes.ConcurrencyConflict,
            await ReadProblemCodeAsync(stale).ConfigureAwait(false));
    }

    [TestMethod]
    public async Task AnApplicationThatDoesNotExistReportsTheStableCode()
    {
        await using var database = await SqliteServerHost.CreateAsync("execution-preferences");
        using var client = database.CreateClient(ClientOptions);
        await SetupAdministratorAsync(client).ConfigureAwait(false);

        using var missing = await client.GetAsync(Path(Guid.NewGuid())).ConfigureAwait(false);

        Assert.AreEqual(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.AreEqual(
            ClientDeviceErrorCodes.NotFound,
            await ReadProblemCodeAsync(missing).ConfigureAwait(false));
    }

    private static string Path(Guid applicationDefinitionId) =>
        $"/api/v1/application-execution-preferences/{applicationDefinitionId}/current"
        + $"?workspaceClass={WorkspaceClassNames.Phone}";

    private static async Task<ApplicationExecutionPreferenceResponse> ReadAsync(
        HttpClient client,
        Guid applicationDefinitionId)
    {
        return await client
            .GetFromJsonAsync<ApplicationExecutionPreferenceResponse>(Path(applicationDefinitionId))
            .ConfigureAwait(false)
            ?? throw new AssertFailedException("The preference endpoint returned no body.");
    }

    private static async Task<HttpResponseMessage> WriteAsync(
        HttpClient client,
        Guid applicationDefinitionId,
        UpdateApplicationExecutionPreferenceRequest request,
        AntiforgeryTokenResponse antiforgery)
    {
        using var message = new HttpRequestMessage(HttpMethod.Put, Path(applicationDefinitionId))
        {
            Content = JsonContent.Create(request),
        };
        message.Headers.Add(antiforgery.HeaderName, antiforgery.Token);
        return await client.SendAsync(message).ConfigureAwait(false);
    }

    private static async Task SeedApplicationsAsync(string connectionString)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync().ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO application_definitions (id, owning_package_id, stable_key, display_name_key,
                                                 instance_policy, default_width, default_height,
                                                 minimum_width, minimum_height, is_enabled,
                                                 surface_contract_version, surface_supports_keep_active,
                                                 surface_handles_back, revision)
            VALUES ($capable, 'de.juloc.julos.reference', 'reference', 'reference.title',
                    'SingleInstancePerUser', 800, 600, 320, 240, 1, '1.0.0', 1, 1, 1),
                   ($suspendOnly, 'de.juloc.julos.hostmetrics', 'host-metrics', 'hostmetrics.title',
                    'SingleInstancePerUser', 800, 600, 320, 240, 1, '1.0.0', 0, 0, 1);
            """;
        _ = command.Parameters.AddWithValue("$capable", KeepActiveCapable.ToString());
        _ = command.Parameters.AddWithValue("$suspendOnly", SuspendOnly.ToString());
        _ = await command.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    private static async Task SetupAdministratorAsync(HttpClient client)
    {
        using var response = await client.PostAsJsonAsync(
            "/api/v1/auth/setup",
            new InitialAdministratorRequest("admin", "Administrator", AdministratorPassword))
            .ConfigureAwait(false);
        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);
    }

    private static async Task<AntiforgeryTokenResponse> ReadAntiforgeryAsync(HttpClient client)
    {
        return await client
            .GetFromJsonAsync<AntiforgeryTokenResponse>("/api/v1/auth/antiforgery")
            .ConfigureAwait(false)
            ?? throw new AssertFailedException("The antiforgery endpoint returned no token.");
    }

    private static async Task<string> ReadProblemCodeAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync().ConfigureAwait(false));
        return document.RootElement.GetProperty(ProblemExtensionNames.Code).GetString()
            ?? throw new AssertFailedException("The problem response has no error code.");
    }
}
