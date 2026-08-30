using System.Text.Json;

using JulOS.Application.Remote;
using JulOS.Application.Secrets;
using JulOS.Contracts.Remote;
using JulOS.Infrastructure.Remote;
using JulOS.PackageSdk;

namespace JulOS.Infrastructure.Tests.Remote;

[TestClass]
public sealed class RemoteSessionCapabilityProviderTests
{
    [TestMethod]
    public async Task CreateReturnsDurableRequestedSessionAndSignalsProvisioning()
    {
        var sessionId = Guid.Parse("11111111-2222-4333-8444-555555555555");
        var ownerUserId = Guid.Parse("aaaaaaaa-bbbb-4ccc-8ddd-eeeeeeeeeeee");
        var sessions = new RecordingSessionService(sessionId);
        var signal = new RecordingProvisioningSignal();
        var provider = new RemoteSessionCapabilityProvider(
            sessions,
            signal,
            new UnusedLifecycleService(),
            new UnusedSecretReferenceService());
        var now = DateTimeOffset.UtcNow;
        var create = new CreateRemoteSessionRequest(
            "remote-capability-create",
            "rdp",
            new RemoteTargetContract("server.example.test", 3389),
            Guid.Parse("99999999-8888-4777-8666-555555555555"),
            ProfileId: null,
            NetworkProfileId: null,
            new RemoteViewportContract(1440, 900, 1m),
            IdleTimeoutSeconds: 120,
            MaximumSessionSeconds: 600,
            RequestedAtUtc: now,
            DeadlineUtc: now.AddMinutes(2));
        var request = new CapabilityRequest(
            RemoteSessionCapabilityContract.Name,
            RemoteSessionCapabilityContract.Version,
            RemoteSessionCapabilityContract.CreateOperation,
            "remote-capability-create-test",
            JsonSerializer.SerializeToElement(create),
            now.AddSeconds(5),
            new CapabilityCallerContext("de.juloc.julos.remote", ownerUserId));

        var result = await provider.InvokeAsync(request).ConfigureAwait(false);

        Assert.IsTrue(result.Succeeded, result.ErrorDetail);
        Assert.AreEqual(1, sessions.CreateCount);
        Assert.AreEqual(1, signal.SignalCount);
        var response = result.Payload.Deserialize<RemoteSessionResponse>(new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.IsNotNull(response);
        Assert.AreEqual(sessionId, response.SessionId);
        Assert.AreEqual(RemoteSessionStates.Requested, response.State);
        Assert.AreEqual(1L, response.Revision);
    }

    private sealed class RecordingSessionService(Guid sessionId) : IRemoteSessionService
    {
        internal int CreateCount { get; private set; }

        public Task<RemoteSessionResponse> CreateAsync(
            CreateRemoteSessionCommand command,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            this.CreateCount++;
            var now = DateTimeOffset.UtcNow;
            return Task.FromResult(new RemoteSessionResponse(
                sessionId,
                command.Request.OperationKey,
                "request-identity",
                command.Request.Protocol,
                command.Request.Target,
                RemoteSessionStates.Requested,
                now,
                ConnectedAtUtc: null,
                EndedAtUtc: null,
                Display: null,
                Failure: null,
                Revision: 1));
        }

        public Task<RemoteSessionResponse> ReadAsync(
            ReadRemoteSessionCommand command,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<RemoteSessionListResponse> ListAsync(
            ListRemoteSessionsCommand command,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<RemoteSessionResponse> CancelAsync(
            CancelRemoteSessionCommand command,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class RecordingProvisioningSignal : IRemoteSessionProvisioningSignal
    {
        internal int SignalCount { get; private set; }

        public void Signal() => this.SignalCount++;

        public ValueTask WaitAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class UnusedLifecycleService : IRemoteSessionLifecycleService
    {
        public Task<RemoteSessionResponse> DisconnectAsync(
            DisconnectRemoteSessionCommand command,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<RemoteSessionResponse> DetachAsync(
            DetachRemoteSessionCommand command,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<RemoteSessionResponse> ResumeAsync(
            ResumeRemoteSessionCommand command,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<RemoteLifecycleReconciliationResult> ReconcileDueAsync(
            int limit,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class UnusedSecretReferenceService : ISecretReferenceService
    {
        public Task<SecretReferenceSnapshot> CreateAsync(
            CreateSecretReferenceCommand command,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<SecretReferenceSnapshot> ReadAsync(
            Guid secretReferenceId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<SecretReferenceSnapshot> RotateAsync(
            RotateSecretReferenceCommand command,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<SecretReferenceSnapshot> DeleteAsync(
            DeleteSecretReferenceCommand command,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
