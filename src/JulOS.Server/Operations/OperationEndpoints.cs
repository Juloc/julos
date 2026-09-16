using System.Security.Claims;

using JulOS.Application.Operations;
using JulOS.Contracts.Operations;
using JulOS.Server.Authentication;
using JulOS.Server.Authorization;
using JulOS.Server.Errors;

using Microsoft.AspNetCore.Antiforgery;

namespace JulOS.Server.Operations;

/// <summary>Maps the durable operation-resource HTTP contract.</summary>
internal static class OperationEndpoints
{
    internal static IEndpointRouteBuilder MapJulOsOperations(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var group = endpoints.MapGroup("/api/v1/operations").WithTags("Operations");

        group.MapPost(string.Empty, CreateAsync)
            .RequireAuthorization(JulOsAuthorizationPolicies.OperationCreate)
            .RequireJulOsAntiforgery();
        group.MapGet(string.Empty, ListAsync)
            .RequireAuthorization(JulOsAuthorizationPolicies.OperationRead);
        group.MapGet("/{operationId:guid}", ReadAsync)
            .RequireAuthorization(JulOsAuthorizationPolicies.OperationRead);
        group.MapGet("/{operationId:guid}/events", ReadProgressAsync)
            .RequireAuthorization(JulOsAuthorizationPolicies.OperationRead);
        group.MapPost("/{operationId:guid}/cancellation", CancelAsync)
            .RequireAuthorization(JulOsAuthorizationPolicies.OperationCancel)
            .RequireJulOsAntiforgery();

        return endpoints;
    }

    private static async Task<IResult> CreateAsync(
        HttpContext context,
        CreateOperationRequest request,
        IAntiforgery antiforgery,
        IOperationService operations,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        await JulOsAntiforgery.ValidateAsync(context, antiforgery).ConfigureAwait(false);

        var operation = await operations.CreateAsync(
            new CreateOperationCommand(
                CurrentUserId(context.User),
                request.OperationType,
                request.SourcePackageId,
                request.TargetReference,
                request.IdempotencyKey,
                CorrelationId.Get(context)),
            cancellationToken).ConfigureAwait(false);

        return TypedResults.Accepted(
            $"/api/v1/operations/{operation.OperationId:D}",
            ToResponse(operation));
    }

    private static async Task<IResult> ListAsync(
        HttpContext context,
        string? states,
        string? sourcePackageId,
        DateTimeOffset? createdAfterUtc,
        string? cursor,
        int? limit,
        IOperationService operations,
        CancellationToken cancellationToken)
    {
        var page = await operations.ListAsync(
            new OperationQuery(
                // Always the authenticated user. A global read permission does not turn
                // this into a cross-user administrative API.
                CurrentUserId(context.User),
                ParseStates(states),
                string.IsNullOrWhiteSpace(sourcePackageId) ? null : sourcePackageId,
                createdAfterUtc,
                cursor,
                limit),
            cancellationToken).ConfigureAwait(false);

        return TypedResults.Ok(new OperationPageResponse(
            page.Items.Select(ToResponse).ToArray(),
            page.NextCursor));
    }

    /// <summary>Reads the optional state filter, refusing a state that does not exist.</summary>
    private static List<OperationState>? ParseStates(string? states)
    {
        if (string.IsNullOrWhiteSpace(states))
        {
            return null;
        }

        var parsed = new List<OperationState>();
        foreach (var name in states.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            parsed.Add(name switch
            {
                "queued" => OperationState.Queued,
                "running" => OperationState.Running,
                "succeeded" => OperationState.Succeeded,
                "failed" => OperationState.Failed,
                "cancelled" => OperationState.Cancelled,
                _ => throw new OperationFailureException(OperationFailureReason.Invalid),
            });
        }

        return parsed;
    }

    private static async Task<IResult> ReadAsync(
        HttpContext context,
        Guid operationId,
        IOperationService operations,
        CancellationToken cancellationToken)
    {
        var operation = await operations
            .ReadAsync(operationId, CurrentUserId(context.User), cancellationToken)
            .ConfigureAwait(false);
        return TypedResults.Ok(ToResponse(operation));
    }

    private static async Task<IResult> ReadProgressAsync(
        HttpContext context,
        Guid operationId,
        IOperationService operations,
        CancellationToken cancellationToken)
    {
        var events = await operations
            .ReadProgressAsync(operationId, CurrentUserId(context.User), cancellationToken)
            .ConfigureAwait(false);
        return TypedResults.Ok(events.Select(ToResponse).ToArray());
    }

    private static async Task<IResult> CancelAsync(
        HttpContext context,
        Guid operationId,
        IAntiforgery antiforgery,
        IOperationService operations,
        CancellationToken cancellationToken)
    {
        await JulOsAntiforgery.ValidateAsync(context, antiforgery).ConfigureAwait(false);
        var operation = await operations
            .RequestCancellationAsync(operationId, CurrentUserId(context.User), cancellationToken)
            .ConfigureAwait(false);
        var response = ToResponse(operation);

        return operation.State == OperationState.Cancelled
            ? TypedResults.Ok(response)
            : TypedResults.Accepted($"/api/v1/operations/{operation.OperationId:D}", response);
    }

    /// <summary>The public shape of one operation, shared with endpoints that start one.</summary>
    internal static OperationResponse ToResponse(OperationSnapshot operation) => new(
        operation.OperationId,
        operation.OperationType,
        operation.OwnerUserId,
        operation.SourcePackageId,
        operation.TargetReference,
        StateName(operation.State),
        operation.ProgressPercent,
        operation.CurrentStep,
        operation.CreatedAtUtc,
        operation.StartedAtUtc,
        operation.CompletedAtUtc,
        operation.FailureCode,
        operation.FailureDetail,
        operation.CorrelationId,
        operation.CancellationRequestedAtUtc is not null,
        operation.Revision);

    private static OperationProgressEventResponse ToResponse(OperationProgressSnapshot progress) => new(
        progress.EventId,
        progress.OperationId,
        progress.ProgressPercent,
        progress.CurrentStep,
        progress.OccurredAtUtc);

    private static string StateName(OperationState state) => state switch
    {
        OperationState.Queued => OperationStates.Queued,
        OperationState.Running => OperationStates.Running,
        OperationState.Succeeded => OperationStates.Succeeded,
        OperationState.Failed => OperationStates.Failed,
        OperationState.Cancelled => OperationStates.Cancelled,
        _ => throw new InvalidOperationException("Unknown operation state."),
    };

    private static Guid CurrentUserId(ClaimsPrincipal principal)
    {
        var identifier = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(identifier, out var userId) && userId != Guid.Empty
            ? userId
            : throw new OperationFailureException(OperationFailureReason.NotFound);
    }
}
