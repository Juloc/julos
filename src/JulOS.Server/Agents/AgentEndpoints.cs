using System.Globalization;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;

using JulOS.Application.Agents;
using JulOS.Contracts.Agents;
using JulOS.Server.Authentication;
using JulOS.Server.Authorization;

using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Mvc;

namespace JulOS.Server.Agents;

internal static class AgentEndpointItems
{
    internal const string AgentId = "JulOS.AgentId";
}

internal sealed class AgentAuthenticationFilter : IEndpointFilter
{
    private const string AgentIdHeader = "X-JulOS-Agent-Id";
    private const string CredentialHeader = "X-JulOS-Agent-Credential";
    private readonly IAgentControlService service;

    public AgentAuthenticationFilter(IAgentControlService service)
    {
        this.service = service ?? throw new ArgumentNullException(nameof(service));
    }

    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        var httpContext = context.HttpContext;
        if (!Guid.TryParseExact(httpContext.Request.Headers[AgentIdHeader], "D", out var agentId))
        {
            return Unauthorized();
        }

        var encodedCredential = httpContext.Request.Headers[CredentialHeader].ToString();
        if (!TryDecodeCredential(encodedCredential, out var credential))
        {
            return Unauthorized();
        }

        try
        {
            if (!await this.service.AuthenticateAsync(
                    agentId,
                    credential,
                    httpContext.RequestAborted)
                .ConfigureAwait(false))
            {
                return Unauthorized();
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(credential);
        }

        httpContext.Items[AgentEndpointItems.AgentId] = agentId;
        return await next(context).ConfigureAwait(false);
    }

    private static IResult Unauthorized() => Results.Json(
        new { code = "agent.authentication_failed", detail = "Agent authentication failed." },
        statusCode: StatusCodes.Status401Unauthorized);

    private static bool TryDecodeCredential(string encoded, out byte[] credential)
    {
        credential = [];
        if (encoded.Length is < 32 or > 1024 || encoded.Any(char.IsControl))
        {
            return false;
        }

        var normalized = encoded.Replace('-', '+').Replace('_', '/');
        normalized += (normalized.Length % 4) switch
        {
            2 => "==",
            3 => "=",
            _ => string.Empty,
        };
        try
        {
            credential = Convert.FromBase64String(normalized);
            return credential.Length is >= 24 and <= 256;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}

internal static class AgentEndpoints
{
    private const int HeartbeatIntervalSeconds = 30;
    private const int CommandPollIntervalSeconds = 5;

    internal static IEndpointRouteBuilder MapJulOsAgents(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var administration = endpoints.MapGroup("/api/v1/agents").WithTags("Agents");
        administration.MapPost("/enrollment-tokens", CreateEnrollmentTokenAsync)
            .RequireAuthorization(JulOsAuthorizationPolicies.AuthorizationManage)
            .RequireJulOsAntiforgery();
        administration.MapGet("/", ListAsync)
            .RequireAuthorization(JulOsAuthorizationPolicies.AuthorizationRead);
        administration.MapGet("/{agentId:guid}", ReadAsync)
            .RequireAuthorization(JulOsAuthorizationPolicies.AuthorizationRead);
        administration.MapPost("/{agentId:guid}/revoke", RevokeAsync)
            .RequireAuthorization(JulOsAuthorizationPolicies.AuthorizationManage)
            .RequireJulOsAntiforgery();
        administration.MapPost("/{agentId:guid}/commands", CreateCommandAsync)
            .RequireAuthorization(JulOsAuthorizationPolicies.AuthorizationManage)
            .RequireJulOsAntiforgery();
        administration.MapGet("/{agentId:guid}/metrics", ReadMetricsAsync)
            .RequireAuthorization(JulOsAuthorizationPolicies.AuthorizationRead);

        endpoints.MapPost("/api/v1/agent/enroll", EnrollAsync)
            .AllowAnonymous();

        var agent = endpoints.MapGroup("/api/v1/agent")
            .WithTags("Agent runtime")
            .AllowAnonymous()
            .AddEndpointFilter<AgentAuthenticationFilter>();
        agent.MapPost("/heartbeat", HeartbeatAsync);
        agent.MapPost("/metrics", StoreMetricsAsync);
        agent.MapGet("/commands/next", AcquireCommandAsync);
        agent.MapPost("/commands/{commandId:guid}/complete", CompleteCommandAsync);

        return endpoints;
    }

    private static async Task<IResult> CreateEnrollmentTokenAsync(
        HttpContext context,
        CreateAgentEnrollmentTokenRequest request,
        IAntiforgery antiforgery,
        IAgentControlService service,
        CancellationToken cancellationToken)
    {
        await JulOsAntiforgery.ValidateAsync(context, antiforgery).ConfigureAwait(false);
        try
        {
            return TypedResults.Ok(await service.CreateEnrollmentTokenAsync(
                RequireUserId(context.User),
                request,
                context.TraceIdentifier,
                context.Connection.RemoteIpAddress?.ToString(),
                cancellationToken).ConfigureAwait(false));
        }
        catch (AgentControlException exception)
        {
            return Failure(exception);
        }
    }

    private static async Task<IResult> EnrollAsync(
        HttpContext context,
        RedeemAgentEnrollmentRequest request,
        IAgentControlService service,
        CancellationToken cancellationToken)
    {
        try
        {
            var credential = await service.RedeemEnrollmentTokenAsync(
                request,
                context.TraceIdentifier,
                context.Connection.RemoteIpAddress?.ToString(),
                cancellationToken).ConfigureAwait(false);
            return TypedResults.Ok(new RedeemAgentEnrollmentResponse(
                credential.AgentId,
                credential.Credential,
                credential.EnrolledAtUtc,
                HeartbeatIntervalSeconds,
                CommandPollIntervalSeconds));
        }
        catch (AgentControlException exception)
        {
            return Failure(exception);
        }
    }

    private static async Task<IResult> ListAsync(
        IAgentControlService service,
        CancellationToken cancellationToken) =>
        TypedResults.Ok(await service.ListAsync(cancellationToken).ConfigureAwait(false));

    private static async Task<IResult> ReadAsync(
        Guid agentId,
        IAgentControlService service,
        CancellationToken cancellationToken)
    {
        try
        {
            return TypedResults.Ok(await service.ReadAsync(agentId, cancellationToken).ConfigureAwait(false));
        }
        catch (AgentControlException exception)
        {
            return Failure(exception);
        }
    }

    private static async Task<IResult> RevokeAsync(
        HttpContext context,
        Guid agentId,
        [FromQuery] int revision,
        IAntiforgery antiforgery,
        IAgentControlService service,
        CancellationToken cancellationToken)
    {
        await JulOsAntiforgery.ValidateAsync(context, antiforgery).ConfigureAwait(false);
        try
        {
            return TypedResults.Ok(await service.RevokeAsync(
                RequireUserId(context.User),
                agentId,
                revision,
                context.TraceIdentifier,
                context.Connection.RemoteIpAddress?.ToString(),
                cancellationToken).ConfigureAwait(false));
        }
        catch (AgentControlException exception)
        {
            return Failure(exception);
        }
    }

    private static async Task<IResult> CreateCommandAsync(
        HttpContext context,
        Guid agentId,
        CreateAgentCommandRequest request,
        IAntiforgery antiforgery,
        IAgentControlService service,
        CancellationToken cancellationToken)
    {
        await JulOsAntiforgery.ValidateAsync(context, antiforgery).ConfigureAwait(false);
        try
        {
            return TypedResults.Accepted(
                $"/api/v1/agents/{agentId:D}/commands",
                await service.CreateCommandAsync(
                    RequireUserId(context.User),
                    agentId,
                    request,
                    context.TraceIdentifier,
                    context.Connection.RemoteIpAddress?.ToString(),
                    cancellationToken).ConfigureAwait(false));
        }
        catch (AgentControlException exception)
        {
            return Failure(exception);
        }
    }

    private static async Task<IResult> ReadMetricsAsync(
        Guid agentId,
        [FromQuery] DateTimeOffset fromUtc,
        [FromQuery] DateTimeOffset toUtc,
        IAgentControlService service,
        CancellationToken cancellationToken)
    {
        try
        {
            return TypedResults.Ok(await service.ReadMetricsAsync(
                agentId,
                fromUtc,
                toUtc,
                cancellationToken).ConfigureAwait(false));
        }
        catch (AgentControlException exception)
        {
            return Failure(exception);
        }
    }

    private static async Task<IResult> HeartbeatAsync(
        HttpContext context,
        AgentHeartbeatRequest request,
        IAgentControlService service,
        CancellationToken cancellationToken)
    {
        try
        {
            return TypedResults.Ok(await service.RecordHeartbeatAsync(
                RequireAgentId(context),
                request,
                cancellationToken).ConfigureAwait(false));
        }
        catch (AgentControlException exception)
        {
            return Failure(exception);
        }
    }

    private static async Task<IResult> StoreMetricsAsync(
        HttpContext context,
        AgentMetricBatchRequest request,
        IAgentControlService service,
        CancellationToken cancellationToken)
    {
        try
        {
            await service.StoreMetricsAsync(
                RequireAgentId(context),
                request,
                cancellationToken).ConfigureAwait(false);
            return TypedResults.NoContent();
        }
        catch (AgentControlException exception)
        {
            return Failure(exception);
        }
    }

    private static async Task<IResult> AcquireCommandAsync(
        HttpContext context,
        IAgentControlService service,
        CancellationToken cancellationToken)
    {
        try
        {
            var command = await service.AcquireNextCommandAsync(
                RequireAgentId(context),
                cancellationToken).ConfigureAwait(false);
            return command is null ? TypedResults.NoContent() : TypedResults.Ok(command);
        }
        catch (AgentControlException exception)
        {
            return Failure(exception);
        }
    }

    private static async Task<IResult> CompleteCommandAsync(
        HttpContext context,
        Guid commandId,
        CompleteAgentCommandRequest request,
        IAgentControlService service,
        CancellationToken cancellationToken)
    {
        try
        {
            return TypedResults.Ok(await service.CompleteCommandAsync(
                RequireAgentId(context),
                commandId,
                request,
                cancellationToken).ConfigureAwait(false));
        }
        catch (AgentControlException exception)
        {
            return Failure(exception);
        }
    }

    private static Guid RequireAgentId(HttpContext context) =>
        context.Items.TryGetValue(AgentEndpointItems.AgentId, out var value) && value is Guid agentId
            ? agentId
            : throw new InvalidOperationException("Authenticated Agent identity is missing.");

    private static Guid RequireUserId(ClaimsPrincipal user)
    {
        var value = user.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParseExact(value, "D", out var userId)
            ? userId
            : throw new InvalidOperationException("Authenticated user identity is missing.");
    }

    private static IResult Failure(AgentControlException exception)
    {
        var status = exception.Code switch
        {
            "agent.not_found" => StatusCodes.Status404NotFound,
            "agent.enrollment_token_invalid" or "agent.enrollment_token_expired" =>
                StatusCodes.Status401Unauthorized,
            "agent.command_duplicate" => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status400BadRequest,
        };
        return Results.Json(
            new { code = exception.Code, detail = exception.Message },
            statusCode: status);
    }
}
