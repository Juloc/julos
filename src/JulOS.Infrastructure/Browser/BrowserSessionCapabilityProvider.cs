using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using JulOS.Application.Packages;
using JulOS.Application.Remote;
using JulOS.Application.Secrets;
using JulOS.Contracts.Browser;
using JulOS.Contracts.Remote;
using JulOS.Contracts.Runtime;
using JulOS.Infrastructure.Packages;
using JulOS.Infrastructure.Persistence.Core;
using JulOS.PackageSdk;

using Microsoft.EntityFrameworkCore;

namespace JulOS.Infrastructure.Browser;

/// <summary>Creates isolated Chromium runtimes and reuses Remote VNC presentation sessions.</summary>
internal sealed class BrowserSessionCapabilityProvider : ICapabilityProvider
{
    internal const string ProviderPackageId = "julos.core.browser";

    private const string BrowserPackageId = "de.juloc.julos.browser";
    private const string VncSecretPurpose = "remote.browser.vnc";
    private const string PersistentProfileTarget = "/var/lib/julos-browser/profile";
    private const int BrowserVncPort = 5900;
    private const int MaximumSessionSeconds = 86400;
    private static readonly RuntimeResourceLimits RuntimeLimits = new(1024, 2m, 256);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly CoreDbContext context;
    private readonly IPackageWorkerCommandDispatcher workers;
    private readonly BrowserRuntimeOptions runtimeOptions;
    private readonly BrowserSessionCoordinator coordinator;
    private readonly IRemoteRuntimeManager runtimeManager;
    private readonly IRemoteRuntimePolicy remotePolicy;
    private readonly IRemoteSessionService sessions;
    private readonly IRemoteSessionProvisioner provisioner;
    private readonly IRemoteSessionLifecycleService lifecycle;
    private readonly ISecretReferenceService secrets;
    private readonly TimeProvider timeProvider;

    internal BrowserSessionCapabilityProvider(
        CoreDbContext context,
        IPackageWorkerCommandDispatcher workers,
        BrowserRuntimeOptions runtimeOptions,
        BrowserSessionCoordinator coordinator,
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
        this.runtimeOptions = runtimeOptions ?? throw new ArgumentNullException(nameof(runtimeOptions));
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
        BrowserSessionCapabilityContract.Name,
        BrowserSessionCapabilityContract.Version,
        Priority: 1000,
        Healthy: true);

    public async Task<CapabilityResponse> InvokeAsync(
        CapabilityRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!string.Equals(request.CapabilityName, BrowserSessionCapabilityContract.Name, StringComparison.Ordinal)
            || !string.Equals(request.ContractVersion, BrowserSessionCapabilityContract.Version, StringComparison.Ordinal))
        {
            return Failure(
                "browser.contract_incompatible",
                "The requested Browser session capability contract is incompatible.");
        }
        if (request.Caller?.UserId is not Guid ownerUserId
            || ownerUserId == Guid.Empty
            || !string.Equals(request.Caller.PackageId, BrowserPackageId, StringComparison.Ordinal))
        {
            return Failure("browser.caller_invalid", "An authenticated Browser package caller is required.");
        }

        try
        {
            return request.Operation switch
            {
                BrowserSessionCapabilityContract.CreateOperation => await this.CreateAsync(
                    ownerUserId,
                    Deserialize<CreateBrowserSessionRequest>(request.Payload),
                    request.CorrelationId,
                    cancellationToken).ConfigureAwait(false),
                BrowserSessionCapabilityContract.ReadOperation => Success(ToBrowserResponse(
                    await this.sessions.ReadAsync(
                        new ReadRemoteSessionCommand(
                            ownerUserId,
                            BrowserPackageId,
                            new ReadRemoteSessionRequest(
                                Deserialize<ReadBrowserSessionRequest>(request.Payload).SessionId)),
                        cancellationToken).ConfigureAwait(false))),
                BrowserSessionCapabilityContract.TerminateOperation => await this.TerminateAsync(
                    ownerUserId,
                    Deserialize<TerminateBrowserSessionRequest>(request.Payload),
                    request.CorrelationId,
                    cancellationToken).ConfigureAwait(false),
                _ => Failure(
                    "browser.operation_unsupported",
                    "The requested Browser session operation is not supported."),
            };
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException)
        {
            return Failure("browser.request_invalid", "The Browser session request payload is invalid.");
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
            return Failure("browser.worker_unavailable", "Browser worker is unavailable.");
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

    private async Task<CapabilityResponse> CreateAsync(
        Guid ownerUserId,
        CreateBrowserSessionRequest request,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var normalized = ValidateCreate(request);
        var runtimeId = RuntimeId(ownerUserId, normalized);
        var targetHost = RuntimeHost(runtimeId);

        using var lease = await this.coordinator.EnterAsync(cancellationToken).ConfigureAwait(false);
        var existing = await this.context.RemoteSessions
            .AsNoTracking()
            .SingleOrDefaultAsync(
                row => row.OwnerUserId == ownerUserId
                    && row.CallerPackageId == BrowserPackageId
                    && row.OperationKey == normalized.OperationKey,
                cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            if (!string.Equals(existing.TargetHost, targetHost, StringComparison.Ordinal)
                || existing.TargetPort != BrowserVncPort)
            {
                return Failure(
                    "browser.idempotency_conflict",
                    "Browser operation key was reused with a different request.");
            }

            var recovered = await this.sessions.ReadAsync(
                new ReadRemoteSessionCommand(
                    ownerUserId,
                    BrowserPackageId,
                    new ReadRemoteSessionRequest(existing.Id)),
                cancellationToken).ConfigureAwait(false);
            if (string.Equals(recovered.State, RemoteSessionStates.Requested, StringComparison.Ordinal))
            {
                recovered = await this.provisioner.ProvisionAsync(
                    new ProvisionRemoteSessionCommand(
                        ownerUserId,
                        BrowserPackageId,
                        recovered.SessionId,
                        recovered.Revision),
                    cancellationToken).ConfigureAwait(false);
            }
            return recovered.Failure is null
                ? Success(ToBrowserResponse(recovered))
                : Failure(recovered.Failure.Code, recovered.Failure.Detail);
        }

        if (!this.runtimeOptions.IsConfigured)
        {
            return Failure(
                "browser.runtime_not_configured",
                "Browser runtime image is not configured.");
        }

        var planResult = await this.workers.InvokeAsync(
            BrowserPackageId,
            new PackageWorkerCommand(
                BrowserWorkerCommands.ResolveSessionPlan,
                JsonSerializer.SerializeToElement(
                    new ResolveBrowserSessionPlanRequest(ownerUserId, normalized),
                    JsonOptions)),
            cancellationToken).ConfigureAwait(false);
        if (!planResult.Succeeded)
        {
            return Failure(
                planResult.ErrorCode ?? "browser.plan_failed",
                planResult.ErrorDetail ?? "Browser runtime plan could not be resolved.");
        }
        var plan = planResult.Payload.Deserialize<BrowserSessionRuntimePlan>(JsonOptions)
            ?? throw new JsonException("Browser worker returned no runtime plan.");
        ValidatePlan(plan);

        RemoteRuntimeSelection remoteSelection;
        try
        {
            remoteSelection = this.remotePolicy.Resolve(
                "vnc",
                null,
                new RemoteTargetContract(targetHost, BrowserVncPort));
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
                "browser.network_mismatch",
                "Browser runtime network is not reachable by the configured Remote VNC provider.");
        }

        var password = CreateVncPassword();
        SecretReferenceSnapshot? secret = null;
        RemoteSessionResponse? created = null;
        try
        {
            await this.runtimeManager.RemoveAsync(runtimeId, cancellationToken).ConfigureAwait(false);
            await this.runtimeManager.AllocateAndStartAsync(
                CreateRuntimeRequest(runtimeId, plan, password),
                cancellationToken).ConfigureAwait(false);

            var secretBytes = Encoding.UTF8.GetBytes(password);
            try
            {
                secret = await this.secrets.CreateAsync(
                    new CreateSecretReferenceCommand(
                        ownerUserId,
                        SecretOwningScopeType.Package,
                        BrowserPackageId,
                        VncSecretPurpose,
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
                    BrowserPackageId,
                    new CreateRemoteSessionRequest(
                        normalized.OperationKey,
                        "vnc",
                        new RemoteTargetContract(targetHost, BrowserVncPort),
                        secret.SecretReferenceId,
                        ProfileId: null,
                        remoteSelection.NetworkProfile.NetworkProfileId,
                        new RemoteViewportContract(1280, 800, 1m),
                        plan.IdleTimeoutSeconds,
                        MaximumSessionSeconds,
                        now,
                        now.AddSeconds(30))),
                cancellationToken).ConfigureAwait(false);

            var provisioned = await this.provisioner.ProvisionAsync(
                new ProvisionRemoteSessionCommand(
                    ownerUserId,
                    BrowserPackageId,
                    created.SessionId,
                    created.Revision),
                cancellationToken).ConfigureAwait(false);
            if (provisioned.Failure is not null)
            {
                await this.CleanupRuntimeAndSecretAsync(
                    runtimeId,
                    secret,
                    ownerUserId,
                    correlationId,
                    cancellationToken).ConfigureAwait(false);
                return Failure(provisioned.Failure.Code, provisioned.Failure.Detail);
            }

            return Success(ToBrowserResponse(provisioned));
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
                            BrowserPackageId,
                            new CancelRemoteSessionRequest(
                                created.SessionId,
                                $"browser-cleanup-{created.SessionId:N}",
                                created.Revision,
                                "Browser startup failed.")),
                        cancellationToken).ConfigureAwait(false);
                }
                catch (RemoteSessionServiceException)
                {
                }
            }

            await this.CleanupRuntimeAndSecretAsync(
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
        TerminateBrowserSessionRequest request,
        string correlationId,
        CancellationToken cancellationToken)
    {
        if (request.SessionId == Guid.Empty || request.ExpectedRevision < 1)
        {
            return Failure("browser.request_invalid", "Browser termination request is invalid.");
        }

        var row = await this.context.RemoteSessions
            .AsNoTracking()
            .SingleOrDefaultAsync(
                candidate => candidate.Id == request.SessionId
                    && candidate.OwnerUserId == ownerUserId
                    && candidate.CallerPackageId == BrowserPackageId,
                cancellationToken).ConfigureAwait(false);
        if (row is null)
        {
            return Failure("browser.session_not_found", "Browser session was not found.");
        }

        RemoteSessionResponse session;
        if (RemoteSessionStates.IsTerminal(row.State))
        {
            session = await this.sessions.ReadAsync(
                new ReadRemoteSessionCommand(
                    ownerUserId,
                    BrowserPackageId,
                    new ReadRemoteSessionRequest(row.Id)),
                cancellationToken).ConfigureAwait(false);
        }
        else if (string.Equals(row.State, RemoteSessionStates.Requested, StringComparison.Ordinal))
        {
            session = await this.sessions.CancelAsync(
                new CancelRemoteSessionCommand(
                    ownerUserId,
                    BrowserPackageId,
                    new CancelRemoteSessionRequest(
                        row.Id,
                        $"browser-terminate-{row.Id:N}",
                        request.ExpectedRevision,
                        "Browser session terminated.")),
                cancellationToken).ConfigureAwait(false);
        }
        else if (row.State is RemoteSessionStates.Connecting
            or RemoteSessionStates.Connected
            or RemoteSessionStates.Disconnecting)
        {
            session = await this.lifecycle.DisconnectAsync(
                new DisconnectRemoteSessionCommand(
                    ownerUserId,
                    BrowserPackageId,
                    new DisconnectRemoteSessionRequest(
                        row.Id,
                        request.ExpectedRevision,
                        "Browser session terminated.")),
                cancellationToken).ConfigureAwait(false);
        }
        else
        {
            return Failure(
                "browser.session_busy",
                "Browser session is still provisioning; retry termination shortly.");
        }

        await this.CleanupRuntimeAndSecretAsync(
            RuntimeIdFromHost(row.TargetHost),
            await ReadSecretAsync(row.SecretReferenceId, cancellationToken).ConfigureAwait(false),
            ownerUserId,
            correlationId,
            cancellationToken).ConfigureAwait(false);
        return Success(ToBrowserResponse(session));
    }

    private async Task CleanupRuntimeAndSecretAsync(
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
            || !string.Equals(secret.OwningScopeId, BrowserPackageId, StringComparison.Ordinal)
            || !secret.Purpose.StartsWith("remote.browser.", StringComparison.Ordinal))
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

    private CreatePackageRuntimeRequest CreateRuntimeRequest(
        string runtimeId,
        BrowserSessionRuntimePlan plan,
        string password)
    {
        var environment = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["JULOS_START_URL"] = plan.InitialUrl,
        };
        IReadOnlyList<PackageRuntimeVolume> volumes = [];
        if (plan.VolumeName is not null)
        {
            environment["JULOS_PROFILE_DIRECTORY"] = PersistentProfileTarget;
            volumes = [new PackageRuntimeVolume(plan.VolumeName, PersistentProfileTarget, ReadOnly: false)];
        }

        return new CreatePackageRuntimeRequest(
            BrowserPackageId,
            plan.PackageVersion,
            runtimeId,
            this.runtimeOptions.Image!,
            RuntimeLimits,
            environment,
            [plan.RuntimeNetwork])
        {
            Volumes = volumes,
            SecretEnvironment = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["JULOS_VNC_PASSWORD"] = password,
            },
        };
    }

    private static CreateBrowserSessionRequest ValidateCreate(CreateBrowserSessionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.OperationKey)
            || request.OperationKey != request.OperationKey.Trim()
            || request.OperationKey.Length > 128
            || request.OperationKey.Any(char.IsControl))
        {
            throw new JsonException("Browser operation key is invalid.");
        }
        if (!Uri.TryCreate(request.InitialUrl, UriKind.Absolute, out var url)
            || !IsHttpUrl(url)
            || !string.IsNullOrEmpty(url.UserInfo))
        {
            throw new JsonException("Browser URL is invalid.");
        }
        if (request.ProfileMode is not (BrowserSessionProfileModes.Temporary
            or BrowserSessionProfileModes.Persistent
            or BrowserSessionProfileModes.Application))
        {
            throw new JsonException("Browser profile mode is invalid.");
        }
        if (request.ProfileMode == BrowserSessionProfileModes.Temporary && request.ProfileId is not null
            || request.ProfileMode != BrowserSessionProfileModes.Temporary
                && (request.ProfileId is null || request.ProfileId == Guid.Empty))
        {
            throw new JsonException("Browser profile identity is invalid.");
        }

        return request with { InitialUrl = url.AbsoluteUri };
    }

    private static void ValidatePlan(BrowserSessionRuntimePlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (string.IsNullOrWhiteSpace(plan.PackageVersion)
            || string.IsNullOrWhiteSpace(plan.RuntimeNetwork)
            || plan.IdleTimeoutSeconds is < 60 or > MaximumSessionSeconds
            || !Uri.TryCreate(plan.InitialUrl, UriKind.Absolute, out var url)
            || !IsHttpUrl(url))
        {
            throw new JsonException("Browser worker returned an invalid runtime plan.");
        }
        if (plan.ProfileMode == BrowserSessionProfileModes.Temporary && plan.VolumeName is not null
            || plan.ProfileMode != BrowserSessionProfileModes.Temporary
                && string.IsNullOrWhiteSpace(plan.VolumeName))
        {
            throw new JsonException("Browser worker returned invalid profile storage.");
        }
    }

    private static bool IsHttpUrl(Uri url) =>
        string.Equals(url.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
        || string.Equals(url.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);

    private static string RuntimeId(Guid ownerUserId, CreateBrowserSessionRequest request)
    {
        var identity = string.Join(
            '\n',
            ownerUserId.ToString("N"),
            request.OperationKey,
            request.InitialUrl,
            request.ProfileMode,
            request.ProfileId?.ToString("N") ?? string.Empty);
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(identity));
        return "browser-" + Convert.ToHexStringLower(digest)[..32];
    }

    internal static string RuntimeHost(string runtimeId) => "julos-" + runtimeId;

    internal static string RuntimeIdFromHost(string host)
    {
        const string prefix = "julos-browser-";
        if (!host.StartsWith(prefix, StringComparison.Ordinal)
            || host.Length <= "julos-".Length)
        {
            throw new InvalidOperationException("Stored Browser runtime target is invalid.");
        }
        return host["julos-".Length..];
    }

    private static string CreateVncPassword()
    {
        var bytes = RandomNumberGenerator.GetBytes(6);
        try
        {
            return Convert.ToBase64String(bytes);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }

    private static BrowserSessionResponse ToBrowserResponse(RemoteSessionResponse response) => new(
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
        ?? throw new JsonException("Browser session payload is empty.");

    private static CapabilityResponse ServiceFailure(RemoteSessionServiceException exception) =>
        exception.Reason switch
        {
            RemoteSessionServiceFailureReason.NotFound => Failure(
                "browser.session_not_found",
                exception.Message),
            RemoteSessionServiceFailureReason.IdempotencyConflict => Failure(
                "browser.idempotency_conflict",
                exception.Message),
            RemoteSessionServiceFailureReason.ConcurrencyConflict => Failure(
                "browser.concurrency_conflict",
                exception.Message),
            RemoteSessionServiceFailureReason.InvalidTransition => Failure(
                "browser.state_invalid",
                exception.Message),
            _ => Failure("browser.operation_failed", "Browser session orchestration failed."),
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
