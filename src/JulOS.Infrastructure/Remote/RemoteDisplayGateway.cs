using System.Globalization;

using JulOS.Contracts.Remote;

using Microsoft.Extensions.Configuration;

namespace JulOS.Infrastructure.Remote;

/// <summary>Issues token-free browser display descriptors and resolves hidden provider endpoints.</summary>
public sealed class RemoteDisplayGateway
{
    /// <summary>JulOS 1.0 graphical display kind.</summary>
    public const string DisplayKind = "graphical";

    /// <summary>JulOS 1.0 display transport contract version.</summary>
    public const string ContractVersion = "1.0.0";

    private const int DefaultGrantLifetimeSeconds = 60;
    private const int MaximumGrantLifetimeSeconds = 300;
    private readonly string providerEndpointTemplate;
    private readonly string publicOrigin;
    private readonly TimeSpan grantLifetime;
    private readonly TimeProvider timeProvider;

    private RemoteDisplayGateway(
        string providerEndpointTemplate,
        string publicOrigin,
        TimeSpan grantLifetime,
        TimeProvider timeProvider)
    {
        this.providerEndpointTemplate = providerEndpointTemplate;
        this.publicOrigin = publicOrigin;
        this.grantLifetime = grantLifetime;
        this.timeProvider = timeProvider;
    }

    /// <summary>Gets whether the display gateway has complete configuration.</summary>
    public bool IsConfigured =>
        this.providerEndpointTemplate.Length > 0
        && this.publicOrigin.Length > 0;

    /// <summary>Reads display gateway configuration. Missing complete configuration disables grants.</summary>
    public static RemoteDisplayGateway Read(IConfiguration configuration, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(timeProvider);

        var providerTemplate = configuration["Remote:Display:ProviderEndpointTemplate"]
            ?? Environment.GetEnvironmentVariable("JULOS_REMOTE_DISPLAY_PROVIDER_ENDPOINT_TEMPLATE");
        var configuredOrigin = configuration["Remote:Display:PublicOrigin"]
            ?? Environment.GetEnvironmentVariable("JULOS_REMOTE_DISPLAY_PUBLIC_ORIGIN");
        var lifetimeValue = configuration["Remote:Display:GrantLifetimeSeconds"]
            ?? Environment.GetEnvironmentVariable("JULOS_REMOTE_DISPLAY_GRANT_LIFETIME_SECONDS");

        if (string.IsNullOrWhiteSpace(providerTemplate)
            && string.IsNullOrWhiteSpace(configuredOrigin))
        {
            return new RemoteDisplayGateway(
                string.Empty,
                string.Empty,
                TimeSpan.Zero,
                timeProvider);
        }

        providerTemplate = ValidateProviderTemplate(providerTemplate);
        configuredOrigin = ValidatePublicOrigin(configuredOrigin);

        var lifetimeSeconds = string.IsNullOrWhiteSpace(lifetimeValue)
            ? DefaultGrantLifetimeSeconds
            : int.TryParse(
                lifetimeValue,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var parsedLifetime)
                ? parsedLifetime
                : 0;
        if (lifetimeSeconds is < 1 or > MaximumGrantLifetimeSeconds)
        {
            throw new InvalidOperationException(
                $"Remote display grant lifetime must be from 1 through {MaximumGrantLifetimeSeconds} seconds.");
        }

        return new RemoteDisplayGateway(
            providerTemplate,
            configuredOrigin,
            TimeSpan.FromSeconds(lifetimeSeconds),
            timeProvider);
    }

    /// <summary>Issues one token-free same-origin descriptor bounded by the session lifetime.</summary>
    public RemoteDisplayTransportResponse Issue(
        Guid sessionId,
        Guid ownerUserId,
        string callerPackageId,
        string runtimeId,
        long revision,
        DateTimeOffset sessionExpiresAtUtc)
    {
        EnsureConfigured();
        ValidateIdentity(sessionId, ownerUserId, callerPackageId, runtimeId, revision);

        var now = this.timeProvider.GetUtcNow();
        var expiresAtUtc = now.Add(this.grantLifetime);
        if (expiresAtUtc > sessionExpiresAtUtc)
        {
            expiresAtUtc = sessionExpiresAtUtc;
        }
        if (expiresAtUtc <= now)
        {
            throw new RemoteDisplayGatewayException(
                "remote.display_session_expired",
                "The Remote session cannot receive a display descriptor after its lifetime ended.");
        }

        var expires = expiresAtUtc.ToUnixTimeSeconds();
        return new RemoteDisplayTransportResponse(
            DisplayKind,
            ContractVersion,
            DescriptorEndpoint(sessionId, callerPackageId, revision, expires),
            expiresAtUtc);
    }

    /// <summary>Verifies an exact active descriptor without accepting a browser-supplied access token.</summary>
    public bool MatchesDescriptor(
        Guid sessionId,
        Guid ownerUserId,
        string callerPackageId,
        string runtimeId,
        long revision,
        long expires,
        string endpoint)
    {
        if (!this.IsConfigured || string.IsNullOrWhiteSpace(endpoint))
        {
            return false;
        }

        try
        {
            ValidateIdentity(sessionId, ownerUserId, callerPackageId, runtimeId, revision);
        }
        catch (RemoteDisplayGatewayException)
        {
            return false;
        }

        DateTimeOffset expiresAtUtc;
        try
        {
            expiresAtUtc = DateTimeOffset.FromUnixTimeSeconds(expires);
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }

        var now = this.timeProvider.GetUtcNow();
        if (expiresAtUtc <= now || expiresAtUtc - now > this.grantLifetime)
        {
            return false;
        }

        var expected = DescriptorEndpoint(sessionId, callerPackageId, revision, expires);
        return string.Equals(endpoint, expected, StringComparison.Ordinal);
    }

    /// <summary>Returns whether a browser Origin exactly matches the configured public JulOS origin.</summary>
    public bool IsAllowedOrigin(string? value)
    {
        if (!this.IsConfigured)
        {
            return false;
        }

        var normalized = TryNormalizePublicOrigin(value);
        return normalized is not null
            && string.Equals(normalized, this.publicOrigin, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Resolves the provider endpoint without exposing it to the browser.</summary>
    public Uri ProviderEndpoint(string runtimeId)
    {
        EnsureConfigured();
        ValidateRuntimeId(runtimeId);

        return new Uri(
            this.providerEndpointTemplate.Replace("{runtimeId}", runtimeId, StringComparison.Ordinal),
            UriKind.Absolute);
    }

    private static string DescriptorEndpoint(
        Guid sessionId,
        string callerPackageId,
        long revision,
        long expires)
    {
        var package = Uri.EscapeDataString(callerPackageId);
        return string.Create(
            CultureInfo.InvariantCulture,
            $"/api/v1/remote/sessions/{sessionId:D}/display?package={package}&revision={revision}&expires={expires}");
    }

    private static string ValidateProviderTemplate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value.Count(character => character == '{') != 1
            || value.Count(character => character == '}') != 1
            || !value.Contains("{runtimeId}", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Remote display provider endpoint template must contain exactly one {runtimeId} placeholder.");
        }

        var probe = value.Replace("{runtimeId}", "remote-probe", StringComparison.Ordinal);
        if (!Uri.TryCreate(probe, UriKind.Absolute, out var endpoint)
            || endpoint.Scheme is not ("ws" or "wss")
            || !string.IsNullOrEmpty(endpoint.UserInfo)
            || !string.IsNullOrEmpty(endpoint.Fragment))
        {
            throw new InvalidOperationException(
                "Remote display provider endpoint template must resolve to an absolute WS or WSS URI without user information or fragment.");
        }

        return value;
    }

    private static string ValidatePublicOrigin(string? value) =>
        TryNormalizePublicOrigin(value)
        ?? throw new InvalidOperationException(
            "Remote display public origin must be one absolute HTTP or HTTPS origin without path, query, fragment or user information.");

    private static string? TryNormalizePublicOrigin(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)
            || !Uri.TryCreate(value, UriKind.Absolute, out var origin)
            || origin.Scheme is not ("http" or "https")
            || !string.IsNullOrEmpty(origin.UserInfo)
            || !string.IsNullOrEmpty(origin.Query)
            || !string.IsNullOrEmpty(origin.Fragment)
            || origin.AbsolutePath is not "/")
        {
            return null;
        }

        var builder = new UriBuilder(origin.Scheme, origin.IdnHost)
        {
            Port = origin.IsDefaultPort ? -1 : origin.Port,
            Path = string.Empty,
            Query = string.Empty,
            Fragment = string.Empty,
        };
        return builder.Uri.GetLeftPart(UriPartial.Authority);
    }

    private static void ValidateIdentity(
        Guid sessionId,
        Guid ownerUserId,
        string callerPackageId,
        string runtimeId,
        long revision)
    {
        if (sessionId == Guid.Empty || ownerUserId == Guid.Empty || revision < 1)
        {
            throw new RemoteDisplayGatewayException(
                "remote.display_identity_invalid",
                "Remote display descriptor identity is invalid.");
        }
        if (string.IsNullOrWhiteSpace(callerPackageId)
            || callerPackageId != callerPackageId.Trim()
            || callerPackageId.Length > 128
            || callerPackageId.Any(char.IsControl))
        {
            throw new RemoteDisplayGatewayException(
                "remote.display_package_invalid",
                "Remote display package identity is invalid.");
        }

        ValidateRuntimeId(runtimeId);
    }

    private static void ValidateRuntimeId(string runtimeId)
    {
        if (string.IsNullOrWhiteSpace(runtimeId)
            || runtimeId != runtimeId.Trim()
            || runtimeId.Length > 64
            || runtimeId.Any(character => !(char.IsAsciiLetterOrDigit(character) || character == '-'))
        {
            throw new RemoteDisplayGatewayException(
                "remote.display_runtime_invalid",
                "Remote display runtime identity is invalid.");
        }
    }

    private void EnsureConfigured()
    {
        if (!this.IsConfigured)
        {
            throw new RemoteDisplayGatewayException(
                "remote.display_not_configured",
                "Remote display transport is not configured.");
        }
    }
}

/// <summary>Caller-safe display descriptor or provider endpoint failure.</summary>
public sealed class RemoteDisplayGatewayException : Exception
{
    /// <summary>Creates a Remote display gateway failure.</summary>
    public RemoteDisplayGatewayException(string code, string message)
        : base(message)
    {
        this.Code = code;
    }

    /// <summary>Gets the stable failure code.</summary>
    public string Code { get; }
}
