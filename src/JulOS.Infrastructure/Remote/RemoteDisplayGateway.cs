using System.Globalization;
using System.Security.Cryptography;
using System.Text;

using JulOS.Contracts.Remote;

using Microsoft.Extensions.Configuration;

namespace JulOS.Infrastructure.Remote;

/// <summary>Issues browser display grants and resolves the hidden provider WebSocket endpoint.</summary>
public sealed class RemoteDisplayGateway : IDisposable
{
    private const string TokenVersion = "display-v1";
    private const int MaximumTokenLength = 128;
    private const int DefaultGrantLifetimeSeconds = 60;
    private const int MaximumGrantLifetimeSeconds = 300;
    private readonly byte[] signingKey;
    private readonly string providerEndpointTemplate;
    private readonly TimeSpan grantLifetime;
    private readonly TimeProvider timeProvider;
    private bool disposed;

    private RemoteDisplayGateway(
        byte[] signingKey,
        string providerEndpointTemplate,
        TimeSpan grantLifetime,
        TimeProvider timeProvider)
    {
        this.signingKey = signingKey;
        this.providerEndpointTemplate = providerEndpointTemplate;
        this.grantLifetime = grantLifetime;
        this.timeProvider = timeProvider;
    }

    /// <summary>Gets whether the display gateway has complete configuration.</summary>
    public bool IsConfigured => this.signingKey.Length > 0 && this.providerEndpointTemplate.Length > 0;

    /// <summary>Reads display gateway configuration. Missing complete configuration disables grants.</summary>
    public static RemoteDisplayGateway Read(IConfiguration configuration, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(timeProvider);
        var signingKeyValue = configuration["Remote:Display:SigningKey"]
            ?? Environment.GetEnvironmentVariable("JULOS_REMOTE_DISPLAY_SIGNING_KEY");
        var providerTemplate = configuration["Remote:Display:ProviderEndpointTemplate"]
            ?? Environment.GetEnvironmentVariable("JULOS_REMOTE_DISPLAY_PROVIDER_ENDPOINT_TEMPLATE");
        var lifetimeValue = configuration["Remote:Display:GrantLifetimeSeconds"]
            ?? Environment.GetEnvironmentVariable("JULOS_REMOTE_DISPLAY_GRANT_LIFETIME_SECONDS");

        if (string.IsNullOrWhiteSpace(signingKeyValue)
            && string.IsNullOrWhiteSpace(providerTemplate))
        {
            return new RemoteDisplayGateway([], string.Empty, TimeSpan.Zero, timeProvider);
        }
        if (signingKeyValue is null
            || signingKeyValue.Length < 32
            || signingKeyValue.Any(char.IsControl))
        {
            throw new InvalidOperationException(
                "Remote display signing key must contain at least 32 non-control characters.");
        }
        providerTemplate = ValidateProviderTemplate(providerTemplate);
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
            Encoding.UTF8.GetBytes(signingKeyValue),
            providerTemplate,
            TimeSpan.FromSeconds(lifetimeSeconds),
            timeProvider);
    }

    /// <summary>Issues one same-origin WebSocket descriptor bounded by the session lifetime.</summary>
    public RemoteDisplayTransportResponse Issue(
        Guid sessionId,
        Guid ownerUserId,
        string callerPackageId,
        string runtimeId,
        long revision,
        DateTimeOffset sessionExpiresAtUtc)
    {
        ObjectDisposedException.ThrowIf(this.disposed, this);
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
                "The Remote session cannot receive a display grant after its lifetime ended.");
        }

        var expires = expiresAtUtc.ToUnixTimeSeconds();
        var token = Sign(
            sessionId,
            ownerUserId,
            callerPackageId,
            runtimeId,
            revision,
            expires,
            this.signingKey);
        try
        {
            var encodedToken = Base64UrlEncode(token);
            var endpoint = string.Create(
                CultureInfo.InvariantCulture,
                $"/api/v1/remote/sessions/{sessionId:D}/display?revision={revision}&expires={expires}&token={encodedToken}");
            return new RemoteDisplayTransportResponse(
                "websocket",
                "1.0.0",
                endpoint,
                expiresAtUtc);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(token);
        }
    }

    /// <summary>Verifies one exact user, package, runtime, revision and expiry grant.</summary>
    public bool Authenticate(
        Guid sessionId,
        Guid ownerUserId,
        string callerPackageId,
        string runtimeId,
        long revision,
        long expires,
        string? token)
    {
        ObjectDisposedException.ThrowIf(this.disposed, this);
        if (!this.IsConfigured
            || string.IsNullOrWhiteSpace(token)
            || token.Length > MaximumTokenLength)
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

        byte[] supplied;
        try
        {
            supplied = Base64UrlDecode(token);
        }
        catch (FormatException)
        {
            return false;
        }
        var expected = Sign(
            sessionId,
            ownerUserId,
            callerPackageId,
            runtimeId,
            revision,
            expires,
            this.signingKey);
        try
        {
            return supplied.Length == expected.Length
                && CryptographicOperations.FixedTimeEquals(supplied, expected);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(supplied);
            CryptographicOperations.ZeroMemory(expected);
        }
    }

    /// <summary>Resolves the provider endpoint without exposing it to the browser.</summary>
    public Uri ProviderEndpoint(string runtimeId)
    {
        ObjectDisposedException.ThrowIf(this.disposed, this);
        EnsureConfigured();
        ValidateRuntimeId(runtimeId);
        return new Uri(
            this.providerEndpointTemplate.Replace("{runtimeId}", runtimeId, StringComparison.Ordinal),
            UriKind.Absolute);
    }

    /// <summary>Clears the in-memory signing key.</summary>
    public void Dispose()
    {
        if (this.disposed)
        {
            return;
        }
        this.disposed = true;
        CryptographicOperations.ZeroMemory(this.signingKey);
        GC.SuppressFinalize(this);
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

    private static byte[] Sign(
        Guid sessionId,
        Guid ownerUserId,
        string callerPackageId,
        string runtimeId,
        long revision,
        long expires,
        byte[] signingKey)
    {
        var message = Encoding.UTF8.GetBytes(string.Create(
            CultureInfo.InvariantCulture,
            $"{TokenVersion}\n{sessionId:D}\n{ownerUserId:D}\n{callerPackageId}\n{runtimeId}\n{revision}\n{expires}"));
        try
        {
            return HMACSHA256.HashData(signingKey, message);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(message);
        }
    }

    private static string Base64UrlEncode(byte[] value) =>
        Convert.ToBase64String(value)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    private static byte[] Base64UrlDecode(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        padded += padded.Length % 4 switch
        {
            0 => string.Empty,
            2 => "==",
            3 => "=",
            _ => throw new FormatException("Invalid Base64URL value."),
        };
        return Convert.FromBase64String(padded);
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
                "Remote display grant identity is invalid.");
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
            || runtimeId.Any(character => !(char.IsAsciiLetterOrDigit(character) || character == '-')))
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

/// <summary>Caller-safe display grant or provider endpoint failure.</summary>
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
