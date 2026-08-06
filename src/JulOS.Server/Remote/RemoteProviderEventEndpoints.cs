using JulOS.Application.Remote;
using JulOS.Contracts.Remote;
using JulOS.Infrastructure.Remote;

namespace JulOS.Server.Remote;

internal static class RemoteProviderEventEndpoints
{
    internal static IEndpointRouteBuilder MapJulOsRemoteProviderEvents(
        this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        endpoints.MapPost(
                "/api/v1/internal/remote/provider-events",
                ApplyAsync)
            .WithTags("Remote")
            .AllowAnonymous();
        return endpoints;
    }

    private static async Task<IResult> ApplyAsync(
        HttpContext context,
        RemoteProviderEventRequest request,
        RemoteProviderCallbackAuthenticator authenticator,
        IRemoteSessionConnectionService connections,
        CancellationToken cancellationToken)
    {
        var token = context.Request.Headers[RemoteProviderEventContract.TokenHeader].ToString();
        if (!authenticator.Authenticate(request.SessionId, request.RuntimeId, token))
        {
            return Results.Json(
                new
                {
                    code = "remote.provider_authentication_required",
                    detail = "Remote provider authentication failed.",
                },
                statusCode: StatusCodes.Status401Unauthorized);
        }

        try
        {
            return request.Event switch
            {
                RemoteProviderEventContract.Connected => await ConnectAsync(
                    request,
                    connections,
                    cancellationToken).ConfigureAwait(false),
                RemoteProviderEventContract.Failed => await FailAsync(
                    request,
                    connections,
                    cancellationToken).ConfigureAwait(false),
                RemoteProviderEventContract.Activity => await RecordActivityAsync(
                    request,
                    connections,
                    cancellationToken).ConfigureAwait(false),
                _ => InvalidRequest("Remote provider event is unsupported."),
            };
        }
        catch (RemoteSessionContractException exception)
        {
            return InvalidRequest(exception.Message, exception.Code);
        }
        catch (RemoteSessionServiceException exception)
        {
            return SessionFailure(exception);
        }
    }

    private static async Task<IResult> ConnectAsync(
        RemoteProviderEventRequest request,
        IRemoteSessionConnectionService connections,
        CancellationToken cancellationToken)
    {
        if (request.ExpectedRevision < 1
            || request.FailureCode is not null
            || request.FailureDetail is not null
            || request.Retryable)
        {
            return InvalidRequest("Remote connected event payload is invalid.");
        }
        var response = await connections.ConnectAsync(
            new ConnectRemoteSessionCommand(
                request.SessionId,
                request.RuntimeId,
                request.ExpectedRevision),
            cancellationToken).ConfigureAwait(false);
        return Results.Ok(response);
    }

    private static async Task<IResult> FailAsync(
        RemoteProviderEventRequest request,
        IRemoteSessionConnectionService connections,
        CancellationToken cancellationToken)
    {
        if (request.ExpectedRevision < 1
            || string.IsNullOrWhiteSpace(request.FailureCode)
            || string.IsNullOrWhiteSpace(request.FailureDetail))
        {
            return InvalidRequest("Remote failed event payload is invalid.");
        }
        var response = await connections.FailAsync(
            new FailRemoteSessionCommand(
                request.SessionId,
                request.RuntimeId,
                request.ExpectedRevision,
                request.FailureCode,
                request.FailureDetail,
                request.Retryable),
            cancellationToken).ConfigureAwait(false);
        return Results.Ok(response);
    }

    private static async Task<IResult> RecordActivityAsync(
        RemoteProviderEventRequest request,
        IRemoteSessionConnectionService connections,
        CancellationToken cancellationToken)
    {
        if (request.ExpectedRevision != 0
            || request.FailureCode is not null
            || request.FailureDetail is not null
            || request.Retryable)
        {
            return InvalidRequest("Remote activity event payload is invalid.");
        }
        await connections.RecordActivityAsync(
            new RecordRemoteSessionActivityCommand(request.SessionId, request.RuntimeId),
            cancellationToken).ConfigureAwait(false);
        return Results.NoContent();
    }

    private static IResult InvalidRequest(
        string detail,
        string code = "remote.provider_event_invalid") =>
        Results.Json(new { code, detail }, statusCode: StatusCodes.Status400BadRequest);

    private static IResult SessionFailure(RemoteSessionServiceException exception)
    {
        var status = exception.Reason switch
        {
            RemoteSessionServiceFailureReason.NotFound => StatusCodes.Status404NotFound,
            RemoteSessionServiceFailureReason.ConcurrencyConflict => StatusCodes.Status409Conflict,
            RemoteSessionServiceFailureReason.InvalidTransition => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status400BadRequest,
        };
        return Results.Json(
            new
            {
                code = $"remote.{exception.Reason.ToString().ToLowerInvariant()}",
                detail = exception.Message,
            },
            statusCode: status);
    }
}
