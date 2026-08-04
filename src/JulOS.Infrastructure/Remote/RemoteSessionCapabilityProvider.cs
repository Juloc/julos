using System.Text.Json;

using JulOS.Application.Remote;
using JulOS.Contracts.Remote;
using JulOS.PackageSdk;

namespace JulOS.Infrastructure.Remote;

/// <summary>Provides authenticated durable Remote session orchestration through the capability broker.</summary>
public sealed class RemoteSessionCapabilityProvider : ICapabilityProvider
{
    /// <summary>Core-owned provider identity used by the capability broker.</summary>
    public const string ProviderPackageId = "julos.core.remote";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IRemoteSessionService sessions;
    private readonly IRemoteSessionProvisioner provisioner;
    private readonly IRemoteSessionLifecycleService lifecycle;

    /// <summary>Creates the Remote session capability provider.</summary>
    public RemoteSessionCapabilityProvider(
        IRemoteSessionService sessions,
        IRemoteSessionProvisioner provisioner,
        IRemoteSessionLifecycleService lifecycle)
    {
        this.sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
        this.provisioner = provisioner ?? throw new ArgumentNullException(nameof(provisioner));
        this.lifecycle = lifecycle ?? throw new ArgumentNullException(nameof(lifecycle));
    }

    /// <inheritdoc />
    public CapabilityProviderDescriptor Descriptor { get; } = new(
        ProviderPackageId,
        RemoteSessionCapabilityContract.Name,
        RemoteSessionCapabilityContract.Version,
        Priority: 1000,
        Healthy: true);

    /// <inheritdoc />
    public async Task<CapabilityResponse> InvokeAsync(
        CapabilityRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!string.Equals(request.CapabilityName, RemoteSessionCapabilityContract.Name, StringComparison.Ordinal)
            || !string.Equals(request.ContractVersion, RemoteSessionCapabilityContract.Version, StringComparison.Ordinal))
        {
            return Failure(
                "remote.contract_incompatible",
                "The requested Remote session capability contract is incompatible.");
        }
        var caller = request.Caller;
        if (caller?.UserId is not Guid ownerUserId || ownerUserId == Guid.Empty)
        {
            return Failure("remote.caller_invalid", "An authenticated Remote session caller is required.");
        }

        try
        {
            return request.Operation switch
            {
                RemoteSessionCapabilityContract.CreateOperation => await this.CreateAsync(
                    ownerUserId,
                    caller.PackageId,
                    Deserialize<CreateRemoteSessionRequest>(request.Payload),
                    cancellationToken).ConfigureAwait(false),
                RemoteSessionCapabilityContract.ReadOperation => Success(await this.sessions.ReadAsync(
                    new ReadRemoteSessionCommand(
                        ownerUserId,
                        caller.PackageId,
                        Deserialize<ReadRemoteSessionRequest>(request.Payload)),
                    cancellationToken).ConfigureAwait(false)),
                RemoteSessionCapabilityContract.ListOperation => Success(await this.sessions.ListAsync(
                    new ListRemoteSessionsCommand(
                        ownerUserId,
                        caller.PackageId,
                        Deserialize<ListRemoteSessionsRequest>(request.Payload)),
                    cancellationToken).ConfigureAwait(false)),
                RemoteSessionCapabilityContract.CancelOperation => Success(await this.sessions.CancelAsync(
                    new CancelRemoteSessionCommand(
                        ownerUserId,
                        caller.PackageId,
                        Deserialize<CancelRemoteSessionRequest>(request.Payload)),
                    cancellationToken).ConfigureAwait(false)),
                RemoteSessionLifecycleCapabilityContract.DisconnectOperation => Success(
                    await this.lifecycle.DisconnectAsync(
                        new DisconnectRemoteSessionCommand(
                            ownerUserId,
                            caller.PackageId,
                            Deserialize<DisconnectRemoteSessionRequest>(request.Payload)),
                        cancellationToken).ConfigureAwait(false)),
                RemoteSessionLifecycleCapabilityContract.DetachOperation => Success(
                    await this.lifecycle.DetachAsync(
                        new DetachRemoteSessionCommand(
                            ownerUserId,
                            caller.PackageId,
                            Deserialize<DetachRemoteSessionRequest>(request.Payload)),
                        cancellationToken).ConfigureAwait(false)),
                _ => Failure(
                    "remote.operation_unsupported",
                    "The requested Remote session operation is not supported."),
            };
        }
        catch (RemoteSessionContractException exception)
        {
            return Failure(exception.Code, exception.Message);
        }
        catch (RemoteSessionServiceException exception)
        {
            return ServiceFailure(exception);
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException)
        {
            return Failure("remote.request_invalid", "The Remote session request payload is invalid.");
        }
    }

    private async Task<CapabilityResponse> CreateAsync(
        Guid ownerUserId,
        string callerPackageId,
        CreateRemoteSessionRequest request,
        CancellationToken cancellationToken)
    {
        var created = await this.sessions.CreateAsync(
            new CreateRemoteSessionCommand(ownerUserId, callerPackageId, request),
            cancellationToken).ConfigureAwait(false);
        var provisioned = await this.provisioner.ProvisionAsync(
            new ProvisionRemoteSessionCommand(
                ownerUserId,
                callerPackageId,
                created.SessionId,
                created.Revision),
            cancellationToken).ConfigureAwait(false);
        return provisioned.Failure is null
            ? Success(provisioned)
            : Failure(provisioned.Failure.Code, provisioned.Failure.Detail);
    }

    private static T Deserialize<T>(JsonElement payload)
    {
        return payload.Deserialize<T>(JsonOptions)
            ?? throw new JsonException("Remote session payload is empty.");
    }

    private static CapabilityResponse ServiceFailure(RemoteSessionServiceException exception)
    {
        return exception.Reason switch
        {
            RemoteSessionServiceFailureReason.InvalidCaller => Failure(
                "remote.caller_invalid",
                exception.Message),
            RemoteSessionServiceFailureReason.NotFound => Failure(
                "remote.session_not_found",
                exception.Message),
            RemoteSessionServiceFailureReason.IdempotencyConflict => Failure(
                "remote.idempotency_conflict",
                exception.Message),
            RemoteSessionServiceFailureReason.ConcurrencyConflict => Failure(
                "remote.concurrency_conflict",
                exception.Message),
            RemoteSessionServiceFailureReason.InvalidTransition => Failure(
                RemoteSessionFailureCodes.StateTransitionInvalid,
                exception.Message),
            RemoteSessionServiceFailureReason.CursorInvalid => Failure(
                "remote.cursor_invalid",
                exception.Message),
            _ => Failure("remote.operation_failed", "Remote session orchestration failed."),
        };
    }

    private static CapabilityResponse Success<T>(T response) => new(
        Succeeded: true,
        ErrorCode: null,
        ErrorDetail: null,
        JsonSerializer.SerializeToElement(response, JsonOptions));

    private static CapabilityResponse Failure(string code, string detail) => new(
        Succeeded: false,
        code,
        detail,
        JsonSerializer.SerializeToElement(new { }, JsonOptions));
}
