using System.Net;
using System.Net.Http.Json;

using JulOS.Application.Remote;
using JulOS.Contracts.Authentication;
using JulOS.Contracts.Remote;
using JulOS.Infrastructure.Persistence.Core;
using JulOS.Integration.Tests.Persistence;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace JulOS.Integration.Tests.Remote;

[TestClass]
[DoNotParallelize]
public sealed class RemoteSessionServiceTests
{
    private const string AdministratorPassword = "Valid-Initial-Password-42!";
    private const string CallerPackageId = "de.juloc.julos.remote";

    private static readonly WebApplicationFactoryClientOptions ClientOptions = new()
    {
        BaseAddress = new Uri("https://localhost"),
        AllowAutoRedirect = false,
        HandleCookies = true,
    };

    [TestMethod]
    public async Task CreateIsExactlyIdempotentAndOwnershipScoped()
    {
        await using var database = await CreateMigratedDatabaseAsync().ConfigureAwait(false);
        using var host = new ServerHost(database.ConnectionString);
        using var client = host.CreateClient(ClientOptions);
        var administrator = await SetupAdministratorAsync(client).ConfigureAwait(false);

        await using var scope = host.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IRemoteSessionService>();
        var request = CreateRequest("remote-create-1", "server.example.test");
        var command = new CreateRemoteSessionCommand(
            administrator.UserId,
            CallerPackageId,
            request);

        var created = await service.CreateAsync(command).ConfigureAwait(false);
        var repeated = await service.CreateAsync(command).ConfigureAwait(false);

        Assert.AreEqual(created.SessionId, repeated.SessionId);
        Assert.AreEqual(created.RequestIdentity, repeated.RequestIdentity);
        Assert.AreEqual(RemoteSessionStates.Requested, created.State);
        Assert.AreEqual(1L, created.Revision);
        Assert.IsNull(created.Display);
        Assert.IsNull(created.Failure);

        var conflict = await Assert.ThrowsExactlyAsync<RemoteSessionServiceException>(() =>
            service.CreateAsync(command with
            {
                Request = request with
                {
                    Target = new RemoteTargetContract("other.example.test", 3389),
                },
            })).ConfigureAwait(false);
        Assert.AreEqual(RemoteSessionServiceFailureReason.IdempotencyConflict, conflict.Reason);

        var foreignPackage = await Assert.ThrowsExactlyAsync<RemoteSessionServiceException>(() =>
            service.ReadAsync(new ReadRemoteSessionCommand(
                administrator.UserId,
                "de.juloc.other",
                new ReadRemoteSessionRequest(created.SessionId)))).ConfigureAwait(false);
        Assert.AreEqual(RemoteSessionServiceFailureReason.NotFound, foreignPackage.Reason);

        var foreignUser = await Assert.ThrowsExactlyAsync<RemoteSessionServiceException>(() =>
            service.ReadAsync(new ReadRemoteSessionCommand(
                Guid.CreateVersion7(),
                CallerPackageId,
                new ReadRemoteSessionRequest(created.SessionId)))).ConfigureAwait(false);
        Assert.AreEqual(RemoteSessionServiceFailureReason.NotFound, foreignUser.Reason);
    }

    [TestMethod]
    public async Task ListCursorAndCancellationSurviveDurableReloads()
    {
        await using var database = await CreateMigratedDatabaseAsync().ConfigureAwait(false);
        using var host = new ServerHost(database.ConnectionString);
        using var client = host.CreateClient(ClientOptions);
        var administrator = await SetupAdministratorAsync(client).ConfigureAwait(false);

        Guid firstSessionId;
        Guid secondSessionId;
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<IRemoteSessionService>();
            firstSessionId = (await service.CreateAsync(new CreateRemoteSessionCommand(
                administrator.UserId,
                CallerPackageId,
                CreateRequest("remote-page-1", "one.example.test"))).ConfigureAwait(false)).SessionId;
            secondSessionId = (await service.CreateAsync(new CreateRemoteSessionCommand(
                administrator.UserId,
                CallerPackageId,
                CreateRequest("remote-page-2", "two.example.test"))).ConfigureAwait(false)).SessionId;
            _ = await service.CreateAsync(new CreateRemoteSessionCommand(
                administrator.UserId,
                CallerPackageId,
                CreateRequest("remote-page-3", "three.example.test"))).ConfigureAwait(false);
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<IRemoteSessionService>();
            var firstPage = await service.ListAsync(new ListRemoteSessionsCommand(
                administrator.UserId,
                CallerPackageId,
                new ListRemoteSessionsRequest([], 2, Cursor: null))).ConfigureAwait(false);
            Assert.AreEqual(2, firstPage.Sessions.Count);
            Assert.IsNotNull(firstPage.NextCursor);

            var secondPage = await service.ListAsync(new ListRemoteSessionsCommand(
                administrator.UserId,
                CallerPackageId,
                new ListRemoteSessionsRequest([], 2, firstPage.NextCursor))).ConfigureAwait(false);
            Assert.AreEqual(1, secondPage.Sessions.Count);
            Assert.IsNull(secondPage.NextCursor);
            var allIds = firstPage.Sessions.Select(session => session.SessionId)
                .Concat(secondPage.Sessions.Select(session => session.SessionId))
                .ToArray();
            Assert.AreEqual(3, allIds.Distinct().Count());

            var first = await service.ReadAsync(new ReadRemoteSessionCommand(
                administrator.UserId,
                CallerPackageId,
                new ReadRemoteSessionRequest(firstSessionId))).ConfigureAwait(false);
            var cancelled = await service.CancelAsync(new CancelRemoteSessionCommand(
                administrator.UserId,
                CallerPackageId,
                new CancelRemoteSessionRequest(
                    firstSessionId,
                    "remote-cancel-1",
                    first.Revision,
                    "No longer needed."))).ConfigureAwait(false);
            Assert.AreEqual(RemoteSessionStates.Cancelled, cancelled.State);
            Assert.AreEqual(2L, cancelled.Revision);
            Assert.IsNotNull(cancelled.EndedAtUtc);

            var repeated = await service.CancelAsync(new CancelRemoteSessionCommand(
                administrator.UserId,
                CallerPackageId,
                new CancelRemoteSessionRequest(
                    firstSessionId,
                    "remote-cancel-repeat",
                    ExpectedRevision: 1,
                    Reason: null))).ConfigureAwait(false);
            Assert.AreEqual(cancelled.Revision, repeated.Revision);
            Assert.AreEqual(RemoteSessionStates.Cancelled, repeated.State);

            var stale = await Assert.ThrowsExactlyAsync<RemoteSessionServiceException>(() =>
                service.CancelAsync(new CancelRemoteSessionCommand(
                    administrator.UserId,
                    CallerPackageId,
                    new CancelRemoteSessionRequest(
                        secondSessionId,
                        "remote-cancel-stale",
                        ExpectedRevision: 99,
                        Reason: null)))).ConfigureAwait(false);
            Assert.AreEqual(RemoteSessionServiceFailureReason.ConcurrencyConflict, stale.Reason);
        }
    }

    private static CreateRemoteSessionRequest CreateRequest(string operationKey, string host)
    {
        var now = DateTimeOffset.UtcNow;
        return new CreateRemoteSessionRequest(
            operationKey,
            "rdp",
            new RemoteTargetContract(host, 3389),
            Guid.CreateVersion7(),
            ProfileId: null,
            NetworkProfileId: null,
            new RemoteViewportContract(1440, 900, 1m),
            IdleTimeoutSeconds: 120,
            MaximumSessionSeconds: 600,
            RequestedAtUtc: now,
            DeadlineUtc: now.AddMinutes(2));
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

    private static async Task<PostgreSqlTestDatabase> CreateMigratedDatabaseAsync()
    {
        var database = await PostgreSqlTestDatabase.CreateAsync().ConfigureAwait(false);
        await CoreDatabaseMigrator.MigrateAsync(database.ConnectionString).ConfigureAwait(false);
        return database;
    }
}
