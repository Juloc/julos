using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using JulOS.Application.Remote;
using JulOS.Application.Secrets;
using JulOS.Contracts.Remote;
using JulOS.PackageSdk;

namespace JulOS.Infrastructure.Remote;

/// <summary>Provides authenticated durable Remote session orchestration through the capability broker.</summary>
public sealed class RemoteSessionCapabilityProvider : ICapabilityProvider
{
    /// <summary>Core-owned provider identity used by the capability broker.</summary>
    public const string ProviderPackageId = "julos.core.remote";

    private const string CredentialPurpose = "remote.credential";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IRemoteSessionService sessions;
    private readonly IRemoteSessionProvisioner provisioner;
    private readonly IRemoteSessionLifecycleService lifecycle;
    private readonly ISecretReferenceService secrets;

    /// <summary>Creates the Remote session capability provider.</summary>
    public RemoteSessionCapabilityProvider(
        IRemoteSessionService sessions,
        IRemoteSessionProvisioner provisioner,
        IRemoteSessionLifecycleService lifecycle,
        ISecretReferenceService secrets)
    {
        this.sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
        this.provisioner = provisioner ?? throw new ArgumentNullException(nameof(provisioner));
        this.lifecycle = lifecycle ?? throw new ArgumentNullException(nameof(lifecycle));
        this.secrets = secrets ?? throw new ArgumentNullException(nameof(secrets));
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
                RemoteSessionLifecycleCapabilityContract.ResumeOperation => Success(
                    await this.lifecycle.ResumeAsync(
                        new ResumeRemoteSessionCommand(
                            ownerUserId,
                            caller.PackageId,
                            Deserialize<ResumeRemoteSessionRequest>(request.Payload)),
                        cancellationToken).ConfigureAwait(false)),
                RemoteCredentialCapabilityContract.CreateOperation => await this.CreateCredentialAsync(
                    ownerUserId,
                    caller.PackageId,
                    request.CorrelationId,
                    Deserialize<CreateRemoteCredentialRequest>(request.Payload),
                    cancellationToken).ConfigureAwait(false),
                RemoteCredentialCapabilityContract.RotateOperation => await this.RotateCredentialAsync(
                    ownerUserId,
                    caller.PackageId,
                    request.CorrelationId,
                    Deserialize<RotateRemoteCredentialRequest>(request.Payload),
                    cancellationToken).ConfigureAwait(false),
                RemoteCredentialCapabilityContract.DeleteOperation => await this.DeleteCredentialAsync(
                    ownerUserId,
                    caller.PackageId,
                    request.CorrelationId,
                    Deserialize<DeleteRemoteCredentialRequest>(request.Payload),
                    cancellationToken).ConfigureAwait(false),
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
        catch (SecretReferenceFailureException)
        {
            return Failure(RemoteSessionFailureCodes.CredentialUnavailable, "The Remote credential is unavailable.");
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

    private async Task<CapabilityResponse> CreateCredentialAsync(
        Guid ownerUserId,
        string callerPackageId,
        string correlationId,
        CreateRemoteCredentialRequest request,
        CancellationToken cancellationToken)
    {
        var bytes = CredentialBytes(request.SecretValue);
        try
        {
            var created = await this.secrets.CreateAsync(
                new CreateSecretReferenceCommand(
                    ownerUserId,
                    SecretOwningScopeType.Package,
                    callerPackageId,
                    CredentialPurpose,
                    bytes,
                    correlationId,
                    RemoteAddress: null),
                cancellationToken).ConfigureAwait(false);
            return Success(new RemoteCredentialReferenceResponse(created.SecretReferenceId));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }

    private async Task<CapabilityResponse> RotateCredentialAsync(
        Guid ownerUserId,
        string callerPackageId,
        string correlationId,
        RotateRemoteCredentialRequest request,
        CancellationToken cancellationToken)
    {
        var current = await this.ReadOwnedCredentialAsync(
            callerPackageId,
            request.SecretReferenceId,
            cancellationToken).ConfigureAwait(false);
        var bytes = CredentialBytes(request.SecretValue);
        try
        {
            var rotated = await this.secrets.RotateAsync(
                new RotateSecretReferenceCommand(
                    request.SecretReferenceId,
                    ownerUserId,
                    bytes,
                    current.Revision,
                    correlationId,
                    RemoteAddress: null),
                cancellationToken).ConfigureAwait(false);
            return Success(new RemoteCredentialReferenceResponse(rotated.SecretReferenceId));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }

    private async Task<CapabilityResponse> DeleteCredentialAsync(
        Guid ownerUserId,
        string callerPackageId,
        string correlationId,
        DeleteRemoteCredentialRequest request,
        CancellationToken cancellationToken)
    {
        var current = await this.ReadOwnedCredentialAsync(
            callerPackageId,
            request.SecretReferenceId,
            cancellationToken).ConfigureAwait(false);
        var deleted = await this.secrets.DeleteAsync(
            new DeleteSecretReferenceCommand(
                request.SecretReferenceId,
                ownerUserId,
                current.Revision,
                correlationId,
                RemoteAddress: null),
            cancellationToken).ConfigureAwait(false);
        return Success(new RemoteCredentialReferenceResponse(deleted.SecretReferenceId));
    }

    private async Task<SecretReferenceSnapshot> ReadOwnedCredentialAsync(
        string callerPackageId,
        Guid secretReferenceId,
        CancellationToken cancellationToken)
    {
        var current = await this.secrets.ReadAsync(secretReferenceId, cancellationToken).ConfigureAwait(false);
        if (current.OwningScopeType != SecretOwningScopeType.Package
            || !string.Equals(current.OwningScopeId, callerPackageId, StringComparison.Ordinal)
            || !string.Equals(current.Purpose, CredentialPurpose, StringComparison.Ordinal)
            || !current.IsPresent)
        {
            throw new SecretReferenceFailureException(SecretReferenceFailureReason.NotFound);
        }
        return current;
    }

    private static byte[] CredentialBytes(string secretValue)
    {
        if (string.IsNullOrWhiteSpace(secretValue))
        {
            throw new SecretReferenceFailureException(SecretReferenceFailureReason.Invalid);
        }
        return Encoding.UTF8.GetBytes(secretValue);
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
