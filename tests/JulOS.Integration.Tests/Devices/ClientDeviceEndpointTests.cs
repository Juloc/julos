using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using JulOS.Contracts.Authentication;
using JulOS.Contracts.Devices;
using JulOS.Contracts.Errors;
using Microsoft.AspNetCore.Mvc.Testing;

namespace JulOS.Integration.Tests.Devices;

/// <summary>
/// The client-device HTTP contract from <c>docs/MOBILE_PWA.md</c> section 3 as the Desktop
/// Settings surface actually uses it.
/// </summary>
/// <remarks>
/// SQLite rather than PostgreSQL, because these assertions are about the transport — the
/// cookie, the problem codes and the status codes — and must run on every machine rather
/// than report inconclusive wherever no database container is available.
/// </remarks>
[TestClass]
[DoNotParallelize]
public sealed class ClientDeviceEndpointTests
{
    private const string AdministratorPassword = "Valid-Initial-Password-42!";

    private static readonly WebApplicationFactoryClientOptions ClientOptions = new()
    {
        BaseAddress = new Uri("https://localhost"),
        AllowAutoRedirect = false,
        HandleCookies = true,
    };

    [TestMethod]
    public async Task RegistrationIssuesTheKeyOnlyAsACookieAndResolvesTheSameDeviceAfterwards()
    {
        await using var database = await SqliteServerHost.CreateAsync("client-devices").ConfigureAwait(false);
        using var client = database.CreateClient(ClientOptions);
        await SetupAdministratorAsync(client).ConfigureAwait(false);
        var antiforgery = await ReadAntiforgeryAsync(client).ConfigureAwait(false);

        using var registration = await SendAsync(
            client,
            HttpMethod.Post,
            "/api/v1/client-devices/registration",
            new RegisterClientDeviceRequest("Laptop", WorkspaceClassNames.DesktopSingle),
            antiforgery).ConfigureAwait(false);

        Assert.AreEqual(HttpStatusCode.Created, registration.StatusCode);
        var body = await registration.Content.ReadAsStringAsync().ConfigureAwait(false);
        Assert.IsFalse(
            body.Contains("key", StringComparison.OrdinalIgnoreCase),
            "The client instance key and its hash must never appear in a response body.");

        var cookie = registration.Headers
            .GetValues("Set-Cookie")
            .Single(value => value.StartsWith(".JulOS.Device=", StringComparison.Ordinal));
        Assert.IsTrue(cookie.Contains("httponly", StringComparison.OrdinalIgnoreCase));
        Assert.IsTrue(cookie.Contains("samesite=strict", StringComparison.OrdinalIgnoreCase));
        Assert.IsTrue(cookie.Contains("secure", StringComparison.OrdinalIgnoreCase));

        // The same browser presenting the same cookie must resolve, not mint a second device.
        using var again = await SendAsync(
            client,
            HttpMethod.Post,
            "/api/v1/client-devices/registration",
            new RegisterClientDeviceRequest("Laptop", WorkspaceClassNames.Tablet),
            antiforgery).ConfigureAwait(false);
        Assert.AreEqual(HttpStatusCode.OK, again.StatusCode);

        var devices = await ListAsync(client).ConfigureAwait(false);
        Assert.AreEqual(1, devices.Count);
        Assert.IsTrue(devices[0].IsCurrentDevice);
        Assert.AreEqual(WorkspaceClassNames.Tablet, devices[0].LastDetectedWorkspaceClass);
    }

    [TestMethod]
    public async Task RenamingPinningAndPreferringRoundTripThroughTheDocumentedRoutes()
    {
        await using var database = await SqliteServerHost.CreateAsync("client-devices").ConfigureAwait(false);
        using var client = database.CreateClient(ClientOptions);
        await SetupAdministratorAsync(client).ConfigureAwait(false);
        var antiforgery = await ReadAntiforgeryAsync(client).ConfigureAwait(false);
        var device = await RegisterAsync(client, antiforgery, "Laptop").ConfigureAwait(false);

        using var renamed = await SendAsync(
            client,
            HttpMethod.Put,
            $"/api/v1/client-devices/{device.ClientDeviceId}",
            new UpdateClientDeviceRequest("Kitchen tablet", WorkspaceClassNames.Tablet, device.Revision),
            antiforgery).ConfigureAwait(false);
        Assert.AreEqual(HttpStatusCode.OK, renamed.StatusCode);
        var updated = await ReadDeviceAsync(renamed).ConfigureAwait(false);
        Assert.AreEqual("Kitchen tablet", updated.DisplayName);
        Assert.AreEqual(WorkspaceClassNames.Tablet, updated.WorkspaceClassOverride);
        Assert.IsTrue(
            updated.IsCurrentDevice,
            "A mutation response must report the calling device as current, as the list does.");

        using var preferred = await SendAsync(
            client,
            HttpMethod.Put,
            $"/api/v1/client-devices/{device.ClientDeviceId}/preferences/{WorkspaceClassNames.Tablet}",
            new UpdateDeviceWorkspacePreferenceRequest("device", "fresh", updated.Revision),
            antiforgery).ConfigureAwait(false);
        Assert.AreEqual(HttpStatusCode.OK, preferred.StatusCode);
        var withPreference = await ReadDeviceAsync(preferred).ConfigureAwait(false);
        var preference = withPreference.Preferences.Single();
        Assert.AreEqual(WorkspaceClassNames.Tablet, preference.WorkspaceClass);
        Assert.AreEqual("device", preference.LayoutScope);
        Assert.AreEqual("fresh", preference.RestoreMode);
        Assert.IsTrue(withPreference.IsCurrentDevice);

        // A write based on the revision the caller already spent must lose.
        using var stale = await SendAsync(
            client,
            HttpMethod.Put,
            $"/api/v1/client-devices/{device.ClientDeviceId}/preferences/{WorkspaceClassNames.Tablet}",
            new UpdateDeviceWorkspacePreferenceRequest("shared", "resume", updated.Revision),
            antiforgery).ConfigureAwait(false);
        Assert.AreEqual(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.AreEqual(
            PlatformErrorCodes.ConcurrencyConflict,
            await ReadProblemCodeAsync(stale).ConfigureAwait(false));
    }

    [TestMethod]
    public async Task RemovingTheCurrentDeviceClearsItsCookie()
    {
        await using var database = await SqliteServerHost.CreateAsync("client-devices").ConfigureAwait(false);
        using var client = database.CreateClient(ClientOptions);
        await SetupAdministratorAsync(client).ConfigureAwait(false);
        var antiforgery = await ReadAntiforgeryAsync(client).ConfigureAwait(false);
        var device = await RegisterAsync(client, antiforgery, "Laptop").ConfigureAwait(false);

        using var removed = await SendAsync(
            client,
            HttpMethod.Delete,
            $"/api/v1/client-devices/{device.ClientDeviceId}?revision={device.Revision}",
            content: null,
            antiforgery).ConfigureAwait(false);

        Assert.AreEqual(HttpStatusCode.NoContent, removed.StatusCode);
        var cleared = removed.Headers
            .GetValues("Set-Cookie")
            .Single(value => value.StartsWith(".JulOS.Device=", StringComparison.Ordinal));
        Assert.IsTrue(
            cleared.Contains("expires=Thu, 01 Jan 1970", StringComparison.OrdinalIgnoreCase),
            "Removing the device in use must clear its cookie rather than leave a key that resolves to nothing.");
        Assert.AreEqual(0, (await ListAsync(client).ConfigureAwait(false)).Count);
    }

    [TestMethod]
    public async Task AnUnknownDeviceReportsTheStableClientDeviceErrorCode()
    {
        await using var database = await SqliteServerHost.CreateAsync("client-devices").ConfigureAwait(false);
        using var client = database.CreateClient(ClientOptions);
        await SetupAdministratorAsync(client).ConfigureAwait(false);
        var antiforgery = await ReadAntiforgeryAsync(client).ConfigureAwait(false);
        _ = await RegisterAsync(client, antiforgery, "Laptop").ConfigureAwait(false);

        using var missing = await SendAsync(
            client,
            HttpMethod.Put,
            $"/api/v1/client-devices/{Guid.NewGuid()}",
            new UpdateClientDeviceRequest("Taken", null, 1),
            antiforgery).ConfigureAwait(false);

        Assert.AreEqual(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.AreEqual(
            ClientDeviceErrorCodes.NotFound,
            await ReadProblemCodeAsync(missing).ConfigureAwait(false),
            "The documented client-device code must reach the client, not the generic platform code.");
    }

    [TestMethod]
    public async Task RemovalWithoutAUsableRevisionIsRejected()
    {
        await using var database = await SqliteServerHost.CreateAsync("client-devices").ConfigureAwait(false);
        using var client = database.CreateClient(ClientOptions);
        await SetupAdministratorAsync(client).ConfigureAwait(false);
        var antiforgery = await ReadAntiforgeryAsync(client).ConfigureAwait(false);
        var device = await RegisterAsync(client, antiforgery, "Laptop").ConfigureAwait(false);

        using var withoutRevision = await SendAsync(
            client,
            HttpMethod.Delete,
            $"/api/v1/client-devices/{device.ClientDeviceId}",
            content: null,
            antiforgery).ConfigureAwait(false);
        Assert.AreEqual(HttpStatusCode.BadRequest, withoutRevision.StatusCode);
        Assert.AreEqual(
            PlatformErrorCodes.Invalid,
            await ReadProblemCodeAsync(withoutRevision).ConfigureAwait(false));

        using var zeroRevision = await SendAsync(
            client,
            HttpMethod.Delete,
            $"/api/v1/client-devices/{device.ClientDeviceId}?revision=0",
            content: null,
            antiforgery).ConfigureAwait(false);
        Assert.AreEqual(HttpStatusCode.BadRequest, zeroRevision.StatusCode);
        Assert.AreEqual(
            PlatformErrorCodes.Invalid,
            await ReadProblemCodeAsync(zeroRevision).ConfigureAwait(false));

        Assert.AreEqual(1, (await ListAsync(client).ConfigureAwait(false)).Count);
    }

    [TestMethod]
    public async Task EveryMutationRequiresAnAntiforgeryToken()
    {
        await using var database = await SqliteServerHost.CreateAsync("client-devices").ConfigureAwait(false);
        using var client = database.CreateClient(ClientOptions);
        await SetupAdministratorAsync(client).ConfigureAwait(false);

        using var unprotected = await client.PostAsJsonAsync(
            "/api/v1/client-devices/registration",
            new RegisterClientDeviceRequest("Laptop", WorkspaceClassNames.DesktopSingle))
            .ConfigureAwait(false);

        Assert.AreEqual(HttpStatusCode.BadRequest, unprotected.StatusCode);
        Assert.AreEqual(
            AuthenticationErrorCodes.AntiforgeryInvalid,
            await ReadProblemCodeAsync(unprotected).ConfigureAwait(false));
    }

    [TestMethod]
    public async Task ClientDeviceEndpointsRequireAnAuthenticatedUser()
    {
        await using var database = await SqliteServerHost.CreateAsync("client-devices").ConfigureAwait(false);
        using var client = database.CreateClient(ClientOptions);

        using var anonymous = await client.GetAsync("/api/v1/client-devices").ConfigureAwait(false);

        Assert.AreEqual(HttpStatusCode.Unauthorized, anonymous.StatusCode);
    }

    private static async Task<ClientDeviceResponse> RegisterAsync(
        HttpClient client,
        AntiforgeryTokenResponse antiforgery,
        string displayName)
    {
        using var response = await SendAsync(
            client,
            HttpMethod.Post,
            "/api/v1/client-devices/registration",
            new RegisterClientDeviceRequest(displayName, WorkspaceClassNames.DesktopSingle),
            antiforgery).ConfigureAwait(false);
        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);
        return await ReadDeviceAsync(response).ConfigureAwait(false);
    }

    private static async Task<IReadOnlyList<ClientDeviceResponse>> ListAsync(HttpClient client)
    {
        return await client
            .GetFromJsonAsync<IReadOnlyList<ClientDeviceResponse>>("/api/v1/client-devices")
            .ConfigureAwait(false)
            ?? throw new AssertFailedException("The client-device list returned no body.");
    }

    private static async Task<ClientDeviceResponse> ReadDeviceAsync(HttpResponseMessage response)
    {
        return await response.Content
            .ReadFromJsonAsync<ClientDeviceResponse>()
            .ConfigureAwait(false)
            ?? throw new AssertFailedException("The response carried no client device.");
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

    private static async Task<string> ReadProblemCodeAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync().ConfigureAwait(false));
        return document.RootElement.GetProperty(ProblemExtensionNames.Code).GetString()
            ?? throw new AssertFailedException("The problem response has no error code.");
    }
}
