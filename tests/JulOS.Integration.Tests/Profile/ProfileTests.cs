using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using JulOS.Contracts.Authentication;
using JulOS.Contracts.Errors;
using JulOS.Contracts.Profile;
using JulOS.Infrastructure.Persistence.Core;
using JulOS.Integration.Tests.Persistence;

using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace JulOS.Integration.Tests.Profile;

[TestClass]
[DoNotParallelize]
public sealed class ProfileTests
{
    private const string AdministratorPassword = "Valid-Initial-Password-42!";

    private static readonly WebApplicationFactoryClientOptions ClientOptions = new()
    {
        BaseAddress = new Uri("https://localhost"),
        AllowAutoRedirect = false,
        HandleCookies = true,
    };

    [TestMethod]
    public async Task ProfileReturnsDefaultsAndUpdatesSupportedPreferences()
    {
        await using var database = await CreateMigratedDatabaseAsync().ConfigureAwait(false);
        using var host = new ServerHost(database.ConnectionString);
        using var client = host.CreateClient(ClientOptions);
        var administrator = await SetupAdministratorAsync(client).ConfigureAwait(false);

        var initial = await client
            .GetFromJsonAsync<ProfileResponse>("/api/v1/profile")
            .ConfigureAwait(false);
        Assert.IsNotNull(initial);
        Assert.AreEqual(administrator.UserId, initial.UserId);
        Assert.AreEqual("admin", initial.UserName);
        Assert.AreEqual("Administrator", initial.DisplayName);
        Assert.AreEqual(ProfileLanguages.English, initial.PreferredLanguage);
        Assert.AreEqual("UTC", initial.TimeZone);
        Assert.AreEqual(ProfileThemes.System, initial.Theme);
        Assert.AreEqual(ProfileMotionPreferences.Enabled, initial.Motion);
        Assert.IsTrue(initial.Revision > 0);

        var antiforgery = await ReadAntiforgeryAsync(client).ConfigureAwait(false);
        using var response = await SendUpdateAsync(
            client,
            new UpdateProfilePreferencesRequest(
                ProfileLanguages.German,
                "Europe/Berlin",
                ProfileThemes.Dark,
                ProfileMotionPreferences.Reduced,
                initial.Revision),
            antiforgery).ConfigureAwait(false);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var updated = await response.Content
            .ReadFromJsonAsync<ProfileResponse>()
            .ConfigureAwait(false);
        Assert.IsNotNull(updated);
        Assert.AreEqual(ProfileLanguages.German, updated.PreferredLanguage);
        Assert.AreEqual("Europe/Berlin", updated.TimeZone);
        Assert.AreEqual(ProfileThemes.Dark, updated.Theme);
        Assert.AreEqual(ProfileMotionPreferences.Reduced, updated.Motion);
        Assert.AreEqual(initial.Revision + 1, updated.Revision);
    }

    [TestMethod]
    public async Task InvalidLocaleAndTimeZoneReturnStableValidationFailure()
    {
        await using var database = await CreateMigratedDatabaseAsync().ConfigureAwait(false);
        using var host = new ServerHost(database.ConnectionString);
        using var client = host.CreateClient(ClientOptions);
        _ = await SetupAdministratorAsync(client).ConfigureAwait(false);
        var antiforgery = await ReadAntiforgeryAsync(client).ConfigureAwait(false);

        using var invalidLocale = await SendUpdateAsync(
            client,
            new UpdateProfilePreferencesRequest(
                "fr",
                "UTC",
                ProfileThemes.System,
                ProfileMotionPreferences.Enabled,
                Revision: 1),
            antiforgery).ConfigureAwait(false);
        Assert.AreEqual(HttpStatusCode.BadRequest, invalidLocale.StatusCode);
        Assert.AreEqual(
            ProfileErrorCodes.InvalidPreferences,
            await ReadProblemCodeAsync(invalidLocale).ConfigureAwait(false));

        using var invalidTimeZone = await SendUpdateAsync(
            client,
            new UpdateProfilePreferencesRequest(
                ProfileLanguages.English,
                "Not/A_Real_Time_Zone",
                ProfileThemes.System,
                ProfileMotionPreferences.Enabled,
                Revision: 1),
            antiforgery).ConfigureAwait(false);
        Assert.AreEqual(HttpStatusCode.BadRequest, invalidTimeZone.StatusCode);
        Assert.AreEqual(
            ProfileErrorCodes.InvalidPreferences,
            await ReadProblemCodeAsync(invalidTimeZone).ConfigureAwait(false));
    }

    [TestMethod]
    public async Task PreferenceMutationRequiresAntiforgeryAndRejectsStaleRevision()
    {
        await using var database = await CreateMigratedDatabaseAsync().ConfigureAwait(false);
        using var host = new ServerHost(database.ConnectionString);
        using var client = host.CreateClient(ClientOptions);
        _ = await SetupAdministratorAsync(client).ConfigureAwait(false);
        var initial = await client
            .GetFromJsonAsync<ProfileResponse>("/api/v1/profile")
            .ConfigureAwait(false)
            ?? throw new AssertFailedException("The profile endpoint returned no profile.");

        using var missingToken = await client.PutAsJsonAsync(
            "/api/v1/profile/preferences",
            new UpdateProfilePreferencesRequest(
                ProfileLanguages.German,
                "UTC",
                ProfileThemes.Light,
                ProfileMotionPreferences.Reduced,
                Revision: initial.Revision)).ConfigureAwait(false);
        Assert.AreEqual(HttpStatusCode.BadRequest, missingToken.StatusCode);
        Assert.AreEqual(
            AuthenticationErrorCodes.AntiforgeryInvalid,
            await ReadProblemCodeAsync(missingToken).ConfigureAwait(false));

        var antiforgery = await ReadAntiforgeryAsync(client).ConfigureAwait(false);
        using var first = await SendUpdateAsync(
            client,
            new UpdateProfilePreferencesRequest(
                ProfileLanguages.German,
                "UTC",
                ProfileThemes.Light,
                ProfileMotionPreferences.Reduced,
                Revision: initial.Revision),
            antiforgery).ConfigureAwait(false);
        Assert.AreEqual(HttpStatusCode.OK, first.StatusCode);

        using var stale = await SendUpdateAsync(
            client,
            new UpdateProfilePreferencesRequest(
                ProfileLanguages.English,
                "UTC",
                ProfileThemes.Dark,
                ProfileMotionPreferences.Enabled,
                Revision: initial.Revision),
            antiforgery).ConfigureAwait(false);
        Assert.AreEqual(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.AreEqual(
            JulOS.Contracts.Errors.PlatformErrorCodes.ConcurrencyConflict,
            await ReadProblemCodeAsync(stale).ConfigureAwait(false));

        using var document = JsonDocument.Parse(
            await stale.Content.ReadAsStringAsync().ConfigureAwait(false));
        Assert.AreEqual(
            initial.Revision + 1,
            document.RootElement.GetProperty(ProblemExtensionNames.CurrentRevision).GetInt32());

        var dataSource = host.Services.GetRequiredService<EndpointDataSource>();
        var updateEndpoint = dataSource.Endpoints
            .OfType<RouteEndpoint>()
            .Single(endpoint => string.Equals(
                endpoint.RoutePattern.RawText,
                "/api/v1/profile/preferences",
                StringComparison.Ordinal));
        Assert.IsNotNull(updateEndpoint.Metadata.GetMetadata<IAntiforgeryMetadata>());
        Assert.IsNotNull(updateEndpoint.Metadata.GetMetadata<IHttpMethodMetadata>());
    }

    private static async Task<AuthenticatedUserResponse> SetupAdministratorAsync(HttpClient client)
    {
        using var response = await client.PostAsJsonAsync(
            "/api/v1/auth/setup",
            new InitialAdministratorRequest("admin", "Administrator", AdministratorPassword))
            .ConfigureAwait(false);
        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);
        return await response.Content
            .ReadFromJsonAsync<AuthenticatedUserResponse>()
            .ConfigureAwait(false)
            ?? throw new AssertFailedException("Initial setup returned no user.");
    }

    private static async Task<AntiforgeryTokenResponse> ReadAntiforgeryAsync(HttpClient client)
    {
        return await client
            .GetFromJsonAsync<AntiforgeryTokenResponse>("/api/v1/auth/antiforgery")
            .ConfigureAwait(false)
            ?? throw new AssertFailedException("The antiforgery endpoint returned no token.");
    }

    private static async Task<HttpResponseMessage> SendUpdateAsync(
        HttpClient client,
        UpdateProfilePreferencesRequest request,
        AntiforgeryTokenResponse antiforgery)
    {
        using var message = new HttpRequestMessage(HttpMethod.Put, "/api/v1/profile/preferences")
        {
            Content = JsonContent.Create(request),
        };
        message.Headers.Add(antiforgery.HeaderName, antiforgery.Token);
        return await client.SendAsync(message).ConfigureAwait(false);
    }

    private static async Task<string> ReadProblemCodeAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync().ConfigureAwait(false));
        return document.RootElement.GetProperty(ProblemExtensionNames.Code).GetString()
            ?? throw new AssertFailedException("The problem response has no error code.");
    }

    private static async Task<PostgreSqlTestDatabase> CreateMigratedDatabaseAsync()
    {
        var database = await PostgreSqlTestDatabase.CreateAsync().ConfigureAwait(false);
        await CoreDatabaseMigrator.MigrateAsync(database.ConnectionString).ConfigureAwait(false);
        return database;
    }
}
