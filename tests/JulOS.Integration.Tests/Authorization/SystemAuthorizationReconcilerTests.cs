using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

using JulOS.Application.Authorization;
using JulOS.Contracts.Authentication;
using JulOS.Contracts.Authorization;
using JulOS.Infrastructure.Authorization;
using JulOS.Infrastructure.Persistence.Core;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;

namespace JulOS.Integration.Tests.Authorization;

/// <summary>
/// Proves that an administrator provisioned before a permission entered the
/// catalog regains it on the next startup, so package and web-app management keep
/// working across upgrades instead of failing with a silent 403.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class SystemAuthorizationReconcilerTests
{
    private static readonly WebApplicationFactoryClientOptions ClientOptions = new()
    {
        BaseAddress = new Uri("https://localhost"),
        AllowAutoRedirect = false,
        HandleCookies = true,
    };

    [TestMethod]
    public async Task ReconcileRestoresPermissionsRemovedBeforeAnUpgrade()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "julos-integration-tests",
            "authorization-reconcile",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var connectionString =
            $"Data Source={Path.Combine(directory, "julos.db")};Pooling=False";

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
                .PostAsJsonAsync(
                    "/api/v1/auth/setup",
                    new InitialAdministratorRequest("admin", "Administrator", "Valid-Initial-Password-42!"))
                .ConfigureAwait(false);
            Assert.AreEqual(HttpStatusCode.Created, setup.StatusCode);

            // Simulate an administrator that was provisioned before these permissions
            // existed by removing the grants that a fresh setup seeds today.
            await RemoveAdministratorGrantAsync(
                connectionString,
                AuthorizationPermissionNames.PackageManage).ConfigureAwait(false);
            await RemoveAdministratorGrantAsync(
                connectionString,
                AuthorizationPermissionNames.WebAppUse).ConfigureAwait(false);

            var antiforgery = await ReadAntiforgeryAsync(client).ConfigureAwait(false);

            // Without the grant the guarded endpoint denies the administrator, which is
            // exactly the "install and uninstall do nothing" symptom: authorization
            // rejects the request before it reaches the handler, so nothing changes and
            // no server error is logged.
            using var deniedRemoval = await SendRemovePackageAsync(client, antiforgery).ConfigureAwait(false);
            Assert.AreEqual(HttpStatusCode.Forbidden, deniedRemoval.StatusCode);

            await SystemAuthorizationReconciler
                .ReconcileAdministratorPermissionsAsync(host.Services)
                .ConfigureAwait(false);

            // Every catalog permission is present again after reconciliation.
            foreach (var permission in AuthorizationPermissionCatalog.InitialAdministratorPermissions)
            {
                Assert.AreEqual(
                    1,
                    await CountAdministratorGrantAsync(connectionString, permission.Value).ConfigureAwait(false),
                    $"Administrator is missing the '{permission.Value}' grant after reconciliation.");
            }

            // The guarded endpoint now runs the handler again. The package does not
            // exist, so the answer is 404 rather than the earlier 403 - the point is
            // that authorization no longer blocks the administrator.
            var refreshedAntiforgery = await ReadAntiforgeryAsync(client).ConfigureAwait(false);
            using var allowedRemoval = await SendRemovePackageAsync(client, refreshedAntiforgery).ConfigureAwait(false);
            Assert.AreNotEqual(HttpStatusCode.Forbidden, allowedRemoval.StatusCode);
            Assert.AreEqual(HttpStatusCode.NotFound, allowedRemoval.StatusCode);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(directory, recursive: true);
        }
    }

    private static async Task<HttpResponseMessage> SendRemovePackageAsync(
        HttpClient client,
        AntiforgeryTokenResponse antiforgery)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Delete,
            "/api/v1/packages/de.juloc.test.absent")
        {
            Content = new StringContent(
                JsonSerializer.Serialize(new { revision = 1, deletePackageData = false }, JsonSerializerOptions.Web),
                Encoding.UTF8,
                "application/json"),
        };
        request.Headers.Add(antiforgery.HeaderName, antiforgery.Token);
        return await client.SendAsync(request).ConfigureAwait(false);
    }

    private static async Task<AntiforgeryTokenResponse> ReadAntiforgeryAsync(HttpClient client)
    {
        var token = await client
            .GetFromJsonAsync<AntiforgeryTokenResponse>("/api/v1/auth/antiforgery")
            .ConfigureAwait(false);
        return token ?? throw new AssertFailedException("No antiforgery token was returned.");
    }

    private static async Task RemoveAdministratorGrantAsync(string connectionString, string permission)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync().ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            "DELETE FROM permission_assignments WHERE permission = $permission AND subject_kind = 'Role'";
        command.Parameters.AddWithValue("$permission", permission);
        await command.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    private static async Task<long> CountAdministratorGrantAsync(string connectionString, string permission)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync().ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT COUNT(*) FROM permission_assignments WHERE permission = $permission AND subject_kind = 'Role'";
        command.Parameters.AddWithValue("$permission", permission);
        return (long)(await command.ExecuteScalarAsync().ConfigureAwait(false) ?? 0L);
    }
}
