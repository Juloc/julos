namespace JulOS.Contracts.WebApps;

/// <summary>Client configuration for the dynamic "type a URL" web-application proxy.</summary>
/// <param name="Enabled">Whether the dynamic proxy mode is enabled for this deployment.</param>
/// <param name="ProxyZone">The DNS zone under which encoded proxy hosts are served.</param>
public sealed record WebProxyConfigResponse(bool Enabled, string ProxyZone);
