using System.Net;
using System.Text.RegularExpressions;

using JulOS.Application.Remote;
using JulOS.Contracts.Remote;
using JulOS.Contracts.Runtime;
using JulOS.Domain;
using JulOS.Domain.Packages;

using Microsoft.Extensions.Configuration;

namespace JulOS.Infrastructure.Remote;

/// <summary>Strict configuration-backed Remote provider, egress and target policy.</summary>
public sealed partial class ConfiguredRemoteRuntimePolicy : IRemoteRuntimePolicy
{
    private readonly Dictionary<string, RemoteProviderRuntimeDefinition> providers;
    private readonly Dictionary<Guid, RemoteNetworkProfileDefinition> profiles;
    private readonly RemoteNetworkProfileDefinition? defaultProfile;

    /// <summary>Creates a validated immutable policy from explicit definitions.</summary>
    public ConfiguredRemoteRuntimePolicy(
        IEnumerable<RemoteProviderRuntimeDefinition> providers,
        IEnumerable<RemoteNetworkProfileDefinition> profiles)
    {
        ArgumentNullException.ThrowIfNull(providers);
        ArgumentNullException.ThrowIfNull(profiles);

        var providerMap = new Dictionary<string, RemoteProviderRuntimeDefinition>(StringComparer.Ordinal);
        foreach (var provider in providers)
        {
            ValidateProvider(provider);
            if (!providerMap.TryAdd(provider.Protocol, provider))
            {
                throw new InvalidOperationException(
                    $"Remote protocol '{provider.Protocol}' has more than one configured provider.");
            }
        }

        var profileMap = new Dictionary<Guid, RemoteNetworkProfileDefinition>();
        RemoteNetworkProfileDefinition? selectedDefault = null;
        foreach (var profile in profiles)
        {
            ValidateProfile(profile);
            if (!profileMap.TryAdd(profile.NetworkProfileId, profile))
            {
                throw new InvalidOperationException(
                    $"Remote network profile '{profile.NetworkProfileId:D}' is configured more than once.");
            }
            if (profile.Default)
            {
                if (selectedDefault is not null)
                {
                    throw new InvalidOperationException("Only one Remote network profile can be the default.");
                }
                selectedDefault = profile;
            }
        }

        this.providers = providerMap;
        this.profiles = profileMap;
        this.defaultProfile = selectedDefault;
    }

    /// <summary>Reads provider and network-profile definitions from configuration.</summary>
    public static ConfiguredRemoteRuntimePolicy Read(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var providerConfigurations = configuration.GetSection("Remote:Providers")
            .Get<RemoteProviderConfiguration[]>()
            ?? [];
        var profileConfigurations = configuration.GetSection("Remote:NetworkProfiles")
            .Get<RemoteNetworkProfileConfiguration[]>()
            ?? [];

        var providers = providerConfigurations.Select(configurationItem =>
            new RemoteProviderRuntimeDefinition(
                configurationItem.Protocol,
                configurationItem.ProviderPackageId,
                configurationItem.PackageVersion,
                configurationItem.Image,
                new RuntimeResourceLimits(
                    configurationItem.MemoryMegabytes,
                    configurationItem.CpuLimit,
                    configurationItem.PidsLimit)));
        var profiles = profileConfigurations.Select(configurationItem =>
            new RemoteNetworkProfileDefinition(
                configurationItem.NetworkProfileId,
                configurationItem.Default,
                configurationItem.RuntimeNetworks,
                configurationItem.AllowedTargetPatterns,
                configurationItem.AllowedPorts));
        return new ConfiguredRemoteRuntimePolicy(providers, profiles);
    }

    /// <inheritdoc />
    public RemoteRuntimeSelection Resolve(
        string protocol,
        Guid? networkProfileId,
        RemoteTargetContract target)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (!this.providers.TryGetValue(protocol, out var provider))
        {
            throw new RemoteRuntimePolicyException(
                RemoteSessionFailureCodes.ProtocolUnsupported,
                "No configured Remote provider supports the requested protocol.");
        }

        RemoteNetworkProfileDefinition profile;
        if (networkProfileId is null)
        {
            profile = this.defaultProfile
                ?? throw new RemoteRuntimePolicyException(
                    RemoteSessionFailureCodes.NetworkProfileUnavailable,
                    "No default Remote network profile is configured.");
        }
        else
        {
            if (!this.profiles.TryGetValue(networkProfileId.Value, out var configuredProfile))
            {
                throw new RemoteRuntimePolicyException(
                    RemoteSessionFailureCodes.NetworkProfileUnavailable,
                    "The selected Remote network profile is unavailable.");
            }
            profile = configuredProfile;
        }

        if (!profile.AllowedPorts.Contains(target.Port)
            || !profile.AllowedTargetPatterns.Any(pattern => TargetMatches(pattern, target.Host)))
        {
            throw new RemoteRuntimePolicyException(
                RemoteSessionFailureCodes.TargetInvalid,
                "The Remote target is not allowed by the selected network profile.");
        }

        return new RemoteRuntimeSelection(provider, profile);
    }

    private static void ValidateProvider(RemoteProviderRuntimeDefinition provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        if (!ProtocolPattern().IsMatch(provider.Protocol))
        {
            throw new InvalidOperationException("A configured Remote provider protocol is invalid.");
        }
        try
        {
            _ = PackageId.Parse(provider.ProviderPackageId);
        }
        catch (DomainRuleViolationException exception)
        {
            throw new InvalidOperationException("A configured Remote provider package identity is invalid.", exception);
        }
        if (!SemanticVersionPattern().IsMatch(provider.PackageVersion))
        {
            throw new InvalidOperationException("A configured Remote provider version is invalid.");
        }
        if (!ImageDigestPattern().IsMatch(provider.Image))
        {
            throw new InvalidOperationException("A configured Remote provider image must be digest-pinned.");
        }
        if (provider.Limits.MemoryMegabytes is < 16 or > 262144
            || provider.Limits.CpuLimit is <= 0 or > 64
            || provider.Limits.PidsLimit is < 1 or > 4096)
        {
            throw new InvalidOperationException("Configured Remote provider resource limits are invalid.");
        }
    }

    private static void ValidateProfile(RemoteNetworkProfileDefinition profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (profile.NetworkProfileId == Guid.Empty)
        {
            throw new InvalidOperationException("A Remote network profile identity is invalid.");
        }
        if (profile.RuntimeNetworks.Count == 0
            || profile.RuntimeNetworks.Count != profile.RuntimeNetworks.Distinct(StringComparer.Ordinal).Count()
            || profile.RuntimeNetworks.Any(network => !RuntimeNetworkPattern().IsMatch(network)
                || network is "host" or "none"))
        {
            throw new InvalidOperationException("Remote runtime networks are invalid.");
        }
        if (profile.AllowedPorts.Count == 0
            || profile.AllowedPorts.Count != profile.AllowedPorts.Distinct().Count()
            || profile.AllowedPorts.Any(port => port is < 1 or > 65535))
        {
            throw new InvalidOperationException("Remote target ports are invalid.");
        }
        if (profile.AllowedTargetPatterns.Count == 0
            || profile.AllowedTargetPatterns.Count
                != profile.AllowedTargetPatterns.Distinct(StringComparer.OrdinalIgnoreCase).Count()
            || profile.AllowedTargetPatterns.Any(pattern => !IsValidTargetPattern(pattern)))
        {
            throw new InvalidOperationException("Remote target patterns are invalid.");
        }
    }

    private static bool IsValidTargetPattern(string pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern)
            || pattern != pattern.Trim()
            || pattern.Length > 253
            || pattern.Any(char.IsControl)
            || pattern.Contains('/')
            || pattern.Contains('\\')
            || (pattern.Contains(':') && !IPAddress.TryParse(pattern, out _)))
        {
            return false;
        }
        var candidate = pattern.StartsWith("*.", StringComparison.Ordinal)
            ? pattern[2..]
            : pattern;
        return IPAddress.TryParse(candidate, out _)
            || Uri.CheckHostName(candidate) == UriHostNameType.Dns;
    }

    private static bool TargetMatches(string pattern, string host)
    {
        if (pattern.StartsWith("*.", StringComparison.Ordinal))
        {
            var suffix = pattern[1..];
            return host.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
                && host.Length > suffix.Length;
        }
        return string.Equals(pattern, host, StringComparison.OrdinalIgnoreCase);
    }

    [GeneratedRegex("^[a-z][a-z0-9.-]{0,31}$", RegexOptions.CultureInvariant)]
    private static partial Regex ProtocolPattern();

    [GeneratedRegex("^[a-z0-9][a-z0-9-]{0,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex RuntimeNetworkPattern();

    [GeneratedRegex("^[^@\\s]+@sha256:[a-f0-9]{64}$", RegexOptions.CultureInvariant)]
    private static partial Regex ImageDigestPattern();

    [GeneratedRegex("^[0-9]+\\.[0-9]+\\.[0-9]+(?:-[0-9A-Za-z.-]+)?(?:\\+[0-9A-Za-z.-]+)?$", RegexOptions.CultureInvariant)]
    private static partial Regex SemanticVersionPattern();

    private sealed class RemoteProviderConfiguration
    {
        public string Protocol { get; init; } = string.Empty;

        public string ProviderPackageId { get; init; } = string.Empty;

        public string PackageVersion { get; init; } = string.Empty;

        public string Image { get; init; } = string.Empty;

        public int MemoryMegabytes { get; init; }

        public decimal CpuLimit { get; init; }

        public int PidsLimit { get; init; }
    }

    private sealed class RemoteNetworkProfileConfiguration
    {
        public Guid NetworkProfileId { get; init; }

        public bool Default { get; init; }

        public string[] RuntimeNetworks { get; init; } = [];

        public string[] AllowedTargetPatterns { get; init; } = [];

        public int[] AllowedPorts { get; init; } = [];
    }
}
