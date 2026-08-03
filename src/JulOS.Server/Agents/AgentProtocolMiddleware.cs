using System.Globalization;

using JulOS.Contracts.Agents;

namespace JulOS.Server.Agents;

internal sealed class AgentProtocolMiddleware
{
    private readonly RequestDelegate next;

    public AgentProtocolMiddleware(RequestDelegate next)
    {
        this.next = next ?? throw new ArgumentNullException(nameof(next));
    }

    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!context.Request.Path.StartsWithSegments("/api/v1/agent"))
        {
            await this.next(context).ConfigureAwait(false);
            return;
        }

        AddCompatibilityHeaders(context.Response);
        var value = context.Request.Headers[AgentProtocolContract.HeaderName].ToString();
        if (!int.TryParse(
                value,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var protocolVersion)
            || !AgentProtocolContract.IsSupported(protocolVersion))
        {
            context.Response.StatusCode = StatusCodes.Status426UpgradeRequired;
            await context.Response.WriteAsJsonAsync(new
            {
                code = "agent.protocol_incompatible",
                detail = "The Agent protocol version is missing or incompatible.",
                minimumSupportedVersion = AgentProtocolContract.MinimumSupportedVersion,
                maximumSupportedVersion = AgentProtocolContract.MaximumSupportedVersion,
            }, context.RequestAborted).ConfigureAwait(false);
            return;
        }

        await this.next(context).ConfigureAwait(false);
    }

    private static void AddCompatibilityHeaders(HttpResponse response)
    {
        response.Headers[AgentProtocolContract.HeaderName] =
            AgentProtocolContract.CurrentVersion.ToString(CultureInfo.InvariantCulture);
        response.Headers[AgentProtocolContract.MinimumHeaderName] =
            AgentProtocolContract.MinimumSupportedVersion.ToString(CultureInfo.InvariantCulture);
        response.Headers[AgentProtocolContract.MaximumHeaderName] =
            AgentProtocolContract.MaximumSupportedVersion.ToString(CultureInfo.InvariantCulture);
    }
}

internal static class AgentProtocolMiddlewareExtensions
{
    internal static IApplicationBuilder UseJulOsAgentProtocol(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        return app.UseMiddleware<AgentProtocolMiddleware>();
    }
}
