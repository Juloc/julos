namespace JulOS.Infrastructure.Persistence.Core;

internal sealed class AgentEnrollmentTokenRow
{
    internal Guid Id { get; set; }

    internal required byte[] TokenHash { get; set; }

    internal Guid CreatedByUserId { get; set; }

    internal required string Description { get; set; }

    internal DateTimeOffset CreatedAtUtc { get; set; }

    internal DateTimeOffset ExpiresAtUtc { get; set; }

    internal DateTimeOffset? RedeemedAtUtc { get; set; }

    internal Guid? RedeemedByAgentId { get; set; }
}

internal sealed class AgentCredentialRow
{
    internal Guid AgentId { get; set; }

    internal required byte[] CredentialHash { get; set; }

    internal DateTimeOffset CreatedAtUtc { get; set; }

    internal DateTimeOffset? RotatedAtUtc { get; set; }

    internal DateTimeOffset? RevokedAtUtc { get; set; }

    internal int Revision { get; set; }
}

internal sealed class AgentCommandRow
{
    internal Guid Id { get; set; }

    internal Guid AgentId { get; set; }

    internal required string OperationKey { get; set; }

    internal required string CommandType { get; set; }

    internal required string PayloadJson { get; set; }

    internal required string State { get; set; }

    internal DateTimeOffset CreatedAtUtc { get; set; }

    internal DateTimeOffset ExpiresAtUtc { get; set; }

    internal DateTimeOffset? StartedAtUtc { get; set; }

    internal DateTimeOffset? CompletedAtUtc { get; set; }

    internal string? ResultJson { get; set; }

    internal string? ErrorCode { get; set; }

    internal int Revision { get; set; }
}

internal sealed class AgentMetricSampleRow
{
    internal Guid Id { get; set; }

    internal Guid AgentId { get; set; }

    internal required string MetricName { get; set; }

    internal double? Value { get; set; }

    internal required string Unit { get; set; }

    internal required string LabelsJson { get; set; }

    internal DateTimeOffset ObservedAtUtc { get; set; }

    internal DateTimeOffset ReceivedAtUtc { get; set; }
}
