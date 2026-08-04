using System.Globalization;
using System.Security.Cryptography;
using System.Text;

using Microsoft.Extensions.Configuration;

namespace JulOS.Infrastructure.Remote;

/// <summary>Issues and verifies expiring session- and runtime-scoped provider callback tokens.</summary>
public sealed class RemoteProviderCallbackAuthenticator : IDisposable
{
    private const string TokenVersion = "v1";
    private const int MaximumTokenLength = 256;
    private static readonly TimeSpan MaximumLifetime = TimeSpan.FromDays(7).Add(TimeSpan.FromMinutes(1));
    private readonly byte[] signingKey;
    private readonly TimeProvider timeProvider;
    private bool disposed;

    private RemoteProviderCallbackAuthenticator(
        Uri? endpoint,
        byte[] signingKey,
        TimeProvider timeProvider)
    {
        this.Endpoint = endpoint;
        this.signingKey = signingKey;
        this.timeProvider = timeProvider;
    }

    /// <summary>Gets the private Server endpoint supplied to provider runtimes.</summary>
    public Uri? Endpoint { get; }

    /// <summary>Gets whether provider callbacks are configured.</summary>
    public bool IsConfigured => this.Endpoint is not null && this.signingKey.Length > 0;

    /// <summary>Reads and validates provider callback configuration.</summary>
    public static RemoteProviderCallbackAuthenticator Read(
        IConfiguration configuration,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(timeProvider);
        var endpointValue = configuration["Remote:ProviderCallback:Endpoint"]
            ?? Environment.GetEnvironmentVariable("JULOS_REMOTE_PROVIDER_CALLBACK_ENDPOINT");
        var signingKeyValue = configuration["Remote:ProviderCallback:SigningKey"]
            ?? Environment.GetEnvironmentVariable("JULOS_REMOTE_PROVIDER_CALLBACK_KEY");

        if (string.IsNullOrWhiteSpace(endpointValue) && string.IsNullOrWhiteSpace(signingKeyValue))
        {
            return new RemoteProviderCallbackAuthenticator(null, [], timeProvider);
        }
        if (!Uri.TryCreate(endpointValue, UriKind.Absolute, out var endpoint)
            || endpoint.Scheme is not ("http" or "https"))
        {
            throw new InvalidOperationException(
                "Remote:ProviderCallback:Endpoint must be an absolute HTTP or HTTPS URI.");
        }
        if (signingKeyValue is null
            || signingKeyValue.Length < 32
            || signingKeyValue.Any(char.IsControl))
        {
            throw new InvalidOperationException(
                "Remote provider callback signing key must contain at least 32 non-control characters.");
        }

        return new RemoteProviderCallbackAuthenticator(
            endpoint,
            Encoding.UTF8.GetBytes(signingKeyValue),
            timeProvider);
    }

    /// <summary>Creates one callback token whose validity cannot exceed the session lifetime.</summary>
    public string Issue(Guid sessionId, string runtimeId, DateTimeOffset expiresAtUtc)
    {
        ObjectDisposedException.ThrowIf(this.disposed, this);
        if (!this.IsConfigured)
        {
            throw new RemoteProviderCallbackAuthenticationException(
                "remote.provider_callback_not_configured",
                "Remote provider callback authentication is not configured.");
        }
        ValidateIdentity(sessionId, runtimeId);
        var now = this.timeProvider.GetUtcNow();
        if (expiresAtUtc <= now || expiresAtUtc - now > MaximumLifetime)
        {
            throw new RemoteProviderCallbackAuthenticationException(
                "remote.provider_callback_expiry_invalid",
                "Remote provider callback token expiry is invalid.");
        }

        var expires = expiresAtUtc.ToUnixTimeSeconds();
        var signature = Sign(sessionId, runtimeId, expires, this.signingKey);
        try
        {
            return string.Create(
                CultureInfo.InvariantCulture,
                $"{TokenVersion}.{expires}.{Base64UrlEncode(signature)}");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(signature);
        }
    }

    /// <summary>Verifies one callback token in constant time.</summary>
    public bool Authenticate(Guid sessionId, string runtimeId, string? token)
    {
        ObjectDisposedException.ThrowIf(this.disposed, this);
        if (!this.IsConfigured
            || string.IsNullOrWhiteSpace(token)
            || token.Length > MaximumTokenLength)
        {
            return false;
        }
        var parts = token.Split('.', StringSplitOptions.None);
        if (parts.Length != 3
            || !string.Equals(parts[0], TokenVersion, StringComparison.Ordinal)
            || !long.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var expires))
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
        if (expiresAtUtc <= now || expiresAtUtc - now > MaximumLifetime)
        {
            return false;
        }

        try
        {
            ValidateIdentity(sessionId, runtimeId);
        }
        catch (RemoteProviderCallbackAuthenticationException)
        {
            return false;
        }

        var expected = this.Issue(sessionId, runtimeId, expiresAtUtc);
        var suppliedBytes = Encoding.UTF8.GetBytes(token);
        var expectedBytes = Encoding.UTF8.GetBytes(expected);
        try
        {
            return suppliedBytes.Length == expectedBytes.Length
                && CryptographicOperations.FixedTimeEquals(suppliedBytes, expectedBytes);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(suppliedBytes);
            CryptographicOperations.ZeroMemory(expectedBytes);
        }
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

    private static byte[] Sign(Guid sessionId, string runtimeId, long expires, byte[] signingKey)
    {
        var message = Encoding.UTF8.GetBytes(string.Create(
            CultureInfo.InvariantCulture,
            $"{TokenVersion}\n{sessionId:D}\n{runtimeId}\n{expires}"));
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

    private static void ValidateIdentity(Guid sessionId, string runtimeId)
    {
        if (sessionId == Guid.Empty
            || string.IsNullOrWhiteSpace(runtimeId)
            || runtimeId != runtimeId.Trim()
            || runtimeId.Length > 64
            || runtimeId.Any(character => !(char.IsAsciiLetterOrDigit(character) || character == '-')))
        {
            throw new RemoteProviderCallbackAuthenticationException(
                "remote.provider_callback_identity_invalid",
                "Remote provider callback identity is invalid.");
        }
    }
}

/// <summary>Stable provider callback configuration or token-issuance failure.</summary>
public sealed class RemoteProviderCallbackAuthenticationException : Exception
{
    /// <summary>Creates a caller-safe provider callback authentication failure.</summary>
    public RemoteProviderCallbackAuthenticationException(string code, string message)
        : base(message)
    {
        this.Code = code;
    }

    /// <summary>Gets the stable failure code.</summary>
    public string Code { get; }
}
