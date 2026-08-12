using System.Globalization;

using JulOS.Application.Concurrency;
using JulOS.Application.Operations;
using JulOS.Application.Remote;
using JulOS.Application.Secrets;
using JulOS.Contracts.Remote;
using JulOS.Contracts.Runtime;
using JulOS.Domain;
using JulOS.Domain.Packages;
using JulOS.Infrastructure.Persistence.Core;

using Microsoft.EntityFrameworkCore;

namespace JulOS.Infrastructure.Remote;

/// <summary>Assigns allowlisted provider runtimes to durable Remote sessions.</summary>
public sealed class PostgresRemoteSessionProvisioner : IRemoteSessionProvisioner
{
    private const string CallbackTokenEnvironmentName = "JULOS_REMOTE_CALLBACK_TOKEN";
    private const string TargetCredentialEnvironmentName = "JULOS_REMOTE_TARGET_CREDENTIAL";
    private const string CredentialOperationType = "remote.session.credential";
    private readonly CoreDbContext context;
    private readonly IRemoteSessionService sessions;
    private readonly ISecretReferenceService secrets;
    private readonly IOperationService operations;
    private readonly ISecretLeaseService leases;
    private readonly IRemoteRuntimePolicy runtimePolicy;
    private readonly IRemoteRuntimeManager runtimeManager;
    private readonly RemoteProviderCallbackAuthenticator callbackAuthenticator;
    private readonly TimeProvider timeProvider;

    /// <summary>Creates the persistent Remote runtime provisioner.</summary>
    public PostgresRemoteSessionProvisioner(
        CoreDbContext context,
        IRemoteSessionService sessions,
        ISecretReferenceService secrets,
        IOperationService operations,
        ISecretLeaseService leases,
        IRemoteRuntimePolicy runtimePolicy,
        IRemoteRuntimeManager runtimeManager,
        RemoteProviderCallbackAuthenticator callbackAuthenticator,
        TimeProvider timeProvider)
    {
        this.context = context ?? throw new ArgumentNullException(nameof(context));
        this.sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
        this.secrets = secrets ?? throw new ArgumentNullException(nameof(secrets));
        this.operations = operations ?? throw new ArgumentNullException(nameof(operations));
        this.leases = leases ?? throw new ArgumentNullException(nameof(leases));
        this.runtimePolicy = runtimePolicy ?? throw new ArgumentNullException(nameof(runtimePolicy));
        this.runtimeManager = runtimeManager ?? throw new ArgumentNullException(nameof(runtimeManager));
        this.callbackAuthenticator = callbackAuthenticator
            ?? throw new ArgumentNullException(nameof(callbackAuthenticator));
        this.timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    /// <inheritdoc />
    public async Task<RemoteSessionResponse> ProvisionAsync(
        ProvisionRemoteSessionCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var callerPackageId = ValidateCaller(command.OwnerUserId, command.CallerPackageId);
        var row = await this.context.RemoteSessions
            .SingleOrDefaultAsync(
                candidate => candidate.Id == command.SessionId
                    && candidate.OwnerUserId == command.OwnerUserId
                    && candidate.CallerPackageId == callerPackageId,
                cancellationToken)
            .ConfigureAwait(false)
            ?? throw new RemoteSessionServiceException(RemoteSessionServiceFailureReason.NotFound);

        if (string.Equals(row.State, RemoteSessionStates.Connecting, StringComparison.Ordinal)
            && row.RuntimeId is not null)
        {
            return await this.ReadAsync(command, cancellationToken).ConfigureAwait(false);
        }
        if (!string.Equals(row.State, RemoteSessionStates.Requested, StringComparison.Ordinal)
            && !string.Equals(row.State, RemoteSessionStates.Provisioning, StringComparison.Ordinal))
        {
            throw new RemoteSessionServiceException(RemoteSessionServiceFailureReason.InvalidTransition);
        }
        if (string.Equals(row.State, RemoteSessionStates.Requested, StringComparison.Ordinal)
            && row.Revision != command.ExpectedRevision)
        {
            throw new RemoteSessionServiceException(RemoteSessionServiceFailureReason.ConcurrencyConflict);
        }

        RemoteRuntimeSelection selection;
        try
        {
            selection = this.runtimePolicy.Resolve(
                row.Protocol,
                row.NetworkProfileId,
                new RemoteTargetContract(row.TargetHost, row.TargetPort));
        }
        catch (RemoteRuntimePolicyException exception)
        {
            return await this.MarkFailedAsync(row, exception.Code, exception.Message, false, command, cancellationToken)
                .ConfigureAwait(false);
        }

        string targetCredential;
        try
        {
            var secret = await this.secrets.ReadAsync(row.SecretReferenceId, cancellationToken).ConfigureAwait(false);
            if (!secret.IsPresent
                || secret.OwningScopeType != SecretOwningScopeType.Package
                || !string.Equals(secret.OwningScopeId, callerPackageId, StringComparison.Ordinal)
                || !secret.Purpose.StartsWith("remote.", StringComparison.Ordinal))
            {
                return await this.MarkCredentialUnavailableAsync(row, command, cancellationToken).ConfigureAwait(false);
            }

            var operation = await this.operations.CreateAsync(
                new CreateOperationCommand(
                    row.OwnerUserId,
                    CredentialOperationType,
                    callerPackageId,
                    row.Id.ToString("D"),
                    $"remote-session-credential-{row.Id:D}",
                    $"remote-{row.Id:N}"),
                cancellationToken).ConfigureAwait(false);
            if (operation.State == OperationState.Queued)
            {
                operation = await this.operations.MarkRunningAsync(operation.OperationId, cancellationToken)
                    .ConfigureAwait(false);
            }
            else if (operation.State != OperationState.Running)
            {
                return await this.MarkCredentialUnavailableAsync(row, command, cancellationToken).ConfigureAwait(false);
            }

            using var lease = await this.leases.AcquireAsync(row.SecretReferenceId, operation.OperationId, cancellationToken)
                .ConfigureAwait(false);
            targetCredential = Convert.ToBase64String(lease.Value.Span);

            try
            {
                _ = await this.operations.MarkSucceededAsync(operation.OperationId, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationFailureException)
            {
                // The lease already succeeded; completion bookkeeping is best effort.
            }
        }
        catch (SecretReferenceFailureException)
        {
            return await this.MarkCredentialUnavailableAsync(row, command, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationFailureException)
        {
            return await this.MarkCredentialUnavailableAsync(row, command, cancellationToken).ConfigureAwait(false);
        }

        var runtimeId = row.RuntimeId ?? $"remote-{row.Id:N}";
        string callbackToken;
        try
        {
            callbackToken = this.callbackAuthenticator.Issue(row.Id, runtimeId, row.ExpiresAtUtc);
        }
        catch (RemoteProviderCallbackAuthenticationException)
        {
            return await this.MarkFailedAsync(
                row,
                RemoteSessionFailureCodes.RuntimeUnavailable,
                "Remote provider callbacks are not configured.",
                false,
                command,
                cancellationToken).ConfigureAwait(false);
        }

        var now = this.timeProvider.GetUtcNow();
        if (string.Equals(row.State, RemoteSessionStates.Requested, StringComparison.Ordinal))
        {
            RemoteSessionContractValidator.ValidateTransition(row.State, RemoteSessionStates.Provisioning);
            row.State = RemoteSessionStates.Provisioning;
            row.RuntimeId = runtimeId;
            row.UpdatedAtUtc = now;
            row.LastActivityAtUtc = now;
            row.Revision = checked(row.Revision + 1);
            await this.SaveAsync(cancellationToken).ConfigureAwait(false);
        }
        else if (!string.Equals(row.RuntimeId, runtimeId, StringComparison.Ordinal))
        {
            throw new RemoteSessionServiceException(RemoteSessionServiceFailureReason.InvalidTransition);
        }

        var request = new CreatePackageRuntimeRequest(
            selection.Provider.ProviderPackageId,
            selection.Provider.PackageVersion,
            runtimeId,
            selection.Provider.Image,
            selection.Provider.Limits,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["JULOS_REMOTE_SESSION_ID"] = row.Id.ToString("D"),
                ["JULOS_REMOTE_PROTOCOL"] = row.Protocol,
                ["JULOS_REMOTE_TARGET_HOST"] = row.TargetHost,
                ["JULOS_REMOTE_TARGET_PORT"] = row.TargetPort.ToString(CultureInfo.InvariantCulture),
                ["JULOS_REMOTE_VIEWPORT_WIDTH"] = row.ViewportWidth.ToString(CultureInfo.InvariantCulture),
                ["JULOS_REMOTE_VIEWPORT_HEIGHT"] = row.ViewportHeight.ToString(CultureInfo.InvariantCulture),
                ["JULOS_REMOTE_DEVICE_SCALE_FACTOR"] = row.DeviceScaleFactor.ToString(CultureInfo.InvariantCulture),
                ["JULOS_REMOTE_IDLE_TIMEOUT_SECONDS"] = row.IdleTimeoutSeconds.ToString(CultureInfo.InvariantCulture),
                ["JULOS_REMOTE_MAXIMUM_SESSION_SECONDS"] = row.MaximumSessionSeconds.ToString(CultureInfo.InvariantCulture),
                ["JULOS_REMOTE_CALLBACK_ENDPOINT"] = this.callbackAuthenticator.Endpoint!.AbsoluteUri,
                ["JULOS_REMOTE_EXPECTED_REVISION"] = (row.Revision + 1).ToString(CultureInfo.InvariantCulture),
            },
            selection.NetworkProfile.RuntimeNetworks)
        {
            SecretEnvironment = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [CallbackTokenEnvironmentName] = callbackToken,
                [TargetCredentialEnvironmentName] = targetCredential,
            },
        };

        // Once a provider runtime is launched it will connect and post its callback
        // regardless of whether the caller still waits, so the launch and the state
        // commit that records it must not be tied to the caller's (short) request
        // deadline. A client or reverse-proxy disconnect during a slow first-image
        // pull would otherwise cancel provisioning and strand the started runtime,
        // whose connected-callback then cannot match the torn-down session and fails
        // with a 404. The Runtime Manager client still bounds this with its own
        // create timeout, so the launch cannot block indefinitely.
        var launch = CancellationToken.None;
        try
        {
            _ = await this.runtimeManager.AllocateAndStartAsync(request, launch).ConfigureAwait(false);
        }
        catch (RemoteRuntimeManagerException)
        {
            try
            {
                await this.runtimeManager.RemoveAsync(runtimeId, launch).ConfigureAwait(false);
            }
            catch (RemoteRuntimeManagerException)
            {
                // Cleanup is best effort. Runtime identity remains durable for reconciliation.
            }

            return await this.MarkFailedAsync(
                row,
                RemoteSessionFailureCodes.RuntimeUnavailable,
                "No compatible Remote provider runtime is currently available.",
                true,
                command,
                launch).ConfigureAwait(false);
        }

        RemoteSessionContractValidator.ValidateTransition(row.State, RemoteSessionStates.Connecting);
        now = this.timeProvider.GetUtcNow();
        row.State = RemoteSessionStates.Connecting;
        row.UpdatedAtUtc = now;
        row.LastActivityAtUtc = now;
        row.Revision = checked(row.Revision + 1);
        await this.SaveAsync(launch).ConfigureAwait(false);
        return await this.ReadAsync(command, launch).ConfigureAwait(false);
    }

    private Task<RemoteSessionResponse> MarkCredentialUnavailableAsync(
        RemoteSessionRow row,
        ProvisionRemoteSessionCommand command,
        CancellationToken cancellationToken) =>
        this.MarkFailedAsync(
            row,
            RemoteSessionFailureCodes.CredentialUnavailable,
            "The Remote credential reference is unavailable to the caller package.",
            false,
            command,
            cancellationToken);

    private async Task<RemoteSessionResponse> MarkFailedAsync(
        RemoteSessionRow row,
        string code,
        string detail,
        bool retryable,
        ProvisionRemoteSessionCommand command,
        CancellationToken cancellationToken)
    {
        RemoteSessionContractValidator.ValidateTransition(row.State, RemoteSessionStates.Failed);
        var now = this.timeProvider.GetUtcNow();
        row.State = RemoteSessionStates.Failed;
        row.FailureCode = code;
        row.FailureDetail = detail;
        row.FailureRetryable = retryable;
        row.UpdatedAtUtc = now;
        row.LastActivityAtUtc = now;
        row.EndedAtUtc = now;
        row.Revision = checked(row.Revision + 1);
        await this.SaveAsync(cancellationToken).ConfigureAwait(false);
        return await this.ReadAsync(command, cancellationToken).ConfigureAwait(false);
    }

    private Task<RemoteSessionResponse> ReadAsync(
        ProvisionRemoteSessionCommand command,
        CancellationToken cancellationToken) =>
        this.sessions.ReadAsync(
            new ReadRemoteSessionCommand(
                command.OwnerUserId,
                command.CallerPackageId,
                new ReadRemoteSessionRequest(command.SessionId)),
            cancellationToken);

    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await this.context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (ConcurrencyConflictException exception)
        {
            throw new RemoteSessionServiceException(
                RemoteSessionServiceFailureReason.ConcurrencyConflict,
                exception);
        }
    }

    private static string ValidateCaller(Guid ownerUserId, string callerPackageId)
    {
        if (ownerUserId == Guid.Empty)
        {
            throw new RemoteSessionServiceException(RemoteSessionServiceFailureReason.InvalidCaller);
        }

        try
        {
            return PackageId.Parse(callerPackageId).Value;
        }
        catch (DomainRuleViolationException exception)
        {
            throw new RemoteSessionServiceException(
                RemoteSessionServiceFailureReason.InvalidCaller,
                exception);
        }
    }
}
