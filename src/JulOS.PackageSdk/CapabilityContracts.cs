using System.Text.Json;

namespace JulOS.PackageSdk;

public sealed record CapabilityRequest(
    string CapabilityName,
    string ContractVersion,
    string Operation,
    string CorrelationId,
    JsonElement Payload,
    DateTimeOffset DeadlineUtc);

public sealed record CapabilityResponse(
    bool Succeeded,
    string? ErrorCode,
    string? ErrorDetail,
    JsonElement Payload);

public sealed record CapabilityProviderDescriptor(
    string ProviderPackageId,
    string CapabilityName,
    string ContractVersion,
    int Priority,
    bool Healthy);

public interface ICapabilityClient
{
    Task<CapabilityResponse> InvokeAsync(
        CapabilityRequest request,
        CancellationToken cancellationToken = default);
}

public interface ICapabilityProvider
{
    CapabilityProviderDescriptor Descriptor { get; }

    Task<CapabilityResponse> InvokeAsync(
        CapabilityRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class CapabilityUnavailableException : Exception
{
    public CapabilityUnavailableException(string capabilityName, string contractVersion)
        : base($"No healthy provider is available for capability '{capabilityName}' version '{contractVersion}'.")
    {
        this.CapabilityName = capabilityName;
        this.ContractVersion = contractVersion;
    }

    public string CapabilityName { get; }

    public string ContractVersion { get; }
}
