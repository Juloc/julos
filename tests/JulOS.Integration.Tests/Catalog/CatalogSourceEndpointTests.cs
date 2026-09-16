using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using JulOS.Contracts.Authentication;
using JulOS.Contracts.Catalog;
using JulOS.Contracts.Errors;

using Microsoft.AspNetCore.Mvc.Testing;

namespace JulOS.Integration.Tests.Catalog;

/// <summary>
/// The catalog-source administration and publisher-key trust HTTP contract from
/// <c>docs/APPLICATION_CATALOG.md</c>, as an administration surface actually uses it.
/// </summary>
/// <remarks>
/// SQLite rather than PostgreSQL, because these assertions are about the transport — the
/// permissions, the problem codes and the status codes — and must run on every machine
/// rather than report inconclusive wherever no database container is available.
/// </remarks>
[TestClass]
[DoNotParallelize]
public sealed class CatalogSourceEndpointTests
{
    private const string AdministratorPassword = "Valid-Initial-Password-42!";
    private const string Location = "https://catalog.example.test/index.json";

    private static readonly WebApplicationFactoryClientOptions ClientOptions = new()
    {
        BaseAddress = new Uri("https://localhost"),
        AllowAutoRedirect = false,
        HandleCookies = true,
    };

    [TestMethod]
    public async Task ASourceRoundTripsThroughTheDocumentedRoutes()
    {
        await using var database = await SqliteServerHost.CreateAsync("catalog-sources").ConfigureAwait(false);
        using var client = database.CreateClient(ClientOptions);
        await SetupAdministratorAsync(client).ConfigureAwait(false);
        var antiforgery = await ReadAntiforgeryAsync(client).ConfigureAwait(false);

        var added = await AddAsync(client, antiforgery).ConfigureAwait(false);
        Assert.AreEqual(CatalogSourceKindNames.Https, added.Kind);
        Assert.AreEqual(CatalogRefreshStateNames.Never, added.LastRefreshState);

        using var updated = await SendAsync(
            client,
            HttpMethod.Put,
            $"/api/v1/catalog/sources/{added.CatalogSourceId}",
            new UpdateCatalogSourceRequest(
                "Renamed catalog",
                Location,
                null,
                CatalogSourceTrustLevelNames.AdministratorTrusted,
                Enabled: false,
                added.Revision),
            antiforgery).ConfigureAwait(false);
        Assert.AreEqual(HttpStatusCode.OK, updated.StatusCode);

        var read = await client
            .GetFromJsonAsync<CatalogSourceResponse>($"/api/v1/catalog/sources/{added.CatalogSourceId}")
            .ConfigureAwait(false);
        Assert.IsNotNull(read);
        Assert.AreEqual("Renamed catalog", read.DisplayName);
        Assert.AreEqual(CatalogSourceTrustLevelNames.AdministratorTrusted, read.TrustLevel);
        Assert.IsFalse(read.Enabled);

        using var removed = await SendAsync(
            client,
            HttpMethod.Delete,
            $"/api/v1/catalog/sources/{added.CatalogSourceId}?expectedRevision={read.Revision}",
            null,
            antiforgery).ConfigureAwait(false);
        Assert.AreEqual(HttpStatusCode.OK, removed.StatusCode);

        var live = await ListAsync(client, includeRemoved: false).ConfigureAwait(false);
        Assert.AreEqual(0, live.Count);
        var all = await ListAsync(client, includeRemoved: true).ConfigureAwait(false);
        Assert.AreEqual(
            1,
            all.Count,
            "A removed source stays resolvable, because installed applications still point at it.");
        Assert.IsNotNull(all[0].RemovedAtUtc);
    }

    [TestMethod]
    public async Task AMutationWithoutAnAntiforgeryTokenIsRefused()
    {
        await using var database = await SqliteServerHost.CreateAsync("catalog-sources").ConfigureAwait(false);
        using var client = database.CreateClient(ClientOptions);
        await SetupAdministratorAsync(client).ConfigureAwait(false);

        using var response = await client.PostAsJsonAsync(
            "/api/v1/catalog/sources",
            NewSourceRequest()).ConfigureAwait(false);

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [TestMethod]
    public async Task AnAnonymousCallerReachesNeitherSurface()
    {
        await using var database = await SqliteServerHost.CreateAsync("catalog-sources").ConfigureAwait(false);
        using var client = database.CreateClient(ClientOptions);
        await SetupAdministratorAsync(client).ConfigureAwait(false);
        await SignOutAsync(client).ConfigureAwait(false);

        using var sources = await client.GetAsync(new Uri("/api/v1/catalog/sources", UriKind.Relative))
            .ConfigureAwait(false);
        Assert.AreEqual(HttpStatusCode.Unauthorized, sources.StatusCode);

        using var key = await client
            .GetAsync(new Uri($"/api/v1/catalog/publisher-keys/{Guid.CreateVersion7()}", UriKind.Relative))
            .ConfigureAwait(false);
        Assert.AreEqual(HttpStatusCode.Unauthorized, key.StatusCode);
    }

    [TestMethod]
    public async Task ADuplicateLocationIsAConflictAndAnUnknownSourceIsNotFound()
    {
        await using var database = await SqliteServerHost.CreateAsync("catalog-sources").ConfigureAwait(false);
        using var client = database.CreateClient(ClientOptions);
        await SetupAdministratorAsync(client).ConfigureAwait(false);
        var antiforgery = await ReadAntiforgeryAsync(client).ConfigureAwait(false);
        _ = await AddAsync(client, antiforgery).ConfigureAwait(false);

        using var duplicate = await SendAsync(
            client,
            HttpMethod.Post,
            "/api/v1/catalog/sources",
            NewSourceRequest("Second catalog"),
            antiforgery).ConfigureAwait(false);
        Assert.AreEqual(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.AreEqual(
            CatalogErrorCodes.SourceDuplicate,
            await ReadProblemCodeAsync(duplicate).ConfigureAwait(false));

        using var missing = await client
            .GetAsync(new Uri($"/api/v1/catalog/sources/{Guid.CreateVersion7()}", UriKind.Relative))
            .ConfigureAwait(false);
        Assert.AreEqual(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.AreEqual(
            CatalogErrorCodes.SourceNotFound,
            await ReadProblemCodeAsync(missing).ConfigureAwait(false));
    }

    [TestMethod]
    public async Task AnOfficialSourceCannotBeMintedThroughTheApi()
    {
        await using var database = await SqliteServerHost.CreateAsync("catalog-sources").ConfigureAwait(false);
        using var client = database.CreateClient(ClientOptions);
        await SetupAdministratorAsync(client).ConfigureAwait(false);
        var antiforgery = await ReadAntiforgeryAsync(client).ConfigureAwait(false);

        using var response = await SendAsync(
            client,
            HttpMethod.Post,
            "/api/v1/catalog/sources",
            new AddCatalogSourceRequest(
                CatalogSourceKindNames.Https,
                "Impostor",
                Location,
                null,
                CatalogSourceTrustLevelNames.Official),
            antiforgery).ConfigureAwait(false);

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.AreEqual(
            CatalogErrorCodes.SourceInvalid,
            await ReadProblemCodeAsync(response).ConfigureAwait(false));
    }

    [TestMethod]
    public async Task AStaleRevisionIsReportedAsAConcurrencyConflict()
    {
        await using var database = await SqliteServerHost.CreateAsync("catalog-sources").ConfigureAwait(false);
        using var client = database.CreateClient(ClientOptions);
        await SetupAdministratorAsync(client).ConfigureAwait(false);
        var antiforgery = await ReadAntiforgeryAsync(client).ConfigureAwait(false);
        var added = await AddAsync(client, antiforgery).ConfigureAwait(false);

        using var response = await SendAsync(
            client,
            HttpMethod.Put,
            $"/api/v1/catalog/sources/{added.CatalogSourceId}",
            new UpdateCatalogSourceRequest(
                "Renamed catalog",
                Location,
                null,
                CatalogSourceTrustLevelNames.Custom,
                Enabled: true,
                added.Revision + 5),
            antiforgery).ConfigureAwait(false);

        Assert.AreEqual(HttpStatusCode.Conflict, response.StatusCode);
        Assert.AreEqual(
            PlatformErrorCodes.ConcurrencyConflict,
            await ReadProblemCodeAsync(response).ConfigureAwait(false));
    }

    [TestMethod]
    public async Task ASourceWithNoObservedKeysListsNone()
    {
        await using var database = await SqliteServerHost.CreateAsync("catalog-sources").ConfigureAwait(false);
        using var client = database.CreateClient(ClientOptions);
        await SetupAdministratorAsync(client).ConfigureAwait(false);
        var antiforgery = await ReadAntiforgeryAsync(client).ConfigureAwait(false);
        var added = await AddAsync(client, antiforgery).ConfigureAwait(false);

        var keys = await client
            .GetFromJsonAsync<IReadOnlyList<CatalogPublisherKeyResponse>>(
                $"/api/v1/catalog/sources/{added.CatalogSourceId}/publisher-keys")
            .ConfigureAwait(false);

        Assert.IsNotNull(keys);
        Assert.AreEqual(0, keys.Count);
    }

    [TestMethod]
    public async Task ARefreshReturnsTheOperationThatOwnsItAndRepeatsAreTheSameOperation()
    {
        await using var database = await SqliteServerHost.CreateAsync("catalog-sources").ConfigureAwait(false);
        using var client = database.CreateClient(ClientOptions);
        await SetupAdministratorAsync(client).ConfigureAwait(false);
        var antiforgery = await ReadAntiforgeryAsync(client).ConfigureAwait(false);
        var added = await AddAsync(client, antiforgery).ConfigureAwait(false);

        using var accepted = await SendAsync(
            client,
            HttpMethod.Post,
            $"/api/v1/catalog/sources/{added.CatalogSourceId}/refresh",
            null,
            antiforgery).ConfigureAwait(false);

        Assert.AreEqual(
            HttpStatusCode.Accepted,
            accepted.StatusCode,
            "A refresh reads a whole remote catalog, so the request returns the durable operation.");
        var first = await ReadOperationIdAsync(accepted).ConfigureAwait(false);
        Assert.AreEqual(
            added.CatalogSourceId.ToString("D"),
            await ReadTargetReferenceAsync(accepted).ConfigureAwait(false));

        using var again = await SendAsync(
            client,
            HttpMethod.Post,
            $"/api/v1/catalog/sources/{added.CatalogSourceId}/refresh",
            null,
            antiforgery).ConfigureAwait(false);

        Assert.AreEqual(
            first,
            await ReadOperationIdAsync(again).ConfigureAwait(false),
            "A retried request joins the refresh that is already queued rather than racing a second one.");
    }

    [TestMethod]
    public async Task ARemovedSourceIsNotRefreshed()
    {
        await using var database = await SqliteServerHost.CreateAsync("catalog-sources").ConfigureAwait(false);
        using var client = database.CreateClient(ClientOptions);
        await SetupAdministratorAsync(client).ConfigureAwait(false);
        var antiforgery = await ReadAntiforgeryAsync(client).ConfigureAwait(false);
        var added = await AddAsync(client, antiforgery).ConfigureAwait(false);

        using var removed = await SendAsync(
            client,
            HttpMethod.Delete,
            $"/api/v1/catalog/sources/{added.CatalogSourceId}?expectedRevision={added.Revision}",
            null,
            antiforgery).ConfigureAwait(false);
        Assert.AreEqual(HttpStatusCode.OK, removed.StatusCode);

        using var refresh = await SendAsync(
            client,
            HttpMethod.Post,
            $"/api/v1/catalog/sources/{added.CatalogSourceId}/refresh",
            null,
            antiforgery).ConfigureAwait(false);

        Assert.AreEqual(HttpStatusCode.Conflict, refresh.StatusCode);
        Assert.AreEqual(
            CatalogErrorCodes.SourceRemoved,
            await ReadProblemCodeAsync(refresh).ConfigureAwait(false));
    }

    private static async Task<string?> ReadOperationIdAsync(HttpResponseMessage response) =>
        await ReadPropertyAsync(response, "operationId").ConfigureAwait(false);

    private static async Task<string?> ReadTargetReferenceAsync(HttpResponseMessage response) =>
        await ReadPropertyAsync(response, "targetReference").ConfigureAwait(false);

    private static async Task<string?> ReadPropertyAsync(HttpResponseMessage response, string name)
    {
        var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        using var document = JsonDocument.Parse(body);
        return document.RootElement.TryGetProperty(name, out var value) ? value.ToString() : null;
    }

    private static AddCatalogSourceRequest NewSourceRequest(string displayName = "Example catalog") =>
        new(
            CatalogSourceKindNames.Https,
            displayName,
            Location,
            null,
            CatalogSourceTrustLevelNames.Custom);

    private static async Task<CatalogSourceResponse> AddAsync(
        HttpClient client,
        AntiforgeryTokenResponse antiforgery)
    {
        using var response = await SendAsync(
            client,
            HttpMethod.Post,
            "/api/v1/catalog/sources",
            NewSourceRequest(),
            antiforgery).ConfigureAwait(false);
        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        return JsonSerializer.Deserialize<CatalogSourceResponse>(body, JsonSerializerOptions.Web)
            ?? throw new AssertFailedException("The catalog source endpoint returned no body.");
    }

    private static async Task<IReadOnlyList<CatalogSourceResponse>> ListAsync(
        HttpClient client,
        bool includeRemoved)
    {
        return await client
            .GetFromJsonAsync<IReadOnlyList<CatalogSourceResponse>>(
                $"/api/v1/catalog/sources?includeRemoved={(includeRemoved ? "true" : "false")}")
            .ConfigureAwait(false)
            ?? throw new AssertFailedException("The catalog source list returned no body.");
    }

    private static async Task<string?> ReadProblemCodeAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        using var document = JsonDocument.Parse(body);
        return document.RootElement.TryGetProperty(ProblemExtensionNames.Code, out var code)
            ? code.GetString()
            : null;
    }

    private static async Task SetupAdministratorAsync(HttpClient client)
    {
        using var response = await client.PostAsJsonAsync(
            "/api/v1/auth/setup",
            new InitialAdministratorRequest("admin", "Administrator", AdministratorPassword))
            .ConfigureAwait(false);
        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);
    }

    private static async Task SignOutAsync(HttpClient client)
    {
        var antiforgery = await ReadAntiforgeryAsync(client).ConfigureAwait(false);
        using var response = await SendAsync(client, HttpMethod.Post, "/api/v1/auth/logout", null, antiforgery)
            .ConfigureAwait(false);
        Assert.IsTrue(response.IsSuccessStatusCode, "Signing out is the precondition of this test.");
    }

    private static async Task<AntiforgeryTokenResponse> ReadAntiforgeryAsync(HttpClient client)
    {
        return await client
            .GetFromJsonAsync<AntiforgeryTokenResponse>("/api/v1/auth/antiforgery")
            .ConfigureAwait(false)
            ?? throw new AssertFailedException("The antiforgery endpoint returned no token.");
    }

    private static async Task<HttpResponseMessage> SendAsync(
        HttpClient client,
        HttpMethod method,
        string path,
        object? content,
        AntiforgeryTokenResponse antiforgery)
    {
        using var message = new HttpRequestMessage(method, path);
        if (content is not null)
        {
            message.Content = JsonContent.Create(content, content.GetType());
        }

        message.Headers.Add(antiforgery.HeaderName, antiforgery.Token);
        return await client.SendAsync(message).ConfigureAwait(false);
    }
}
