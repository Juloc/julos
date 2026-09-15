using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using JulOS.Contracts.Authentication;
using JulOS.Contracts.Devices;
using JulOS.Contracts.Errors;
using JulOS.Contracts.Layouts;

using Microsoft.AspNetCore.Mvc.Testing;

namespace JulOS.Integration.Tests.Layouts;

/// <summary>
/// The workspace-layout HTTP contract from <c>docs/MOBILE_PWA.md</c> section 15 as the
/// Desktop shell actually uses it.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class WorkspaceLayoutEndpointTests
{
    private const string AdministratorPassword = "Valid-Initial-Password-42!";

    private static readonly WebApplicationFactoryClientOptions ClientOptions = new()
    {
        BaseAddress = new Uri("https://localhost"),
        AllowAutoRedirect = false,
        HandleCookies = true,
    };

    [TestMethod]
    public async Task TheOldViewportLayoutRouteIsGone()
    {
        await using var database = await SqliteServerHost.CreateAsync("workspace-layouts");
        using var client = database.CreateClient(ClientOptions);
        await SetupAdministratorAsync(client).ConfigureAwait(false);

        using var removed = await client.GetAsync("/api/v1/desktop/layouts/desktop").ConfigureAwait(false);

        Assert.AreEqual(
            HttpStatusCode.NotFound,
            removed.StatusCode,
            "No indefinite old layout API may exist beside the new one.");
    }

    [TestMethod]
    public async Task AWorkspaceLayoutIsCreatedOnFirstWriteAndReadBack()
    {
        await using var database = await SqliteServerHost.CreateAsync("workspace-layouts");
        using var client = database.CreateClient(ClientOptions);
        await SetupAdministratorAsync(client).ConfigureAwait(false);
        var antiforgery = await ReadAntiforgeryAsync(client).ConfigureAwait(false);

        var empty = await ReadAsync(client, WorkspaceClassNames.DesktopSingle).ConfigureAwait(false);
        Assert.AreEqual(LayoutScopeNames.Shared, empty.LayoutScope);
        Assert.AreEqual(RestoreModeNames.Resume, empty.RestoreMode);
        Assert.IsTrue(empty.PersistenceEnabled);
        Assert.AreEqual(0, empty.Revision);
        Assert.IsNull(empty.Layout.LayoutId);

        using var created = await WriteAsync(
            client,
            WorkspaceClassNames.DesktopSingle,
            Document(),
            expectedRevision: 0,
            antiforgery).ConfigureAwait(false);
        Assert.AreEqual(HttpStatusCode.Created, created.StatusCode, "Create-on-first-save returns 201.");

        var stored = await ReadAsync(client, WorkspaceClassNames.DesktopSingle).ConfigureAwait(false);
        Assert.AreEqual(1, stored.Revision);
        Assert.IsNotNull(stored.Layout.LayoutId);
        Assert.AreEqual(PresentationModeNames.Freeform, stored.Layout.PresentationMode);
        Assert.AreEqual(0, stored.Layout.Windows.Count);

        using var replaced = await WriteAsync(
            client,
            WorkspaceClassNames.DesktopSingle,
            Document(),
            expectedRevision: 1,
            antiforgery).ConfigureAwait(false);
        Assert.AreEqual(HttpStatusCode.OK, replaced.StatusCode, "A replacement returns 200.");
    }

    [TestMethod]
    public async Task EachWorkspaceClassKeepsItsOwnLayout()
    {
        await using var database = await SqliteServerHost.CreateAsync("workspace-layouts");
        using var client = database.CreateClient(ClientOptions);
        await SetupAdministratorAsync(client).ConfigureAwait(false);
        var antiforgery = await ReadAntiforgeryAsync(client).ConfigureAwait(false);

        using var desktop = await WriteAsync(
            client, WorkspaceClassNames.DesktopSingle, Document(), 0, antiforgery).ConfigureAwait(false);
        Assert.AreEqual(HttpStatusCode.Created, desktop.StatusCode);

        var phone = await ReadAsync(client, WorkspaceClassNames.Phone).ConfigureAwait(false);
        Assert.AreEqual(0, phone.Revision, "A desktop write never creates the phone layout.");
        Assert.AreEqual(
            PresentationModeNames.PhoneEmpty,
            phone.Layout.PresentationMode,
            "A phone workspace uses the phone presentation modes even before anything is stored.");
    }

    [TestMethod]
    public async Task AStaleWriteIsRejectedWithTheAuthoritativeRevision()
    {
        await using var database = await SqliteServerHost.CreateAsync("workspace-layouts");
        using var client = database.CreateClient(ClientOptions);
        await SetupAdministratorAsync(client).ConfigureAwait(false);
        var antiforgery = await ReadAntiforgeryAsync(client).ConfigureAwait(false);

        using var created = await WriteAsync(
            client, WorkspaceClassNames.DesktopSingle, Document(), 0, antiforgery).ConfigureAwait(false);
        Assert.AreEqual(HttpStatusCode.Created, created.StatusCode);

        using var stale = await WriteAsync(
            client, WorkspaceClassNames.DesktopSingle, Document(), 0, antiforgery).ConfigureAwait(false);

        Assert.AreEqual(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.AreEqual(
            PlatformErrorCodes.ConcurrencyConflict,
            await ReadProblemCodeAsync(stale).ConfigureAwait(false));

        using var document = JsonDocument.Parse(
            await stale.Content.ReadAsStringAsync().ConfigureAwait(false));
        Assert.AreEqual(1, document.RootElement.GetProperty(ProblemExtensionNames.CurrentRevision).GetInt32());
    }

    [TestMethod]
    public async Task AWorkspaceClassThatDoesNotExistReportsTheStableCode()
    {
        await using var database = await SqliteServerHost.CreateAsync("workspace-layouts");
        using var client = database.CreateClient(ClientOptions);
        await SetupAdministratorAsync(client).ConfigureAwait(false);

        using var invalid = await client.GetAsync("/api/v1/workspace-layouts/desktop/current").ConfigureAwait(false);

        Assert.AreEqual(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.AreEqual(
            WorkspaceLayoutErrorCodes.WorkspaceClassInvalid,
            await ReadProblemCodeAsync(invalid).ConfigureAwait(false),
            "The old viewport vocabulary is not silently accepted as a workspace class.");
    }

    [TestMethod]
    public async Task AFreshWorkspaceRefusesToStoreWindowState()
    {
        await using var database = await SqliteServerHost.CreateAsync("workspace-layouts");
        using var client = database.CreateClient(ClientOptions);
        await SetupAdministratorAsync(client).ConfigureAwait(false);
        var antiforgery = await ReadAntiforgeryAsync(client).ConfigureAwait(false);

        // Registering gives this client a device cookie, which is what carries the
        // preference the resolver reads.
        var device = await RegisterDeviceAsync(client, antiforgery).ConfigureAwait(false);
        using var preference = await SendAsync(
            client,
            HttpMethod.Put,
            $"/api/v1/client-devices/{device.ClientDeviceId}/preferences/{WorkspaceClassNames.DesktopSingle}",
            new UpdateDeviceWorkspacePreferenceRequest(
                LayoutScopeNames.Shared,
                RestoreModeNames.Fresh,
                device.Revision),
            antiforgery).ConfigureAwait(false);
        Assert.AreEqual(HttpStatusCode.OK, preference.StatusCode);

        var resolved = await ReadAsync(client, WorkspaceClassNames.DesktopSingle).ConfigureAwait(false);
        Assert.AreEqual(RestoreModeNames.Fresh, resolved.RestoreMode);
        Assert.IsFalse(resolved.PersistenceEnabled);
        Assert.IsNull(resolved.Layout.LayoutId);
        Assert.AreEqual(0, resolved.Revision);

        using var refused = await WriteAsync(
            client, WorkspaceClassNames.DesktopSingle, Document(), 0, antiforgery).ConfigureAwait(false);

        Assert.AreEqual(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.AreEqual(
            WorkspaceLayoutErrorCodes.PersistenceDisabled,
            await ReadProblemCodeAsync(refused).ConfigureAwait(false));
    }

    [TestMethod]
    public async Task WritingALayoutRequiresAnAntiforgeryTokenAndAnAuthenticatedUser()
    {
        await using var database = await SqliteServerHost.CreateAsync("workspace-layouts");
        using var client = database.CreateClient(ClientOptions);

        using var anonymous = await client
            .GetAsync($"/api/v1/workspace-layouts/{WorkspaceClassNames.DesktopSingle}/current")
            .ConfigureAwait(false);
        Assert.AreEqual(HttpStatusCode.Unauthorized, anonymous.StatusCode);

        await SetupAdministratorAsync(client).ConfigureAwait(false);
        using var unprotected = await client.PutAsJsonAsync(
            $"/api/v1/workspace-layouts/{WorkspaceClassNames.DesktopSingle}/current",
            new WorkspaceLayoutWriteRequest(Document(), 0)).ConfigureAwait(false);

        Assert.AreEqual(HttpStatusCode.BadRequest, unprotected.StatusCode);
        Assert.AreEqual(
            AuthenticationErrorCodes.AntiforgeryInvalid,
            await ReadProblemCodeAsync(unprotected).ConfigureAwait(false));
    }

    [TestMethod]
    public async Task MultiDisplayIsInitializedExplicitly()
    {
        await using var database = await SqliteServerHost.CreateAsync("workspace-layouts");
        using var client = database.CreateClient(ClientOptions);
        await SetupAdministratorAsync(client).ConfigureAwait(false);
        var antiforgery = await ReadAntiforgeryAsync(client).ConfigureAwait(false);

        var absent = await ReadAsync(client, WorkspaceClassNames.DesktopMulti).ConfigureAwait(false);
        Assert.IsNull(absent.Layout.LayoutId, "Reading never creates the multi-display layout.");

        using var initialized = await SendAsync(
            client,
            HttpMethod.Post,
            $"/api/v1/workspace-layouts/{WorkspaceClassNames.DesktopMulti}/initialization",
            new { CopyFromDesktopSingle = false, DisplayCount = 2 },
            antiforgery).ConfigureAwait(false);

        Assert.AreEqual(HttpStatusCode.Created, initialized.StatusCode);
        var created = await ReadAsync(client, WorkspaceClassNames.DesktopMulti).ConfigureAwait(false);
        Assert.AreEqual(2, created.Layout.DisplayCount);
    }

    private static WorkspaceLayoutDocument Document() => new(
        LayoutId: null,
        "Default",
        PresentationModeNames.Freeform,
        PrimaryWindowId: null,
        SecondaryWindowId: null,
        SplitRatioPermille: null,
        DisplayCount: 1,
        UpdatedAtUtc: DateTimeOffset.UnixEpoch,
        Windows: [],
        Widgets: []);

    private static async Task<WorkspaceLayoutResponse> ReadAsync(HttpClient client, string workspaceClass)
    {
        return await client
            .GetFromJsonAsync<WorkspaceLayoutResponse>($"/api/v1/workspace-layouts/{workspaceClass}/current")
            .ConfigureAwait(false)
            ?? throw new AssertFailedException("The layout endpoint returned no body.");
    }

    private static Task<HttpResponseMessage> WriteAsync(
        HttpClient client,
        string workspaceClass,
        WorkspaceLayoutDocument layout,
        int expectedRevision,
        AntiforgeryTokenResponse antiforgery) =>
        SendAsync(
            client,
            HttpMethod.Put,
            $"/api/v1/workspace-layouts/{workspaceClass}/current",
            new WorkspaceLayoutWriteRequest(layout, expectedRevision),
            antiforgery);

    private static async Task<ClientDeviceResponse> RegisterDeviceAsync(
        HttpClient client,
        AntiforgeryTokenResponse antiforgery)
    {
        using var response = await SendAsync(
            client,
            HttpMethod.Post,
            "/api/v1/client-devices/registration",
            new RegisterClientDeviceRequest("Laptop", WorkspaceClassNames.DesktopSingle),
            antiforgery).ConfigureAwait(false);
        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<ClientDeviceResponse>().ConfigureAwait(false)
            ?? throw new AssertFailedException("Registration returned no device.");
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
        object content,
        AntiforgeryTokenResponse antiforgery)
    {
        using var message = new HttpRequestMessage(method, path)
        {
            Content = JsonContent.Create(content, content.GetType()),
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
}
