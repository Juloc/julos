using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using JulOS.Application.Operations;
using JulOS.Contracts.Authentication;
using JulOS.Contracts.Errors;
using JulOS.Contracts.Operations;
using JulOS.Infrastructure.Persistence.Core;
using JulOS.Integration.Tests.Persistence;

using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace JulOS.Integration.Tests.Operations;

[TestClass]
[DoNotParallelize]
public sealed class OperationTests
{
    private const string AdministratorPassword = "Valid-Initial-Password-42!";

    private static readonly WebApplicationFactoryClientOptions ClientOptions = new()
    {
        BaseAddress = new Uri("https://localhost"),
        AllowAutoRedirect = false,
        HandleCookies = true,
    };

    [TestMethod]
    public async Task CreationIsQueuedIdempotentAndAntiforgeryProtected()
    {
        await using var database = await CreateMigratedDatabaseAsync().ConfigureAwait(false);
        using var host = new ServerHost(database.ConnectionString);
        using var client = host.CreateClient(ClientOptions);
        _ = await SetupAdministratorAsync(client).ConfigureAwait(false);

        var request = new CreateOperationRequest(
            "package.install",
            "de.juloc.julos.example",
            "package:de.juloc.julos.example",
            "install-example-1");

        using var missingToken = await client.PostAsJsonAsync("/api/v1/operations", request).ConfigureAwait(false);
        Assert.AreEqual(HttpStatusCode.BadRequest, missingToken.StatusCode);
        Assert.AreEqual(
            AuthenticationErrorCodes.AntiforgeryInvalid,
            await ReadProblemCodeAsync(missingToken).ConfigureAwait(false));

        var antiforgery = await ReadAntiforgeryAsync(client).ConfigureAwait(false);
        using var createdResponse = await SendMutationAsync(client, HttpMethod.Post, "/api/v1/operations", request, antiforgery).ConfigureAwait(false);
        Assert.AreEqual(HttpStatusCode.Accepted, createdResponse.StatusCode);
        var created = await createdResponse.Content.ReadFromJsonAsync<OperationResponse>().ConfigureAwait(false);
        Assert.IsNotNull(created);
        Assert.AreEqual(OperationStates.Queued, created.State);
        Assert.IsNull(created.StartedAtUtc);
        Assert.IsNull(created.CompletedAtUtc);
        Assert.AreEqual(1, created.Revision);

        using var repeatedResponse = await SendMutationAsync(client, HttpMethod.Post, "/api/v1/operations", request, antiforgery).ConfigureAwait(false);
        Assert.AreEqual(HttpStatusCode.Accepted, repeatedResponse.StatusCode);
        var repeated = await repeatedResponse.Content.ReadFromJsonAsync<OperationResponse>().ConfigureAwait(false);
        Assert.IsNotNull(repeated);
        Assert.AreEqual(created.OperationId, repeated.OperationId);
        Assert.AreEqual(created.Revision, repeated.Revision);

        using var conflict = await SendMutationAsync(
            client,
            HttpMethod.Post,
            "/api/v1/operations",
            request with { TargetReference = "package:de.juloc.julos.other" },
            antiforgery).ConfigureAwait(false);
        Assert.AreEqual(HttpStatusCode.Conflict, conflict.StatusCode);
        Assert.AreEqual(
            OperationErrorCodes.IdempotencyConflict,
            await ReadProblemCodeAsync(conflict).ConfigureAwait(false));

        var dataSource = host.Services.GetRequiredService<EndpointDataSource>();
        var createEndpoint = dataSource.Endpoints.OfType<RouteEndpoint>().Single(endpoint =>
            string.Equals(endpoint.RoutePattern.RawText?.TrimEnd('/'), "/api/v1/operations", StringComparison.Ordinal));
        Assert.IsNotNull(createEndpoint.Metadata.GetMetadata<IAntiforgeryMetadata>());
        Assert.IsNotNull(createEndpoint.Metadata.GetMetadata<IHttpMethodMetadata>());
    }

    [TestMethod]
    public async Task ProgressCancellationAndSafeFailureSurviveNewRequests()
    {
        await using var database = await CreateMigratedDatabaseAsync().ConfigureAwait(false);
        using var host = new ServerHost(database.ConnectionString);
        using var client = host.CreateClient(ClientOptions);
        var administrator = await SetupAdministratorAsync(client).ConfigureAwait(false);
        var antiforgery = await ReadAntiforgeryAsync(client).ConfigureAwait(false);

        var operation = await CreateOperationAsync(
            client,
            new CreateOperationRequest("backup.create", null, "backup:daily", "backup-daily-1"),
            antiforgery).ConfigureAwait(false);

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<IOperationService>();
            _ = await service.MarkRunningAsync(operation.OperationId).ConfigureAwait(false);
            _ = await service.ReportProgressAsync(operation.OperationId, 35, "backup.collecting").ConfigureAwait(false);
        }

        var running = await client.GetFromJsonAsync<OperationResponse>($"/api/v1/operations/{operation.OperationId:D}").ConfigureAwait(false);
        Assert.IsNotNull(running);
        Assert.AreEqual(OperationStates.Running, running.State);
        Assert.AreEqual(35, running.ProgressPercent);
        Assert.IsNull(running.CompletedAtUtc);

        var progress = await client.GetFromJsonAsync<OperationProgressEventResponse[]>($"/api/v1/operations/{operation.OperationId:D}/events").ConfigureAwait(false);
        Assert.IsNotNull(progress);
        Assert.AreEqual(1, progress.Length);
        Assert.AreEqual("backup.collecting", progress[0].CurrentStep);

        using var cancellation = await SendMutationAsync<object?>(
            client,
            HttpMethod.Post,
            $"/api/v1/operations/{operation.OperationId:D}/cancellation",
            body: null,
            antiforgery).ConfigureAwait(false);
        Assert.AreEqual(HttpStatusCode.Accepted, cancellation.StatusCode);
        var cancellationRequested = await cancellation.Content.ReadFromJsonAsync<OperationResponse>().ConfigureAwait(false);
        Assert.IsNotNull(cancellationRequested);
        Assert.AreEqual(OperationStates.Running, cancellationRequested.State);
        Assert.IsTrue(cancellationRequested.CancellationRequested);

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<IOperationService>();
            _ = await service.MarkCancelledAsync(operation.OperationId).ConfigureAwait(false);
        }

        var cancelled = await client.GetFromJsonAsync<OperationResponse>($"/api/v1/operations/{operation.OperationId:D}").ConfigureAwait(false);
        Assert.IsNotNull(cancelled);
        Assert.AreEqual(OperationStates.Cancelled, cancelled.State);
        Assert.IsNotNull(cancelled.CompletedAtUtc);

        Guid failedOperationId;
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<IOperationService>();
            var failedOperation = await service.CreateAsync(new CreateOperationCommand(
                administrator.UserId,
                "package.update",
                "de.juloc.julos.example",
                "package:de.juloc.julos.example",
                "update-example-1",
                "test-correlation")).ConfigureAwait(false);
            failedOperationId = failedOperation.OperationId;
            _ = await service.MarkRunningAsync(failedOperationId).ConfigureAwait(false);
            _ = await service.MarkFailedAsync(
                failedOperationId,
                "package.image_unavailable",
                "The package image could not be resolved.").ConfigureAwait(false);
        }

        var failed = await client.GetFromJsonAsync<OperationResponse>($"/api/v1/operations/{failedOperationId:D}").ConfigureAwait(false);
        Assert.IsNotNull(failed);
        Assert.AreEqual(OperationStates.Failed, failed.State);
        Assert.AreEqual("package.image_unavailable", failed.FailureCode);
        Assert.AreEqual("The package image could not be resolved.", failed.FailureDetail);
        Assert.IsNotNull(failed.CompletedAtUtc);
    }

    private static async Task<OperationResponse> CreateOperationAsync(
        HttpClient client,
        CreateOperationRequest request,
        AntiforgeryTokenResponse antiforgery)
    {
        using var response = await SendMutationAsync(client, HttpMethod.Post, "/api/v1/operations", request, antiforgery).ConfigureAwait(false);
        Assert.AreEqual(HttpStatusCode.Accepted, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<OperationResponse>().ConfigureAwait(false)
            ?? throw new AssertFailedException("Operation creation returned no resource.");
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

    private static async Task<string> ReadProblemCodeAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
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
