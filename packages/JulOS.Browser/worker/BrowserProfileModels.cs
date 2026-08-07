using System.Security.Cryptography;
using System.Text;

namespace JulOS.Browser.Worker;

/// <summary>Supported isolated Chromium profile modes.</summary>
public enum BrowserProfileMode
{
    /// <summary>Named user profile retained between sessions.</summary>
    Persistent = 1,

    /// <summary>One-session profile with no persistent volume.</summary>
    Temporary = 2,

    /// <summary>User-owned fixed-application profile retained between sessions.</summary>
    Application = 3,
}

/// <summary>One configured Browser runtime network profile.</summary>
/// <param name="Key">Stable package-local profile key.</param>
/// <param name="RuntimeNetwork">Exact Runtime Manager network name.</param>
/// <param name="ProxySecretReferenceId">Optional opaque package-owned proxy credential reference.</param>
/// <param name="Revision">Optimistic concurrency revision.</param>
public sealed record BrowserNetworkProfile(
    string Key,
    string RuntimeNetwork,
    Guid? ProxySecretReferenceId,
    int Revision);

/// <summary>One user-owned Browser profile definition.</summary>
/// <param name="ProfileId">Stable profile identity.</param>
/// <param name="OwnerUserId">Authenticated JulOS user that owns the profile.</param>
/// <param name="DisplayName">User-visible profile name.</param>
/// <param name="Mode">Browser profile mode.</param>
/// <param name="NetworkProfileKey">Configured network-profile key.</param>
/// <param name="StartUrl">Optional start URL; required in application mode.</param>
/// <param name="ApplicationKey">Optional fixed-application identity; required in application mode.</param>
/// <param name="CreatedAtUtc">Creation time.</param>
/// <param name="UpdatedAtUtc">Last metadata update time.</param>
/// <param name="Revision">Optimistic concurrency revision.</param>
public sealed record BrowserProfile(
    Guid ProfileId,
    Guid OwnerUserId,
    string DisplayName,
    BrowserProfileMode Mode,
    string NetworkProfileKey,
    Uri? StartUrl,
    string? ApplicationKey,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    int Revision);

/// <summary>Runtime storage decision derived from one Browser profile.</summary>
/// <param name="VolumeName">Package-owned persistent volume, or null for temporary mode.</param>
/// <param name="DeleteOnTermination">Whether runtime-local profile data must be destroyed on termination.</param>
public sealed record BrowserRuntimeStorage(string? VolumeName, bool DeleteOnTermination);

/// <summary>Validates Browser profile isolation and configured runtime-network policy.</summary>
public sealed class BrowserProfilePolicy
{
    private const int MaximumKeyLength = 64;
    private const int MaximumDisplayNameLength = 96;
    private readonly HashSet<string> allowedNetworks;

    /// <summary>Creates policy from the administrator-configured Runtime Manager network allowlist.</summary>
    public BrowserProfilePolicy(IEnumerable<string> allowedNetworks, string? defaultNetwork)
    {
        ArgumentNullException.ThrowIfNull(allowedNetworks);
        this.allowedNetworks = new HashSet<string>(StringComparer.Ordinal);
        foreach (var network in allowedNetworks)
        {
            ValidateToken(network, "Runtime network", MaximumKeyLength);
            this.allowedNetworks.Add(network);
        }

        if (defaultNetwork is not null)
        {
            ValidateToken(defaultNetwork, "Default runtime network", MaximumKeyLength);
            if (!this.allowedNetworks.Contains(defaultNetwork))
            {
                throw new ArgumentException("The default Browser network must be present in allowedNetworks.", nameof(defaultNetwork));
            }
        }

        this.DefaultNetwork = defaultNetwork;
    }

    /// <summary>Configured default runtime network, when one exists.</summary>
    public string? DefaultNetwork { get; }

    /// <summary>Number of exact Runtime Manager networks allowed for Browser sessions.</summary>
    public int AllowedNetworkCount => this.allowedNetworks.Count;

    /// <summary>Parses the flat package configuration without introducing a second settings model.</summary>
    public static BrowserProfilePolicy FromConfiguration(IReadOnlyDictionary<string, string> configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var networks = configuration.TryGetValue("allowedNetworks", out var configured)
            ? configured.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            : [];
        var defaultNetwork = configuration.TryGetValue("defaultNetwork", out var selected)
            && !string.IsNullOrWhiteSpace(selected)
            ? selected.Trim()
            : null;
        return new BrowserProfilePolicy(networks, defaultNetwork);
    }

    /// <summary>Validates one network profile against the administrator allowlist.</summary>
    public BrowserNetworkProfile CreateNetworkProfile(
        string key,
        string runtimeNetwork,
        Guid? proxySecretReferenceId = null)
    {
        ValidateToken(key, "Network profile key", MaximumKeyLength);
        ValidateToken(runtimeNetwork, "Runtime network", MaximumKeyLength);
        if (!this.allowedNetworks.Contains(runtimeNetwork))
        {
            throw new InvalidOperationException("The requested Browser runtime network is not allowlisted.");
        }
        if (proxySecretReferenceId == Guid.Empty)
        {
            throw new ArgumentException("Proxy secret reference cannot be empty.", nameof(proxySecretReferenceId));
        }

        return new BrowserNetworkProfile(key, runtimeNetwork, proxySecretReferenceId, 1);
    }

    /// <summary>Creates validated profile metadata. Temporary profiles are intentionally not persisted by the store.</summary>
    public static BrowserProfile CreateProfile(
        Guid ownerUserId,
        string displayName,
        BrowserProfileMode mode,
        string networkProfileKey,
        Uri? startUrl,
        string? applicationKey,
        DateTimeOffset now)
    {
        if (ownerUserId == Guid.Empty)
        {
            throw new ArgumentException("A Browser profile owner is required.", nameof(ownerUserId));
        }
        ValidateToken(displayName, "Browser profile name", MaximumDisplayNameLength);
        ValidateToken(networkProfileKey, "Network profile key", MaximumKeyLength);
        ValidateMode(mode, startUrl, applicationKey);

        return new BrowserProfile(
            Guid.NewGuid(),
            ownerUserId,
            displayName,
            mode,
            networkProfileKey,
            startUrl,
            applicationKey,
            now,
            now,
            1);
    }

    /// <summary>Returns package-owned runtime storage without ever persisting temporary profile data.</summary>
    public static BrowserRuntimeStorage RuntimeStorage(BrowserProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (profile.Mode == BrowserProfileMode.Temporary)
        {
            return new BrowserRuntimeStorage(null, DeleteOnTermination: true);
        }

        var identity = $"{profile.OwnerUserId:N}:{profile.ProfileId:N}";
        var suffix = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(identity)))[..32];
        return new BrowserRuntimeStorage($"julos-browser-profile-{suffix}", DeleteOnTermination: false);
    }

    /// <summary>Rejects access to a profile owned by another authenticated user.</summary>
    public static void EnsureOwner(BrowserProfile profile, Guid userId)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (userId == Guid.Empty || profile.OwnerUserId != userId)
        {
            throw new UnauthorizedAccessException("Browser profile is not owned by the current user.");
        }
    }

    private static void ValidateMode(BrowserProfileMode mode, Uri? startUrl, string? applicationKey)
    {
        if (!Enum.IsDefined(mode))
        {
            throw new ArgumentOutOfRangeException(nameof(mode));
        }

        if (startUrl is not null
            && (!startUrl.IsAbsoluteUri || (startUrl.Scheme != Uri.UriSchemeHttp && startUrl.Scheme != Uri.UriSchemeHttps)))
        {
            throw new ArgumentException("Browser start URL must be an absolute HTTP or HTTPS URL.", nameof(startUrl));
        }

        if (mode == BrowserProfileMode.Application)
        {
            if (startUrl is null)
            {
                throw new ArgumentException("Application mode requires a fixed start URL.", nameof(startUrl));
            }
            ValidateToken(applicationKey, "Application key", MaximumKeyLength);
        }
        else if (applicationKey is not null)
        {
            throw new ArgumentException("Application key is valid only for application mode.", nameof(applicationKey));
        }
    }

    private static void ValidateToken(string? value, string label, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value != value.Trim()
            || value.Length > maximumLength
            || value.Any(char.IsControl))
        {
            throw new ArgumentException($"{label} is invalid.");
        }
    }
}
