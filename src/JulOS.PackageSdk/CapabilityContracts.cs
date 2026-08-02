using System.Text.Json;

namespace JulOS.PackageSdk;

/// <summary>One bounded invocation of a package capability.</summary>
/// <param name="CapabilityName">Capability identity.</param>
/// <param name="ContractVersion">Requested contract version.</param>
/// <param name="Operation">Capability operation.</param>
/// <param name="CorrelationId">Cross-service correlation identity.</param>
/// <param name="Payload">Versioned operation payload.</param>
/// <param name="DeadlineUtc">Absolute invocation deadline.</param>
public sealed record CapabilityRequest(
    string CapabilityName,
    string ContractVersion,
    string Operation,
    string CorrelationId,
    JsonElement Payload,
    DateTimeOffset DeadlineUtc);

/// <summary>Result of one capability invocation.</summary>
/// <param name="Succeeded">Whether the provider completed the operation.</param>
/// <param name="ErrorCode">Stable failure code when unsuccessful.</param>
/// <param name="ErrorDetail">Caller-safe failure detail.</param>
/// <param name="Payload">Versioned response payload.</param>
public sealed record CapabilityResponse(
    bool Succeeded,
    string? ErrorCode,
    string? ErrorDetail,
    JsonElement Payload);

/// <summary>Advertises one healthy or unhealthy capability provider.</summary>
/// <param name="ProviderPackageId">Owning package identity.</param>
/// <param name="CapabilityName">Provided capability.</param>
/// <param name="ContractVersion">Provided contract version.</param>
/// <param name="Priority">Deterministic provider priority.</param>
/// <param name="Healthy">Whether new calls may be routed to the provider.</param>
public sealed record CapabilityProviderDescriptor(
    string ProviderPackageId,
    string CapabilityName,
    string ContractVersion,
    int Priority,
    bool Healthy);

/// <summary>Client boundary for invoking a capability without learning provider transport details.</summary>
public interface ICapabilityClient
{
    /// <summary>Invokes the selected healthy provider within the request deadline.</summary>
    Task<CapabilityResponse> InvokeAsync(
        CapabilityRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>Provider boundary implemented by a package worker.</summary>
public interface ICapabilityProvider
{
    /// <summary>Gets the provider registration.</summary>
    CapabilityProviderDescriptor Descriptor { get; }

    /// <summary>Handles one authorized versioned capability request.</summary>
    Task<CapabilityResponse> InvokeAsync(
        CapabilityRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>Raised when no healthy compatible provider exists.</summary>
public sealed class CapabilityUnavailableException : Exception
{
    /// <summary>Creates an unavailable-provider failure.</summary>
    public CapabilityUnavailableException(string capabilityName, string contractVersion)
        : base($"No healthy provider is available for capability '{capabilityName}' version '{contractVersion}'.")
    {
        this.CapabilityName = capabilityName;
        this.ContractVersion = contractVersion;
    }

    /// <summary>Gets the unavailable capability identity.</summary>
    public string CapabilityName { get; }

    /// <summary>Gets the requested contract version.</summary>
    public string ContractVersion { get; }
}
