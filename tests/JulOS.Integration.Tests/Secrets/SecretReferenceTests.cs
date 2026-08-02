using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using JulOS.Application.Operations;
using JulOS.Application.Secrets;
using JulOS.Contracts.Authentication;
using JulOS.Contracts.Errors;
using JulOS.Contracts.Operations;
using JulOS.Contracts.Secrets;
using JulOS.Infrastructure.Persistence.Core;
using JulOS.Integration.Tests.Persistence;

using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

using Npgsql;

namespace JulOS.Integration.Tests.Secrets;

[TestClass]
[DoNotParallelize]
public sealed class SecretReferenceTests
{
    private const string AdministratorPassword = "Valid-Initial-Password-42!";
    private const string PackageId = "de.juloc.secret-test";
    private const string InitialValue = "api008-initial-value-never-return";
    private const string RotatedValue = "api008-rotated-value-never-return";

    private static readonly WebApplicationFactoryClientOptions ClientOptions = new()
    {
        BaseAddress = new Uri("https://localhost"),
        AllowAutoRedirect = false,
        HandleCookies = true,
    };

    [TestMethod]
    public async Task ValuesStayEncryptedOpaqueAndOperationScopedAcrossLifecycle()
    {
        await using var database = await CreateMigratedDatabaseAsync().ConfigureAwait(false);
        using var host = new ServerHost(database.ConnectionString);
        using var client = host.CreateClient(ClientOptions);
        _ = await SetupAdministratorAsync(client).ConfigureAwait(false);

        var createRequest = new CreateSecretReferenceRequest(
            SecretReferenceScopeTypes.Package,
            PackageId,
            "remote.password",
            InitialValue);

        using var missingToken = await client.PostAsJsonAsync(
            "/api/v1/secret-references",
            createRequest).ConfigureAwait(false);
        Assert.AreEqual(HttpStatusCode.BadRequest, missingToken.StatusCode);

        var antiforgery = await ReadAntiforgeryAsync(client).ConfigureAwait(false);
        using var createdResponse = await SendMutationAsync(
            client,
            HttpMethod.Post,
            "/api/v1/secret-references",
            createRequest,
            antiforgery).ConfigureAwait(false);
        Assert.AreEqual(HttpStatusCode.Created, createdResponse.StatusCode);
        var createdBody = await createdResponse.Content.ReadAsStringAsync().ConfigureAwait(false);
        Assert.IsFalse(createdBody.Contains(InitialValue, StringComparison.Ordinal));
        var created = JsonSerializer.Deserialize<SecretReferenceResponse>(createdBody, JsonSerializerOptions.Web);
        Assert.IsNotNull(created);
        Assert.IsTrue(created.IsPresent);
        Assert.AreEqual(1, created.Revision);
        Assert.AreEqual(SecretStorageProviders.CoreAesGcmV1, created.StorageProvider);

        var stored = await ReadStoredSecretAsync(database.ConnectionString, created.SecretReferenceId).ConfigureAwait(false);
        Assert.AreEqual("primary", stored.KeyId);
        Assert.IsNotNull(stored.Nonce);
        Assert.IsNotNull(stored.Ciphertext);
        Assert.IsNotNull(stored.AuthenticationTag);
        Assert.IsFalse(Contains(stored.Ciphertext, Encoding.UTF8.GetBytes(InitialValue)));
        Assert.AreEqual(12, stored.Nonce.Length);
        Assert.AreEqual(16, stored.AuthenticationTag.Length);

        using var readResponse = await client.GetAsync(
            $"/api/v1/secret-references/{created.SecretReferenceId:D}").ConfigureAwait(false);
        Assert.AreEqual(HttpStatusCode.OK, readResponse.StatusCode);
        Assert.IsFalse((await readResponse.Content.ReadAsStringAsync().ConfigureAwait(false))
            .Contains(InitialValue, StringComparison.Ordinal));

        var operationId = await CreateRunningOperationAsync(
            host,
            client,
            antiforgery,
            PackageId,
            "secret-lease-1").ConfigureAwait(false);
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var leases = scope.ServiceProvider.GetRequiredService<ISecretLeaseService>();
            using var lease = await leases
                .AcquireAsync(created.SecretReferenceId, operationId)
                .ConfigureAwait(false);
            Assert.AreEqual(InitialValue, Encoding.UTF8.GetString(lease.Value.Span));
            Assert.AreEqual(operationId, lease.OperationId);
        }

        var wrongOperationId = await CreateRunningOperationAsync(
            host,
            client,
            antiforgery,
            "de.juloc.other-package",
            "secret-lease-2").ConfigureAwait(false);
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var leases = scope.ServiceProvider.GetRequiredService<ISecretLeaseService>();
            var denied = await Assert.ThrowsExactlyAsync<SecretReferenceFailureException>(() =>
                leases.AcquireAsync(created.SecretReferenceId, wrongOperationId)).ConfigureAwait(false);
            Assert.AreEqual(SecretReferenceFailureReason.LeaseDenied, denied.Reason);
        }

        using var rotatedResponse = await SendMutationAsync(
            client,
            HttpMethod.Post,
            $"/api/v1/secret-references/{created.SecretReferenceId:D}/rotation",
            new RotateSecretReferenceRequest(RotatedValue, created.Revision),
            antiforgery).ConfigureAwait(false);
        Assert.AreEqual(HttpStatusCode.OK, rotatedResponse.StatusCode);
        var rotatedBody = await rotatedResponse.Content.ReadAsStringAsync().ConfigureAwait(false);
        Assert.IsFalse(rotatedBody.Contains(RotatedValue, StringComparison.Ordinal));
        var rotated = JsonSerializer.Deserialize<SecretReferenceResponse>(rotatedBody, JsonSerializerOptions.Web);
        Assert.IsNotNull(rotated);
        Assert.AreEqual(2, rotated.Revision);
        Assert.IsNotNull(rotated.RotatedAtUtc);

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var leases = scope.ServiceProvider.GetRequiredService<ISecretLeaseService>();
            using var lease = await leases
                .AcquireAsync(created.SecretReferenceId, operationId)
                .ConfigureAwait(false);
            Assert.AreEqual(RotatedValue, Encoding.UTF8.GetString(lease.Value.Span));
        }

        using var deletedResponse = await SendMutationAsync<object?>(
            client,
            HttpMethod.Delete,
            $"/api/v1/secret-references/{created.SecretReferenceId:D}?revision={rotated.Revision}",
            body: null,
            antiforgery).ConfigureAwait(false);
        Assert.AreEqual(HttpStatusCode.NoContent, deletedResponse.StatusCode);

        var deleted = await ReadStoredSecretAsync(database.ConnectionString, created.SecretReferenceId).ConfigureAwait(false);
        Assert.IsNull(deleted.KeyId);
        Assert.IsNull(deleted.Ciphertext);
        Assert.IsNull(deleted.Nonce);
        Assert.IsNull(deleted.AuthenticationTag);
        Assert.IsNotNull(deleted.DeletedAtUtc);

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var leases = scope.ServiceProvider.GetRequiredService<ISecretLeaseService>();
            var denied = await Assert.ThrowsExactlyAsync<SecretReferenceFailureException>(() =>
                leases.AcquireAsync(created.SecretReferenceId, operationId)).ConfigureAwait(false);
            Assert.AreEqual(SecretReferenceFailureReason.LeaseDenied, denied.Reason);
        }

        await AssertAuditContainsNoSecretAsync(database.ConnectionString).ConfigureAwait(false);

        var dataSource = host.Services.GetRequiredService<EndpointDataSource>();
        var mutations = dataSource.Endpoints.OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.RoutePattern.RawText?.Contains("secret-references", StringComparison.Ordinal) == true)
            .Where(endpoint => endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods
                .Any(method => !string.Equals(method, HttpMethods.Get, StringComparison.Ordinal)) == true)
            .ToArray();
        Assert.AreEqual(3, mutations.Length);
        Assert.IsTrue(mutations.All(endpoint => endpoint.Metadata.GetMetadata<IAntiforgeryMetadata>() is not null));
    }

    [TestMethod]
    public async Task StaleRotationReturnsCurrentRevisionWithoutEchoingSubmittedValue()
    {
        await using var database = await CreateMigratedDatabaseAsync().ConfigureAwait(false);
        using var host = new ServerHost(database.ConnectionString);
        using var client = host.CreateClient(ClientOptions);
        _ = await SetupAdministratorAsync(client).ConfigureAwait(false);
        var antiforgery = await ReadAntiforgeryAsync(client).ConfigureAwait(false);

        var created = await CreateSecretAsync(client, antiforgery, InitialValue).ConfigureAwait(false);
        using var firstRotation = await SendMutationAsync(
            client,
            HttpMethod.Post,
            $"/api/v1/secret-references/{created.SecretReferenceId:D}/rotation",
            new RotateSecretReferenceRequest(RotatedValue, created.Revision),
            antiforgery).ConfigureAwait(false);
        Assert.AreEqual(HttpStatusCode.OK, firstRotation.StatusCode);

        const string staleValue = "api008-stale-value-never-return";
        using var stale = await SendMutationAsync(
            client,
            HttpMethod.Post,
            $"/api/v1/secret-references/{created.SecretReferenceId:D}/rotation",
            new RotateSecretReferenceRequest(staleValue, created.Revision),
            antiforgery).ConfigureAwait(false);
        Assert.AreEqual(HttpStatusCode.Conflict, stale.StatusCode);
        var problem = await stale.Content.ReadAsStringAsync().ConfigureAwait(false);
        Assert.IsFalse(problem.Contains(staleValue, StringComparison.Ordinal));
        using var document = JsonDocument.Parse(problem);
        Assert.AreEqual(2, document.RootElement.GetProperty(ProblemExtensionNames.CurrentRevision).GetInt32());
    }

    private static async Task<SecretReferenceResponse> CreateSecretAsync(
        HttpClient client,
        AntiforgeryTokenResponse antiforgery,
        string value)
    {
        using var response = await SendMutationAsync(
            client,
            HttpMethod.Post,
            "/api/v1/secret-references",
            new CreateSecretReferenceRequest(
                SecretReferenceScopeTypes.Package,
                PackageId,
                "remote.password",
                value),
            antiforgery).ConfigureAwait(false);
        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<SecretReferenceResponse>().ConfigureAwait(false)
            ?? throw new AssertFailedException("Secret creation returned no metadata.");
    }

    private static async Task<Guid> CreateRunningOperationAsync(
        ServerHost host,
        HttpClient client,
        AntiforgeryTokenResponse antiforgery,
        string packageId,
        string idempotencyKey)
    {
        using var response = await SendMutationAsync(
            client,
            HttpMethod.Post,
            "/api/v1/operations",
            new CreateOperationRequest(
                "secret.lease.test",
                packageId,
                $"package:{packageId}",
                idempotencyKey),
            antiforgery).ConfigureAwait(false);
        Assert.AreEqual(HttpStatusCode.Accepted, response.StatusCode);
        var operation = await response.Content.ReadFromJsonAsync<OperationResponse>().ConfigureAwait(false)
            ?? throw new AssertFailedException("Operation creation returned no resource.");

        await using var scope = host.Services.CreateAsyncScope();
        var operations = scope.ServiceProvider.GetRequiredService<IOperationService>();
        _ = await operations.MarkRunningAsync(operation.OperationId).ConfigureAwait(false);
        return operation.OperationId;
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

    private static async Task AssertAuditContainsNoSecretAsync(string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync().ConfigureAwait(false);
        await using var command = new NpgsqlCommand(
            """
            SELECT action, summary, safe_details
            FROM core.audit_events
            WHERE action LIKE 'secret_reference.%'
            ORDER BY occurred_at_utc
            """,
            connection);
        await using var reader = await command.ExecuteReaderAsync().ConfigureAwait(false);
        var rows = new List<string>();
        while (await reader.ReadAsync().ConfigureAwait(false))
        {
            rows.Add(string.Join("|", reader.GetString(0), reader.GetString(1), reader.GetString(2)));
        }

        Assert.AreEqual(3, rows.Count);
        var joined = string.Join("\n", rows);
        Assert.IsFalse(joined.Contains(InitialValue, StringComparison.Ordinal));
        Assert.IsFalse(joined.Contains(RotatedValue, StringComparison.Ordinal));
        Assert.IsTrue(rows.All(row => row.Contains("Secret value omitted.", StringComparison.Ordinal)));
    }

    private static async Task<StoredSecret> ReadStoredSecretAsync(string connectionString, Guid id)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync().ConfigureAwait(false);
        await using var command = new NpgsqlCommand(
            """
            SELECT encryption_key_id, nonce, ciphertext, authentication_tag, deleted_at_utc
            FROM core.secret_references
            WHERE id = @id
            """,
            connection);
        command.Parameters.AddWithValue("id", id);
        await using var reader = await command.ExecuteReaderAsync().ConfigureAwait(false);
        Assert.IsTrue(await reader.ReadAsync().ConfigureAwait(false));
        return new StoredSecret(
            reader.IsDBNull(0) ? null : reader.GetString(0),
            reader.IsDBNull(1) ? null : reader.GetFieldValue<byte[]>(1),
            reader.IsDBNull(2) ? null : reader.GetFieldValue<byte[]>(2),
            reader.IsDBNull(3) ? null : reader.GetFieldValue<byte[]>(3),
            reader.IsDBNull(4) ? null : reader.GetFieldValue<DateTimeOffset>(4));
    }

    private static bool Contains(byte[]? value, byte[] expected) =>
        value is not null && value.AsSpan().IndexOf(expected) >= 0;

    private static async Task<PostgreSqlTestDatabase> CreateMigratedDatabaseAsync()
    {
        var database = await PostgreSqlTestDatabase.CreateAsync().ConfigureAwait(false);
        await CoreDatabaseMigrator.MigrateAsync(database.ConnectionString).ConfigureAwait(false);
        return database;
    }

    private sealed record StoredSecret(
        string? KeyId,
        byte[]? Nonce,
        byte[]? Ciphertext,
        byte[]? AuthenticationTag,
        DateTimeOffset? DeletedAtUtc);
}
