using Microsoft.AspNetCore.DataProtection;

namespace JulOS.Server.WebApps;

/// <summary>
/// Issues short-lived bearer capabilities for Browser subresources whose fetch mode omits the
/// normal JulOS session cookie (notably cross-origin fonts).
/// </summary>
internal sealed class WebAppProxyAccessTokenService
{
    private const int LifetimeSeconds = 600;
    private readonly IDataProtector protector;

    public WebAppProxyAccessTokenService(IDataProtectionProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        this.protector = provider.CreateProtector("JulOS.WebAppProxy.Subresource.v1");
    }

    internal string Create(string proxyHost)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(proxyHost);
        var expires = DateTimeOffset.UtcNow.AddSeconds(LifetimeSeconds).ToUnixTimeSeconds();
        return this.protector.Protect(string.Concat(proxyHost.Trim().ToLowerInvariant(), "|", expires));
    }

    internal bool TryValidate(string token, string proxyHost)
    {
        if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(proxyHost))
        {
            return false;
        }

        try
        {
            var payload = this.protector.Unprotect(token);
            var separator = payload.LastIndexOf('|');
            if (separator <= 0
                || !long.TryParse(payload[(separator + 1)..], out var expires)
                || expires < DateTimeOffset.UtcNow.ToUnixTimeSeconds())
            {
                return false;
            }

            return string.Equals(
                payload[..separator],
                proxyHost.Trim().ToLowerInvariant(),
                StringComparison.Ordinal);
        }
        catch
        {
            return false;
        }
    }
}
