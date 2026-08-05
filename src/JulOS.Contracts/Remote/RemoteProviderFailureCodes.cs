namespace JulOS.Contracts.Remote;

/// <summary>Caller-safe failures emitted by trusted Remote protocol providers.</summary>
public static class RemoteProviderFailureCodes
{
    /// <summary>The target account is disabled, locked, expired or otherwise unavailable.</summary>
    public const string AccountUnavailable = "remote.account_unavailable";
}
