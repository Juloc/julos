using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using JulOS.Application.Packages;
using JulOS.Application.Remote;
using JulOS.Application.Secrets;
using JulOS.Contracts.Remote;
using JulOS.Contracts.Runtime;
using JulOS.Infrastructure.Persistence.Core;
using JulOS.PackageSdk;

using Microsoft.EntityFrameworkCore;

namespace JulOS.Infrastructure.Packages;

/// <summary>Executes package-owned interactive runtime plans through Runtime Manager and Remote sessions.</summary>
internal sealed class InteractiveSessionCapabilityProvider : ICapabilityProvider
{
    internal const string ProviderPackageId = "julos.core.interactive-session";
    internal const string SecretPurpose = "remote.interactive.presentation";

    private const int MaximumRequestCharacters = 64 * 1024;
    private const int MaximumCredentialCharacters = 4096;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly CoreDbContext context;
    private readonly IPackageWorkerCommandDispatcher workers;
    private readonly InteractiveSessionCoordinator coordinator;
    private readonly IRemoteRuntimeManager runtimeManager;
    private readonly IRemoteRuntimePolicy remotePolicy;
    private readonly IRemoteSessionService sessions;
    private readonly IRemoteSessionProvisioner provisioner;
    private readonly IRemoteSessionLifecycleService lifecycle;
    private readonly ISecretReferenceService secrets;
    private readonly TimeProvider timeProvider;

    internal InteractiveSessionCapabilityProvider(
        CoreDbContext context,
        IPackageWorkerCommandDispatcher workers,
        InteractiveSessionCoordinator coordinator,
        IRemoteRuntimeManager runtimeManager,
        IRemoteRuntimePolicy remotePolicy,
        IRemoteSessionService sessions,
        IRemoteSessionProvisioner provisioner,
        IRemoteSessionLifecycleService lifecycle,
        ISecretReferenceService secrets,
        TimeProvider timeProvider)
    {
        this.context = context ?? throw new ArgumentNullException(nameof(context));
        this.workers = workers ?? throw new ArgumentNullException(nameof(workers));
        this.coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
        this.runtimeManager = runtimeManager ?? throw new ArgumentNullException(nameof(runtimeManager));
        this.remotePolicy = remotePolicy ?? throw new ArgumentNullException(nameof(remotePolicy));
        this.sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
        this.provisioner = provisioner ?? throw new ArgumentNullException(nameof(provisioner));
        this.lifecycle = lifecycle ?? throw new ArgumentNullException(nameof(lifecycle));
        this.secrets = secrets ?? throw new ArgumentNullException(nameof(secrets));
        this.timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public CapabilityProviderDescriptor Descriptor { get; } = new(
        ProviderPackageId,
        InteractiveSessionCapabilityContract.Name,
        InteractiveSessionCapabilityContract.Version,
        Priority: 1000,
        Healthy: true);

    public async Task<CapabilityResponse> InvokeAsync(
        CapabilityRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!string.Equals(request.CapabilityName, InteractiveSessionCapabilityContract.Name, StringComparison.Ordinal)
            || !string.Equals(request.ContractVersion, InteractiveSessionCapabilityContract.Version, StringComparison.Ordinal))
        {
            return Failure(
                "interactive.contract_incompatible",
                "The requested interactive-session capability contract is incompatible.");
        }
        if (request.Caller?.UserId is not Guid ownerUserId
            || ownerUserId == Guid.Empty
            || string.IsNullOrWhiteSpace(request.Caller.PackageId))
        {
            return Failure("interactive.caller_invalid", "An authenticated package caller is required.");
        }

        try
        {
            return request.Operation switch
            {
                InteractiveSessionCapabilityContract.CreateOperation => await this.CreateAsync(
                    ownerUserId,
                    request.Caller.PackageId,
                    Deserialize<CreateInteractiveSessionRequest>(request.Payload),
                    request.CorrelationId,
                    cancellationToken).ConfigureAwait(false),
                InteractiveSessionCapabilityContract.ReadOperation => Success(ToResponse(
                    await this.EnsureDisplayAsync(
                        await this.sessions.ReadAsync(
                            new ReadRemoteSessionCommand(
                                ownerUserId,
                                request.Caller.PackageId,
                                new ReadRemoteSessionRequest(
                                    Deserialize<ReadInteractiveSessionRequest>(request.Payload).SessionId)),
                            cancellationToken).ConfigureAwait(false),
                        ownerUserId,
                        request.Caller.PackageId,
                        cancellationToken).ConfigureAwait(false))),
                InteractiveSessionCapabilityContract.TerminateOperation => await this.TerminateAsync(
                    ownerUserId,
                    request.Caller.PackageId,
                    Deserialize<TerminateInteractiveSessionRequest>(request.Payload),
                    request.CorrelationId,
                    cancellationToken).ConfigureAwait(false),
                _ => Failure(
                    "interactive.operation_unsupported",
                    "The requested interactive-session operation is not supported."),
            };
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException)
        {
            return Failure("interactive.request_invalid", "The interactive-session request payload is invalid.");
        }
        catch (RemoteSessionContractException exception)
        {
            return Failure(exception.Code, exception.Message);
        }
        catch (RemoteSessionServiceException exception)
        {
            return ServiceFailure(exception);
        }
        catch (PackageManagementException)
        {
            return Failure("interactive.worker_unavailable", "Package worker is unavailable.");
        }
        catch (RemoteRuntimeManagerException exception)
        {
            return Failure(exception.Code, exception.Message);
        }
        catch (SecretReferenceFailureException exception)
        {
            return Failure(exception.Code, exception.Message);
        }
    }

    // A connected interactive session carries no presentation transport until one is issued on demand:
    // the caller-safe display descriptor is short-lived and minted only through a Remote resume, never
    // eagerly at connect. The interactive.session frontend polls read and attaches as soon as read
    // surfaces a display, so read (and idempotent create-recovery) issues and persists a fresh descriptor
    // for the owning caller once the session is connected. A stored, unexpired descriptor is returned as
    // is so repeated reads stay idempotent; a resume refusal falls back to the plain snapshot so reads of
    // a connected session never fail on presentation issuance alone.
    private async Task<RemoteSessionResponse> EnsureDisplayAsync(
        RemoteSessionResponse session,
        Guid ownerUserId,
        string callerPackageId,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(session.State, RemoteSessionStates.Connected, StringComparison.Ordinal))
        {
            return session;
        }
        if (session.Display is { } display && display.ExpiresAtUtc > this.timeProvider.GetUtcNow())
        {
            return session;
        }

        try
        {
            return await this.lifecycle.ResumeAsync(
                new ResumeRemoteSessionCommand(
                    ownerUserId,
                    callerPackageId,
                    new ResumeRemoteSessionRequest(session.SessionId, session.Revision)),
                cancellationToken).ConfigureAwait(false);
        }
        catch (RemoteSessionServiceException)
        {
            return session;
        }
    }

    private async Task<CapabilityResponse> CreateAsync(
        Guid ownerUserId,
        string callerPackageId,
        CreateInteractiveSessionRequest request,
        string correlationId,
        CancellationToken cancellationToken)
    {
        ValidateCreate(request);
        var runtimeId = RuntimeId(ownerUserId, callerPackageId, request);
        var targetHost = RuntimeHost(runtimeId);

        using var lease = await this.coordinator.EnterAsync(cancellationToken).ConfigureAwait(false);
        var existing = await this.context.RemoteSessions
            .AsNoTracking()
            .SingleOrDefaultAsync(
                row => row.OwnerUserId == ownerUserId
                    && row.CallerPackageId == callerPackageId
                    && row.OperationKey == request.OperationKey,
                cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            if (!string.Equals(existing.TargetHost, targetHost, StringComparison.Ordinal))
            {
                return Failure(
                    "interactive.idempotency_conflict",
                    "Interactive operation key was reused with a different request.");
            }

            var recovered = await this.sessions.ReadAsync(
                new ReadRemoteSessionCommand(
                    ownerUserId,
                    callerPackageId,
                    new ReadRemoteSessionRequest(existing.Id)),
                cancellationToken).ConfigureAwait(false);
            if (string.Equals(recovered.State, RemoteSessionStates.Requested, StringComparison.Ordinal))
            {
                recovered = await this.provisioner.ProvisionAsync(
                    new ProvisionRemoteSessionCommand(
                        ownerUserId,
                        callerPackageId,
                        recovered.SessionId,
                        recovered.Revision),
                    cancellationToken).ConfigureAwait(false);
            }
            return recovered.Failure is null
                ? Success(ToResponse(await this.EnsureDisplayAsync(
                    recovered,
                    ownerUserId,
                    callerPackageId,
                    cancellationToken).ConfigureAwait(false)))
                : Failure(recovered.Failure.Code, recovered.Failure.Detail);
        }

        var planResult = await this.workers.InvokeAsync(
            callerPackageId,
            new PackageWorkerCommand(
                InteractiveSessionWorkerCommands.ResolvePlan,
                JsonSerializer.SerializeToElement(
                    new ResolveInteractiveSessionPlanRequest(ownerUserId, request),
                    JsonOptions)),
            cancellationToken).ConfigureAwait(false);
        if (!planResult.Succeeded)
        {
            return Failure(
                planResult.ErrorCode ?? "interactive.plan_failed",
                planResult.ErrorDetail ?? "Interactive runtime plan could not be resolved.");
        }
        var plan = planResult.Payload.Deserialize<InteractiveSessionRuntimePlan>(JsonOptions)
            ?? throw new JsonException("Package worker returned no interactive runtime plan.");
        ValidatePlan(plan);

        RemoteRuntimeSelection remoteSelection;
        try
        {
            remoteSelection = this.remotePolicy.Resolve(
                plan.PresentationProtocol,
                null,
                new RemoteTargetContract(targetHost, plan.PresentationPort));
        }
        catch (RemoteRuntimePolicyException exception)
        {
            return Failure(exception.Code, exception.Message);
        }
        if (!remoteSelection.NetworkProfile.RuntimeNetworks.Contains(
                plan.RuntimeNetwork,
                StringComparer.Ordinal))
        {
            return Failure(
                "interactive.network_mismatch",
                "Interactive runtime network is not reachable by the selected presentation provider.");
        }

        SecretReferenceSnapshot? secret = null;
        RemoteSessionResponse? created = null;
        try
        {
            await this.runtimeManager.RemoveAsync(runtimeId, cancellationToken).ConfigureAwait(false);
            await this.runtimeManager.AllocateAndStartAsync(
                CreateRuntimeRequest(callerPackageId, runtimeId, plan),
                cancellationToken).ConfigureAwait(false);

            var secretBytes = Encoding.UTF8.GetBytes(plan.Credential.Value);
            try
            {
                secret = await this.secrets.CreateAsync(
                    new CreateSecretReferenceCommand(
                        ownerUserId,
                        SecretOwningScopeType.Package,
                        callerPackageId,
                        SecretPurpose,
                        secretBytes,
                        correlationId,
                        RemoteAddress: null),
                    cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(secretBytes);
            }

            var now = this.timeProvider.GetUtcNow();
            created = await this.sessions.CreateAsync(
                new CreateRemoteSessionCommand(
                    ownerUserId,
                    callerPackageId,
                    new CreateRemoteSessionRequest(
                        request.OperationKey,
                        plan.PresentationProtocol,
                        new RemoteTargetContract(targetHost, plan.PresentationPort),
                        secret.SecretReferenceId,
                        ProfileId: null,
                        remoteSelection.NetworkProfile.NetworkProfileId,
                        plan.Viewport,
                        plan.IdleTimeoutSeconds,
                        plan.MaximumSessionSeconds,
                        now,
                        now.AddSeconds(30))),
                cancellationToken).ConfigureAwait(false);

            var provisioned = await this.provisioner.ProvisionAsync(
                new ProvisionRemoteSessionCommand(
                    ownerUserId,
                    callerPackageId,
                    created.SessionId,
                    created.Revision),
                cancellationToken).ConfigureAwait(false);
            if (provisioned.Failure is not null)
            {
                await this.CleanupRuntimeAndSecretAsync(
                    callerPackageId,
                    runtimeId,
                    secret,
                    ownerUserId,
                    correlationId,
                    cancellationToken).ConfigureAwait(false);
                return Failure(provisioned.Failure.Code, provisioned.Failure.Detail);
            }

            return Success(ToResponse(provisioned));
        }
        catch
        {
            if (created is not null
                && string.Equals(created.State, RemoteSessionStates.Requested, StringComparison.Ordinal))
            {
                try
                {
                    _ = await this.sessions.CancelAsync(
                        new CancelRemoteSessionCommand(
                            ownerUserId,
                            callerPackageId,
                            new CancelRemoteSessionRequest(
                                created.SessionId,
                                $"interactive-cleanup-{created.SessionId:N}",
                                created.Revision,
                                "Interactive runtime startup failed.")),
                        cancellationToken).ConfigureAwait(false);
                }
                catch (RemoteSessionServiceException)
                {
                }
            }

            await this.CleanupRuntimeAndSecretAsync(
                callerPackageId,
                runtimeId,
                secret,
                ownerUserId,
                correlationId,
                cancellationToken).ConfigureAwait(false);
            throw;
        }
    }

    private async Task<CapabilityResponse> TerminateAsync(
        Guid ownerUserId,
        string callerPackageId,
        TerminateInteractiveSessionRequest request,
        string correlationId,
        CancellationToken cancellationToken)
    {
        if (request.SessionId == Guid.Empty || request.ExpectedRevision < 1)
        {
            return Failure("interactive.request_invalid", "Interactive termination request is invalid.");
        }

        var row = await this.context.RemoteSessions
            .AsNoTracking()
            .SingleOrDefaultAsync(
                candidate => candidate.Id == request.SessionId
                    && candidate.OwnerUserId == ownerUserId
                    && candidate.CallerPackageId == callerPackageId
                    && candidate.TargetHost.StartsWith("julos-interactive-"),
                cancellationToken).ConfigureAwait(false);
        if (row is null)
        {
            return Failure("interactive.session_not_found", "Interactive session was not found.");
        }

        RemoteSessionResponse session;
        if (RemoteSessionStates.IsTerminal(row.State))
        {
            session = await this.sessions.ReadAsync(
                new ReadRemoteSessionCommand(
                    ownerUserId,
                    callerPackageId,
                    new ReadRemoteSessionRequest(row.Id)),
                cancellationToken).ConfigureAwait(false);
        }
        else if (string.Equals(row.State, RemoteSessionStates.Requested, StringComparison.Ordinal))
        {
            session = await this.sessions.CancelAsync(
                new CancelRemoteSessionCommand(
                    ownerUserId,
                    callerPackageId,
                    new CancelRemoteSessionRequest(
                        row.Id,
                        $"interactive-terminate-{row.Id:N}",
                        request.ExpectedRevision,
                        "Interactive session terminated.")),
                cancellationToken).ConfigureAwait(false);
        }
        else if (row.State is RemoteSessionStates.Connecting
            or RemoteSessionStates.Connected
            or RemoteSessionStates.Disconnecting)
        {
            session = await this.lifecycle.DisconnectAsync(
                new DisconnectRemoteSessionCommand(
                    ownerUserId,
                    callerPackageId,
                    new DisconnectRemoteSessionRequest(
                        row.Id,
                        request.ExpectedRevision,
                        "Interactive session terminated.")),
                cancellationToken).ConfigureAwait(false);
        }
        else
        {
            return Failure(
                "interactive.session_busy",
                "Interactive session is still provisioning; retry termination shortly.");
        }

        await this.CleanupRuntimeAndSecretAsync(
            callerPackageId,
            RuntimeIdFromHost(row.TargetHost),
            await ReadSecretAsync(row.SecretReferenceId, cancellationToken).ConfigureAwait(false),
            ownerUserId,
            correlationId,
            cancellationToken).ConfigureAwait(false);
        return Success(ToResponse(session));
    }

    private async Task CleanupRuntimeAndSecretAsync(
        string callerPackageId,
        string runtimeId,
        SecretReferenceSnapshot? secret,
        Guid ownerUserId,
        string correlationId,
        CancellationToken cancellationToken)
    {
        await this.runtimeManager.RemoveAsync(runtimeId, cancellationToken).ConfigureAwait(false);
        if (secret is null || !secret.IsPresent)
        {
            return;
        }
        if (secret.OwningScopeType != SecretOwningScopeType.Package
            || !string.Equals(secret.OwningScopeId, callerPackageId, StringComparison.Ordinal)
            || !string.Equals(secret.Purpose, SecretPurpose, StringComparison.Ordinal))
        {
            return;
        }

        _ = await this.secrets.DeleteAsync(
            new DeleteSecretReferenceCommand(
                secret.SecretReferenceId,
                ownerUserId,
                secret.Revision,
                correlationId,
                RemoteAddress: null),
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<SecretReferenceSnapshot?> ReadSecretAsync(
        Guid secretReferenceId,
        CancellationToken cancellationToken)
    {
        try
        {
            return await this.secrets.ReadAsync(secretReferenceId, cancellationToken).ConfigureAwait(false);
        }
        catch (SecretReferenceFailureException exception)
            when (exception.Reason == SecretReferenceFailureReason.NotFound)
        {
            return null;
        }
    }

    private static CreatePackageRuntimeRequest CreateRuntimeRequest(
        string callerPackageId,
        string runtimeId,
        InteractiveSessionRuntimePlan plan) =>
        new(
            callerPackageId,
            plan.PackageVersion,
            runtimeId,
            plan.Image,
            plan.Limits,
            plan.Environment,
            [plan.RuntimeNetwork])
        {
            Volumes = plan.Volumes,
            SecretEnvironment = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [plan.Credential.EnvironmentName] = plan.Credential.Value,
            },
        };

    private static void ValidateCreate(CreateInteractiveSessionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.OperationKey)
            || request.OperationKey != request.OperationKey.Trim()
            || request.OperationKey.Length > 128
            || request.OperationKey.Any(char.IsControl)
            || request.Request.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null
            || request.Request.GetRawText().Length > MaximumRequestCharacters)
        {
            throw new JsonException("Interactive session request is invalid.");
        }
    }

    private static void ValidatePlan(InteractiveSessionRuntimePlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(plan.Limits);
        ArgumentNullException.ThrowIfNull(plan.Environment);
        ArgumentNullException.ThrowIfNull(plan.Volumes);
        ArgumentNullException.ThrowIfNull(plan.Credential);
        ArgumentNullException.ThrowIfNull(plan.Viewport);
        if (string.IsNullOrWhiteSpace(plan.PackageVersion)
            || string.IsNullOrWhiteSpace(plan.Image)
            || string.IsNullOrWhiteSpace(plan.RuntimeNetwork)
            || string.IsNullOrWhiteSpace(plan.PresentationProtocol)
            || plan.PresentationPort is < 1 or > 65535
            || string.IsNullOrWhiteSpace(plan.Credential.EnvironmentName)
            || string.IsNullOrEmpty(plan.Credential.Value)
            || plan.Credential.Value.Length > MaximumCredentialCharacters
            || plan.Credential.Value.IndexOfAny(['\0', '\r', '\n']) >= 0
            || plan.IdleTimeoutSeconds < 1
            || plan.MaximumSessionSeconds < plan.IdleTimeoutSeconds)
        {
            throw new JsonException("Package worker returned an invalid interactive runtime plan.");
        }
    }

    private static string RuntimeId(
        Guid ownerUserId,
        string callerPackageId,
        CreateInteractiveSessionRequest request)
    {
        var identity = string.Join(
            '\n',
            ownerUserId.ToString("N"),
            callerPackageId,
            request.OperationKey,
            request.Request.GetRawText());
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(identity));
        return "interactive-" + Convert.ToHexStringLower(digest)[..32];
    }

    internal static string RuntimeHost(string runtimeId) => "julos-" + runtimeId;

    internal static string RuntimeIdFromHost(string host)
    {
        const string prefix = "julos-interactive-";
        if (!host.StartsWith(prefix, StringComparison.Ordinal)
            || host.Length <= "julos-".Length)
        {
            throw new InvalidOperationException("Stored interactive runtime target is invalid.");
        }
        return host["julos-".Length..];
    }

    private static InteractiveSessionResponse ToResponse(RemoteSessionResponse response) => new(
        response.SessionId,
        response.State,
        response.CreatedAtUtc,
        response.ConnectedAtUtc,
        response.EndedAtUtc,
        response.Display,
        response.Failure,
        response.Revision);

    private static T Deserialize<T>(JsonElement payload) =>
        payload.Deserialize<T>(JsonOptions)
        ?? throw new JsonException("Interactive session payload is empty.");

    private static CapabilityResponse ServiceFailure(RemoteSessionServiceException exception) =>
        exception.Reason switch
        {
            RemoteSessionServiceFailureReason.NotFound => Failure(
                "interactive.session_not_found",
                exception.Message),
            RemoteSessionServiceFailureReason.IdempotencyConflict => Failure(
                "interactive.idempotency_conflict",
                exception.Message),
            RemoteSessionServiceFailureReason.ConcurrencyConflict => Failure(
                "interactive.concurrency_conflict",
                exception.Message),
            RemoteSessionServiceFailureReason.InvalidTransition => Failure(
                "interactive.state_invalid",
                exception.Message),
            _ => Failure("interactive.operation_failed", "Interactive session orchestration failed."),
        };

    private static CapabilityResponse Success<T>(T response) => new(
        true,
        null,
        null,
        JsonSerializer.SerializeToElement(response, JsonOptions));

    private static CapabilityResponse Failure(string code, string detail) => new(
        false,
        code,
        detail,
        JsonSerializer.SerializeToElement(new { }, JsonOptions));
}
