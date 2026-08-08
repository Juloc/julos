using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using JulOS.Contracts.Authentication;
using JulOS.Contracts.Errors;
using JulOS.Infrastructure.Authentication;
using JulOS.Infrastructure.Persistence.Core;
using JulOS.Integration.Tests.Persistence;

using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using Npgsql;

namespace JulOS.Integration.Tests.Authentication;

[TestClass]
[DoNotParallelize]
public sealed class LocalAuthenticationTests
{
    private static readonly WebApplicationFactoryClientOptions ClientOptions = new()
    {
        BaseAddress = new Uri("https://localhost"),
        AllowAutoRedirect = false,
        HandleCookies = true,
    };

    [TestMethod]
    public async Task InitialSetupCreatesAdministratorAndProtectsTheApi()
    {
        await using var database = await CreateMigratedDatabaseAsync().ConfigureAwait(false);
        using var host = new ServerHost(database.ConnectionString);
        using var client = host.CreateClient(ClientOptions);

        using var unauthenticatedVersion = await client
            .GetAsync("/api/v1/system/version")
            .ConfigureAwait(false);
        Assert.AreEqual(HttpStatusCode.Unauthorized, unauthenticatedVersion.StatusCode);

        using var live = await client.GetAsync("/health/live").ConfigureAwait(false);
        Assert.AreEqual(HttpStatusCode.OK, live.StatusCode);

        var initialStatus = await client
            .GetFromJsonAsync<AuthenticationStatusResponse>("/api/v1/auth/status")
            .ConfigureAwait(false);
        Assert.IsNotNull(initialStatus);
        Assert.IsTrue(initialStatus.SetupRequired);
        Assert.IsFalse(initialStatus.Authenticated);

        using var setup = await client.PostAsJsonAsync(
            "/api/v1/auth/setup",
            ValidAdministrator())
            .ConfigureAwait(false);

        Assert.AreEqual(HttpStatusCode.Created, setup.StatusCode);
        var sessionCookie = setup.Headers
            .GetValues("Set-Cookie")
            .Single(value => value.StartsWith(".JulOS.Session=", StringComparison.Ordinal));
        Assert.IsTrue(sessionCookie.Contains("secure", StringComparison.OrdinalIgnoreCase));
        Assert.IsTrue(sessionCookie.Contains("httponly", StringComparison.OrdinalIgnoreCase));
        Assert.IsTrue(sessionCookie.Contains("samesite=strict", StringComparison.OrdinalIgnoreCase));

        using var authenticatedVersion = await client
            .GetAsync("/api/v1/system/version")
            .ConfigureAwait(false);
        Assert.AreEqual(HttpStatusCode.OK, authenticatedVersion.StatusCode);

        await AssertAdministratorStorageAsync(database.ConnectionString).ConfigureAwait(false);
    }

    [TestMethod]
    public async Task InitialSetupCannotRunTwice()
    {
        await using var database = await CreateMigratedDatabaseAsync().ConfigureAwait(false);
        using var host = new ServerHost(database.ConnectionString);
        using var firstClient = host.CreateClient(ClientOptions);
        using var secondClient = host.CreateClient(ClientOptions);

        using var first = await firstClient
            .PostAsJsonAsync("/api/v1/auth/setup", ValidAdministrator())
            .ConfigureAwait(false);
        Assert.AreEqual(HttpStatusCode.Created, first.StatusCode);

        using var second = await secondClient.PostAsJsonAsync(
            "/api/v1/auth/setup",
            new InitialAdministratorRequest(
                "other-admin",
                "Other Administrator",
                "Other-Valid-Password-42!"))
            .ConfigureAwait(false);

        Assert.AreEqual(HttpStatusCode.Conflict, second.StatusCode);
        Assert.AreEqual(
            AuthenticationErrorCodes.SetupAlreadyCompleted,
            await ReadProblemCodeAsync(second).ConfigureAwait(false));
    }

    [TestMethod]
    public async Task FailedPasswordsLockTheAccountWithoutChangingThePublicFailure()
    {
        await using var database = await CreateMigratedDatabaseAsync().ConfigureAwait(false);
        using var host = new ServerHost(
            database.ConnectionString,
            new Dictionary<string, string?>
            {
                ["Authentication:LoginPermitLimit"] = "20",
                ["Authentication:LoginWindowSeconds"] = "60",
            });
        using var setupClient = host.CreateClient(ClientOptions);
        using var loginClient = host.CreateClient(ClientOptions);

        using var setup = await setupClient
            .PostAsJsonAsync("/api/v1/auth/setup", ValidAdministrator())
            .ConfigureAwait(false);
        Assert.AreEqual(HttpStatusCode.Created, setup.StatusCode);

        for (var attempt = 0; attempt < 5; attempt++)
        {
            using var failed = await loginClient.PostAsJsonAsync(
                "/api/v1/auth/login",
                new LocalLoginRequest("admin", "Wrong-Password-42!"))
                .ConfigureAwait(false);

            Assert.AreEqual(HttpStatusCode.Unauthorized, failed.StatusCode);
            Assert.AreEqual(
                AuthenticationErrorCodes.InvalidCredentials,
                await ReadProblemCodeAsync(failed).ConfigureAwait(false));
        }

        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync().ConfigureAwait(false);
        await using var command = new NpgsqlCommand(
            "SELECT access_failed_count, lockout_end_utc FROM core.users WHERE normalized_user_name = 'ADMIN'",
            connection);
        await using var reader = await command.ExecuteReaderAsync().ConfigureAwait(false);
        Assert.IsTrue(await reader.ReadAsync().ConfigureAwait(false));
        Assert.AreEqual(0, reader.GetInt32(0));
        Assert.IsFalse(reader.IsDBNull(1));
        Assert.IsTrue(reader.GetFieldValue<DateTime>(1) > DateTime.UtcNow);
    }

    [TestMethod]
    public async Task LoginRateLimitReturnsTheCommonRetryableProblem()
    {
        await using var database = await CreateMigratedDatabaseAsync().ConfigureAwait(false);
        using var host = new ServerHost(
            database.ConnectionString,
            new Dictionary<string, string?>
            {
                ["Authentication:LoginPermitLimit"] = "2",
                ["Authentication:LoginWindowSeconds"] = "600",
            });
        using var client = host.CreateClient(ClientOptions);

        for (var attempt = 0; attempt < 2; attempt++)
        {
            using var response = await client.PostAsJsonAsync(
                "/api/v1/auth/login",
                new LocalLoginRequest("admin", "Not-Configured-42!"))
                .ConfigureAwait(false);
            Assert.AreEqual(HttpStatusCode.Conflict, response.StatusCode);
        }

        using var limited = await client.PostAsJsonAsync(
            "/api/v1/auth/login",
            new LocalLoginRequest("admin", "Not-Configured-42!"))
            .ConfigureAwait(false);

        Assert.AreEqual(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.AreEqual(
            PlatformErrorCodes.RateLimited,
            await ReadProblemCodeAsync(limited).ConfigureAwait(false));

        using var document = JsonDocument.Parse(
            await limited.Content.ReadAsStringAsync().ConfigureAwait(false));
        Assert.IsTrue(document.RootElement.GetProperty(ProblemExtensionNames.Retryable).GetBoolean());
        Assert.IsTrue(limited.Headers.Contains("Retry-After"));
    }

    [TestMethod]
    public async Task LogoutRequiresAntiforgeryAndEndsTheSession()
    {
        await using var database = await CreateMigratedDatabaseAsync().ConfigureAwait(false);
        using var host = new ServerHost(database.ConnectionString);
        using var client = host.CreateClient(ClientOptions);

        using var setup = await client
            .PostAsJsonAsync("/api/v1/auth/setup", ValidAdministrator())
            .ConfigureAwait(false);
        Assert.AreEqual(HttpStatusCode.Created, setup.StatusCode);

        using var rejected = await client
            .PostAsync("/api/v1/auth/logout", content: null)
            .ConfigureAwait(false);
        Assert.AreEqual(HttpStatusCode.BadRequest, rejected.StatusCode);
        Assert.AreEqual(
            AuthenticationErrorCodes.AntiforgeryInvalid,
            await ReadProblemCodeAsync(rejected).ConfigureAwait(false));

        var token = await client
            .GetFromJsonAsync<AntiforgeryTokenResponse>("/api/v1/auth/antiforgery")
            .ConfigureAwait(false);
        Assert.IsNotNull(token);

        using var logoutRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/logout");
        logoutRequest.Headers.Add(token.HeaderName, token.Token);
        using var logout = await client.SendAsync(logoutRequest).ConfigureAwait(false);
        Assert.AreEqual(HttpStatusCode.NoContent, logout.StatusCode);

        using var version = await client.GetAsync("/api/v1/system/version").ConfigureAwait(false);
        Assert.AreEqual(HttpStatusCode.Unauthorized, version.StatusCode);
    }

    [TestMethod]
    public async Task AntiforgeryProtectedEndpointsWorkOverPlainHttp()
    {
        var httpClientOptions = new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("http://localhost"),
            AllowAutoRedirect = false,
            HandleCookies = true,
        };

        await using var database = await CreateMigratedDatabaseAsync().ConfigureAwait(false);
        using var host = new ServerHost(database.ConnectionString);
        using var client = host.CreateClient(httpClientOptions);

        using var setup = await client
            .PostAsJsonAsync("/api/v1/auth/setup", ValidAdministrator())
            .ConfigureAwait(false);
        Assert.AreEqual(HttpStatusCode.Created, setup.StatusCode);

        var token = await client
            .GetFromJsonAsync<AntiforgeryTokenResponse>("/api/v1/auth/antiforgery")
            .ConfigureAwait(false);
        Assert.IsNotNull(token);

        using var logoutRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/logout");
        logoutRequest.Headers.Add(token.HeaderName, token.Token);
        using var logout = await client.SendAsync(logoutRequest).ConfigureAwait(false);
        Assert.AreEqual(HttpStatusCode.NoContent, logout.StatusCode);
    }

    [TestMethod]
    public void SessionTimeoutUsesTheConfiguredValue()
    {
        using var host = new ServerHost(
            new Dictionary<string, string?>
            {
                ["Authentication:SessionTimeoutMinutes"] = "17",
            });

        var cookies = host.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>();
        var applicationCookie = cookies.Get(IdentityConstants.ApplicationScheme);

        Assert.AreEqual(TimeSpan.FromMinutes(17), applicationCookie.ExpireTimeSpan);
        Assert.IsTrue(applicationCookie.SlidingExpiration);
        Assert.AreEqual(CookieSecurePolicy.SameAsRequest, applicationCookie.Cookie.SecurePolicy);
        Assert.AreEqual(SameSiteMode.Strict, applicationCookie.Cookie.SameSite);
        Assert.IsTrue(applicationCookie.Cookie.HttpOnly);
    }

    [TestMethod]
    public async Task InitialSetupCreatesAdministratorOnSqlite()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "julos-integration-tests",
            "sqlite-setup",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var connectionString = $"Data Source={Path.Combine(directory, "julos.db")};Pooling=False";

        try
        {
            await CoreDatabaseMigrator.MigrateAsync(
                new CoreDatabaseConfiguration(CoreDatabaseProvider.Sqlite, connectionString))
                .ConfigureAwait(false);

            using var host = new ServerHost(
                connectionString,
                new Dictionary<string, string?> { ["Database:Provider"] = "sqlite" });
            using var client = host.CreateClient(ClientOptions);

            using var setup = await client
                .PostAsJsonAsync("/api/v1/auth/setup", ValidAdministrator())
                .ConfigureAwait(false);

            Assert.AreEqual(HttpStatusCode.Created, setup.StatusCode);

            using var authenticatedVersion = await client
                .GetAsync("/api/v1/system/version")
                .ConfigureAwait(false);
            Assert.AreEqual(HttpStatusCode.OK, authenticatedVersion.StatusCode);

            await using var connection = new SqliteConnection(connectionString);
            await connection.OpenAsync().ConfigureAwait(false);
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT u.user_name, u.display_name, r.name, r.is_system_role, s.completed_at_utc
                FROM users u
                INNER JOIN user_roles ur ON ur.user_id = u.id
                INNER JOIN roles r ON r.id = ur.role_id
                INNER JOIN authentication_setup s ON s.administrator_user_id = u.id
                """;
            await using var reader = await command.ExecuteReaderAsync().ConfigureAwait(false);

            Assert.IsTrue(await reader.ReadAsync().ConfigureAwait(false));
            Assert.AreEqual("admin", reader.GetString(0));
            Assert.AreEqual("Administrator", reader.GetString(1));
            Assert.AreEqual(LocalIdentityNames.AdministratorRole, reader.GetString(2));
            Assert.IsFalse(reader.IsDBNull(4));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static InitialAdministratorRequest ValidAdministrator()
    {
        return new InitialAdministratorRequest(
            "admin",
            "Administrator",
            "Valid-Initial-Password-42!");
    }

    private static async Task<PostgreSqlTestDatabase> CreateMigratedDatabaseAsync()
    {
        var database = await PostgreSqlTestDatabase.CreateAsync().ConfigureAwait(false);
        await CoreDatabaseMigrator.MigrateAsync(database.ConnectionString).ConfigureAwait(false);
        return database;
    }

    private static async Task<string> ReadProblemCodeAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync().ConfigureAwait(false));

        return document.RootElement
            .GetProperty(ProblemExtensionNames.Code)
            .GetString()
            ?? throw new AssertFailedException("The problem response has no error code.");
    }

    private static async Task AssertAdministratorStorageAsync(string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync().ConfigureAwait(false);

        await using var command = new NpgsqlCommand(
            """
            SELECT u.user_name, u.display_name, r.name, r.is_system_role, s.completed_at_utc
            FROM core.users u
            INNER JOIN core.user_roles ur ON ur.user_id = u.id
            INNER JOIN core.roles r ON r.id = ur.role_id
            INNER JOIN core.authentication_setup s ON s.administrator_user_id = u.id
            """,
            connection);
        await using var reader = await command.ExecuteReaderAsync().ConfigureAwait(false);

        Assert.IsTrue(await reader.ReadAsync().ConfigureAwait(false));
        Assert.AreEqual("admin", reader.GetString(0));
        Assert.AreEqual("Administrator", reader.GetString(1));
        Assert.AreEqual(LocalIdentityNames.AdministratorRole, reader.GetString(2));
        Assert.IsTrue(reader.GetBoolean(3));
        Assert.IsFalse(reader.IsDBNull(4));
    }
}

