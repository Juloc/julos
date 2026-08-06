namespace JulOS.Infrastructure.Persistence.Core;

/// <summary>Relational storage shape for durable protocol-neutral Remote sessions.</summary>
internal sealed class RemoteSessionRow
{
    internal Guid Id { get; set; }

    internal Guid OwnerUserId { get; set; }

    internal required string CallerPackageId { get; set; }

    internal required string OperationKey { get; set; }

    internal required string RequestIdentity { get; set; }

    internal required string Protocol { get; set; }

    internal required string TargetHost { get; set; }

    internal int TargetPort { get; set; }

    internal Guid SecretReferenceId { get; set; }

    internal Guid? ProfileId { get; set; }

    internal Guid? NetworkProfileId { get; set; }

    internal int ViewportWidth { get; set; }

    internal int ViewportHeight { get; set; }

    internal decimal DeviceScaleFactor { get; set; }

    internal int IdleTimeoutSeconds { get; set; }

    internal int MaximumSessionSeconds { get; set; }

    internal required string State { get; set; }

    internal DateTimeOffset CreatedAtUtc { get; set; }

    internal DateTimeOffset UpdatedAtUtc { get; set; }

    internal DateTimeOffset LastActivityAtUtc { get; set; }

    internal DateTimeOffset ExpiresAtUtc { get; set; }

    internal DateTimeOffset? ConnectedAtUtc { get; set; }

    internal DateTimeOffset? EndedAtUtc { get; set; }

    internal string? RuntimeId { get; set; }

    internal string? DisplayKind { get; set; }

    internal string? DisplayContractVersion { get; set; }

    internal string? DisplayEndpoint { get; set; }

    internal DateTimeOffset? DisplayExpiresAtUtc { get; set; }

    internal string? FailureCode { get; set; }

    internal string? FailureDetail { get; set; }

    internal bool? FailureRetryable { get; set; }

    internal string? CancellationOperationKey { get; set; }

    internal string? CancellationReason { get; set; }

    internal int Revision { get; set; }
}
