using JulOS.Application.Auditing;
using JulOS.Contracts.Auditing;
using JulOS.Domain.Observability;
using JulOS.Server.Authorization;

namespace JulOS.Server.Auditing;

/// <summary>Maps the protected immutable audit-event query surface.</summary>
internal static class AuditEndpoints
{
    private const int DefaultPageSize = 50;

    internal static IEndpointRouteBuilder MapJulOsAudit(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapGet("/api/v1/audit-events", QueryAsync)
            .WithTags("Audit")
            .RequireAuthorization(JulOsAuthorizationPolicies.AuthorizationRead);

        return endpoints;
    }

    private static async Task<IResult> QueryAsync(
        int? limit,
        string? cursor,
        DateTimeOffset? fromUtc,
        DateTimeOffset? toUtc,
        Guid? userId,
        Guid? agentId,
        string? sourcePackageId,
        string? action,
        string? targetType,
        string? targetId,
        string? outcome,
        IAuditService auditService,
        CancellationToken cancellationToken)
    {
        var page = await auditService.QueryAsync(
            new AuditQuery(
                limit ?? DefaultPageSize,
                cursor,
                fromUtc,
                toUtc,
                userId,
                agentId,
                sourcePackageId,
                action,
                targetType,
                targetId,
                ParseOutcome(outcome)),
            cancellationToken).ConfigureAwait(false);

        return TypedResults.Ok(new AuditEventPageResponse(
            page.Events.Select(ToResponse).ToArray(),
            page.NextCursor));
    }

    private static AuditEventResponse ToResponse(AuditEventSnapshot auditEvent) => new(
        auditEvent.AuditEventId,
        auditEvent.OccurredAtUtc,
        auditEvent.UserId,
        auditEvent.AgentId,
        auditEvent.SourcePackageId,
        auditEvent.Action,
        auditEvent.TargetType,
        auditEvent.TargetId,
        OutcomeName(auditEvent.Outcome),
        auditEvent.CorrelationId,
        auditEvent.RemoteAddress,
        auditEvent.Summary,
        auditEvent.SafeDetails);

    private static AuditOutcome? ParseOutcome(string? value) => value switch
    {
        null => null,
        AuditOutcomeNames.Succeeded => AuditOutcome.Succeeded,
        AuditOutcomeNames.Failed => AuditOutcome.Failed,
        AuditOutcomeNames.Denied => AuditOutcome.Denied,
        _ => throw new ArgumentException("The audit outcome filter is invalid.", nameof(value)),
    };

    private static string OutcomeName(AuditOutcome value) => value switch
    {
        AuditOutcome.Succeeded => AuditOutcomeNames.Succeeded,
        AuditOutcome.Failed => AuditOutcomeNames.Failed,
        AuditOutcome.Denied => AuditOutcomeNames.Denied,
        _ => throw new InvalidOperationException("Unknown audit outcome."),
    };
}
