using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace JulOS.Browser.Worker;

/// <summary>Supported Browser profile modes.</summary>
public static class BrowserProfileModes
{
    public const string Temporary = "temporary";
    public const string Persistent = "persistent";
    public const string Application = "application";

    public static bool IsSupported(string value) => value is Temporary or Persistent or Application;
}

/// <summary>One user-owned Browser profile.</summary>
public sealed record BrowserProfileDefinition(
    string ProfileId,
    Guid OwnerUserId,
    string Mode,
    string? ApplicationId);

/// <summary>One configured Browser network policy.</summary>
public sealed record BrowserNetworkProfile(
    string ProfileId,
    string NetworkName,
    IReadOnlyList<string> AllowedSchemes,
    IReadOnlyList<string> AllowedHostPatterns,
    Guid? ProxySecretReferenceId);

/// <summary>Input used to authorize one Browser session.</summary>
public sealed record BrowserSessionPolicyRequest(
    Guid AuthenticatedUserId,
    BrowserProfileDefinition Profile,
    BrowserNetworkProfile NetworkProfile,
    string StartUrl,
    IReadOnlySet<string> ConfiguredNetworks);

/// <summary>Validated Runtime Manager inputs for one Browser session.</summary>
public sealed record BrowserRuntimePolicy(
    string NetworkName,
    string StartUrl,
    string? ProfileVolumeName,
    string? ProfileVolumeTarget,
    bool DeleteTemporaryData,
    Guid? ProxySecretReferenceId);

/// <summary>Validates Browser ownership, profile and network settings without storing state.</summary>
public sealed partial class BrowserProfilePolicy
{
    private const string ProfileVolumeTarget = "/home/julos/.config/chromium";

    /// <summary>Validates one session request and produces bounded Runtime Manager inputs.</summary>
    public BrowserRuntimePolicy Evaluate(BrowserSessionPolicyRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Profile);
        ArgumentNullException.ThrowIfNull(request.NetworkProfile);
        ArgumentNullException.ThrowIfNull(request.ConfiguredNetworks);

        if (request.AuthenticatedUserId == Guid.Empty
            || request.Profile.OwnerUserId == Guid.Empty
            || request.AuthenticatedUserId != request.Profile.OwnerUserId)
        {
            throw new ArgumentException("The Browser profile is not owned by the authenticated user.", nameof(request));
        }

        ValidateStableId(request.Profile.ProfileId, "profile", nameof(request));
        if (!BrowserProfileModes.IsSupported(request.Profile.Mode))
        {
            throw new ArgumentException("The Browser profile mode is unsupported.", nameof(request));
        }

        if (string.Equals(request.Profile.Mode, BrowserProfileModes.Application, StringComparison.Ordinal))
        {
            ValidateStableId(request.Profile.ApplicationId, "application", nameof(request));
        }
        else if (request.Profile.ApplicationId is not null)
        {
            throw new ArgumentException(
                "An application identity is allowed only for application profiles.",
                nameof(request));
        }

        ValidateStableId(request.NetworkProfile.ProfileId, "network profile", nameof(request));
        ValidateNetworkName(request.NetworkProfile.NetworkName, request.ConfiguredNetworks, nameof(request));
        ValidateProxyReference(request.NetworkProfile.ProxySecretReferenceId, nameof(request));

        var normalizedStartUrl = ValidateStartUrl(
            request.StartUrl,
            request.NetworkProfile.AllowedSchemes,
            request.NetworkProfile.AllowedHostPatterns,
            nameof(request));

        if (string.Equals(request.Profile.Mode, BrowserProfileModes.Temporary, StringComparison.Ordinal))
        {
            return new BrowserRuntimePolicy(
                request.NetworkProfile.NetworkName,
                normalizedStartUrl,
                ProfileVolumeName: null,
                ProfileVolumeTarget: null,
                DeleteTemporaryData: true,
                request.NetworkProfile.ProxySecretReferenceId);
        }

        var scope = string.Equals(
            request.Profile.Mode,
            BrowserProfileModes.Application,
            StringComparison.Ordinal)
            ? string.Concat("application:", request.Profile.ApplicationId)
            : string.Concat("profile:", request.Profile.ProfileId);

        return new BrowserRuntimePolicy(
            request.NetworkProfile.NetworkName,
            normalizedStartUrl,
            CreateProfileVolumeName(request.AuthenticatedUserId, scope),
            ProfileVolumeTarget,
            DeleteTemporaryData: false,
            request.NetworkProfile.ProxySecretReferenceId);
    }

    private static string ValidateStartUrl(
        string startUrl,
        IReadOnlyList<string> allowedSchemes,
        IReadOnlyList<string> allowedHostPatterns,
        string parameterName)
    {
        ArgumentNullException.ThrowIfNull(allowedSchemes);
        ArgumentNullException.ThrowIfNull(allowedHostPatterns);

        if (allowedSchemes.Count is < 1 or > 2
            || allowedSchemes.Any(scheme => scheme is not ("http" or "https"))
            || allowedSchemes.Distinct(StringComparer.Ordinal).Count() != allowedSchemes.Count)
        {
            throw new ArgumentException("The Browser scheme policy is invalid.", parameterName);
        }

        foreach (var pattern in allowedHostPatterns)
        {
            ValidateHostPattern(pattern, parameterName);
        }

        if (string.Equals(startUrl, "about:blank", StringComparison.Ordinal))
        {
            return startUrl;
        }

        if (!Uri.TryCreate(startUrl, UriKind.Absolute, out var uri)
            || !allowedSchemes.Contains(uri.Scheme, StringComparer.Ordinal)
            || !string.IsNullOrEmpty(uri.UserInfo)
            || uri.IsDefaultPort is false
            || string.IsNullOrWhiteSpace(uri.IdnHost))
        {
            throw new ArgumentException("The Browser start URL is not allowed.", parameterName);
        }

        var host = uri.IdnHost.ToLowerInvariant();
        if (allowedHostPatterns.Count > 0
            && !allowedHostPatterns.Any(pattern => MatchesHost(host, pattern)))
        {
            throw new ArgumentException("The Browser start host is not allowed.", parameterName);
        }

        return uri.AbsoluteUri;
    }

    private static bool MatchesHost(string host, string pattern)
    {
        if (pattern.StartsWith("*.", StringComparison.Ordinal))
        {
            var suffix = pattern[1..];
            return host.EndsWith(suffix, StringComparison.Ordinal)
                && host.Length > suffix.Length;
        }

        return string.Equals(host, pattern, StringComparison.Ordinal);
    }

    private static void ValidateHostPattern(string pattern, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(pattern)
            || pattern != pattern.Trim()
            || pattern != pattern.ToLowerInvariant()
            || pattern.Length > 253
            || pattern.Any(char.IsControl))
        {
            throw new ArgumentException("A Browser host pattern is invalid.", parameterName);
        }

        var host = pattern.StartsWith("*.", StringComparison.Ordinal) ? pattern[2..] : pattern;
        if (host.Length == 0
            || !Uri.TryCreate(string.Concat("https://", host), UriKind.Absolute, out var uri)
            || !string.Equals(uri.IdnHost, host, StringComparison.Ordinal)
            || !string.IsNullOrEmpty(uri.PathAndQuery.Trim('/')))
        {
            throw new ArgumentException("A Browser host pattern is invalid.", parameterName);
        }
    }

    private static void ValidateNetworkName(
        string networkName,
        IReadOnlySet<string> configuredNetworks,
        string parameterName)
    {
        if (string.IsNullOrWhiteSpace(networkName)
            || networkName != networkName.Trim()
            || networkName.Length > 128
            || networkName.Any(char.IsControl)
            || networkName is "host" or "none"
            || !configuredNetworks.Contains(networkName))
        {
            throw new ArgumentException("The Browser network is not configured.", parameterName);
        }
    }

    private static void ValidateProxyReference(Guid? referenceId, string parameterName)
    {
        if (referenceId == Guid.Empty)
        {
            throw new ArgumentException("The proxy secret reference is invalid.", parameterName);
        }
    }

    private static void ValidateStableId(string? value, string name, string parameterName)
    {
        if (value is null || !StableId().IsMatch(value))
        {
            throw new ArgumentException($"The Browser {name} identity is invalid.", parameterName);
        }
    }

    private static string CreateProfileVolumeName(Guid ownerUserId, string scope)
    {
        var input = Encoding.UTF8.GetBytes(string.Concat(ownerUserId.ToString("N"), ":", scope));
        var digest = SHA256.HashData(input);

        try
        {
            return string.Concat(
                "julos-browser-profile-",
                Convert.ToHexString(digest.AsSpan(0, 16)).ToLowerInvariant());
        }
        finally
        {
            CryptographicOperations.ZeroMemory(input);
            CryptographicOperations.ZeroMemory(digest);
        }
    }

    [GeneratedRegex("^[a-z0-9](?:[a-z0-9.-]{0,62}[a-z0-9])?$")]
    private static partial Regex StableId();
}
