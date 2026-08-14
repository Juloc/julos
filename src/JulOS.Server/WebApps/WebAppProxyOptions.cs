using System.Net.Security;

namespace JulOS.Server.WebApps;

/// <summary>Deployment options for the local web-application reverse proxy.</summary>
/// <param name="AllowInvalidUpstreamCertificates">
/// When <see langword="true"/>, the proxy connects to an internal upstream that presents an
/// invalid or self-signed certificate. It is off by default; certificate pinning per target is a
/// later milestone (see <c>docs/WEB-APP-RENDERING.md</c>).
/// </param>
internal sealed record WebAppProxyOptions(bool AllowInvalidUpstreamCertificates)
{
    /// <summary>Reads the proxy options. Missing configuration keeps every secure default.</summary>
    internal static WebAppProxyOptions Read(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        return new WebAppProxyOptions(
            configuration.GetValue("WebApps:AllowInvalidUpstreamCertificates", false));
    }

    /// <summary>
    /// Decides whether an upstream certificate is acceptable. A valid certificate is always
    /// accepted; an invalid one is accepted only when the deployment explicitly opts in.
    /// </summary>
    internal bool UpstreamCertificateIsAcceptable(SslPolicyErrors errors) =>
        errors == SslPolicyErrors.None || this.AllowInvalidUpstreamCertificates;
}
