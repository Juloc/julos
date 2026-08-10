using System.Text.Json;

using JulOS.Application.Packages;
using JulOS.PackageSdk;

namespace JulOS.Infrastructure.Packages;

/// <summary>
/// Generic provider that lets a package manage its own interactive-session
/// profiles and network profiles. Core forwards the opaque package payload to the
/// caller's own worker together with the trusted owning user and returns the
/// worker's caller-safe response; it never interprets the profile shape and holds
/// no profile state of its own.
/// </summary>
internal sealed class InteractiveProfilesCapabilityProvider : ICapabilityProvider
{
    /// <summary>Core-owned provider identity used by the capability broker.</summary>
    internal const string ProviderPackageId = "julos.core.interactive-profiles";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IPackageWorkerCommandDispatcher workers;

    internal InteractiveProfilesCapabilityProvider(IPackageWorkerCommandDispatcher workers)
    {
        this.workers = workers ?? throw new ArgumentNullException(nameof(workers));
    }

    /// <inheritdoc />
    public CapabilityProviderDescriptor Descriptor { get; } = new(
        ProviderPackageId,
        InteractiveProfilesCapabilityContract.Name,
        InteractiveProfilesCapabilityContract.Version,
        Priority: 1000,
        Healthy: true);

    /// <inheritdoc />
    public async Task<CapabilityResponse> InvokeAsync(
        CapabilityRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!string.Equals(request.CapabilityName, InteractiveProfilesCapabilityContract.Name, StringComparison.Ordinal)
            || !string.Equals(request.ContractVersion, InteractiveProfilesCapabilityContract.Version, StringComparison.Ordinal))
        {
            return Failure(
                "interactive.profiles.contract_incompatible",
                "The requested interactive-profiles capability contract is incompatible.");
        }
        if (request.Caller?.UserId is not Guid ownerUserId
            || ownerUserId == Guid.Empty
            || string.IsNullOrWhiteSpace(request.Caller.PackageId))
        {
            return Failure(
                "interactive.profiles.caller_invalid",
                "An authenticated package caller is required.");
        }

        var commandName = request.Operation switch
        {
            InteractiveProfilesCapabilityContract.CreateNetworkOperation =>
                InteractiveProfilesWorkerCommands.CreateNetworkProfile,
            InteractiveProfilesCapabilityContract.ListNetworksOperation =>
                InteractiveProfilesWorkerCommands.ListNetworkProfiles,
            InteractiveProfilesCapabilityContract.CreateOperation =>
                InteractiveProfilesWorkerCommands.CreateProfile,
            InteractiveProfilesCapabilityContract.ListOperation =>
                InteractiveProfilesWorkerCommands.ListProfiles,
            InteractiveProfilesCapabilityContract.DeleteOperation =>
                InteractiveProfilesWorkerCommands.DeleteProfile,
            _ => null,
        };
        if (commandName is null)
        {
            return Failure(
                "interactive.profiles.operation_unsupported",
                "The requested interactive-profiles operation is not supported.");
        }

        var envelope = JsonSerializer.SerializeToElement(
            new ManageInteractiveProfilesRequest(ownerUserId, request.Payload),
            JsonOptions);

        PackageWorkerCommandResult result;
        try
        {
            result = await this.workers.InvokeAsync(
                request.Caller.PackageId,
                new PackageWorkerCommand(commandName, envelope),
                cancellationToken).ConfigureAwait(false);
        }
        catch (PackageManagementException)
        {
            return Failure("interactive.profiles.worker_unavailable", "Package worker is unavailable.");
        }

        return result.Succeeded
            ? new CapabilityResponse(true, null, null, result.Payload)
            : Failure(
                result.ErrorCode ?? "interactive.profiles.failed",
                result.ErrorDetail ?? "The interactive-profiles operation failed.");
    }

    private static CapabilityResponse Failure(string code, string detail) => new(
        false,
        code,
        detail,
        JsonSerializer.SerializeToElement(new { }, JsonOptions));
}
