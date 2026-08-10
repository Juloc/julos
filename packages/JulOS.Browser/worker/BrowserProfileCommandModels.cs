namespace JulOS.Browser.Worker;

// Opaque Browser-defined payloads carried inside the generic interactive-profiles
// management envelope. They never enter Core, which only forwards the raw JSON.

/// <summary>Create-network-profile request payload.</summary>
internal sealed record BrowserCreateNetworkProfileRequest(
    string? Key,
    string? RuntimeNetwork,
    Guid? ProxySecretReferenceId);

/// <summary>Caller-safe network-profile view. The proxy secret value is never returned.</summary>
internal sealed record BrowserNetworkProfileResponse(
    string Key,
    string RuntimeNetwork,
    bool HasProxy,
    int Revision);

/// <summary>List-network-profiles response.</summary>
internal sealed record BrowserNetworkProfileListResponse(
    IReadOnlyList<BrowserNetworkProfileResponse> NetworkProfiles);

/// <summary>Create-profile request payload. Mode is "persistent" or "application"; temporary profiles are not persisted.</summary>
internal sealed record BrowserCreateProfileRequest(
    string? DisplayName,
    string? Mode,
    string? NetworkProfileKey,
    string? StartUrl,
    string? ApplicationKey);

/// <summary>Caller-safe profile view.</summary>
internal sealed record BrowserProfileResponse(
    Guid ProfileId,
    string DisplayName,
    string Mode,
    string NetworkProfileKey,
    string? StartUrl,
    string? ApplicationKey,
    int Revision);

/// <summary>List-profiles response.</summary>
internal sealed record BrowserProfileListResponse(
    IReadOnlyList<BrowserProfileResponse> Profiles);

/// <summary>Delete-profile request payload.</summary>
internal sealed record BrowserDeleteProfileRequest(
    Guid ProfileId,
    int Revision);

/// <summary>Delete-profile response.</summary>
internal sealed record BrowserDeleteProfileResponse(bool Deleted);
