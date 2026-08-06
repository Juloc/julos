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

    private static OperationResponse ToResponse(OperationSnapshot operation) => new(
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
