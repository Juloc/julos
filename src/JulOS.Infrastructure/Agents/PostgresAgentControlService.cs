using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

using JulOS.Application.Agents;
using JulOS.Application.Auditing;
using JulOS.Application.Concurrency;
using JulOS.Contracts.Agents;
using JulOS.Domain.Agents;
using JulOS.Domain.Observability;
using JulOS.Infrastructure.Persistence.Core;

using Microsoft.EntityFrameworkCore;

namespace JulOS.Infrastructure.Agents;

/// <summary>Authoritative PostgreSQL Agent identity, command and telemetry service.</summary>
internal sealed partial class PostgresAgentControlService : IAgentControlService
{
    private const int MaximumMetricsPerBatch = 1000;
    private const int MaximumMetricAgeDays = 30;
    private const int MaximumPayloadBytes = 64 * 1024;
    private static readonly HashSet<string> AllowedCommandTypes = new(StringComparer.Ordinal)
    {
        "diagnostics.snapshot",
        "service.restart",
        "system.update.prepare",
        "system.update.apply",
    };

    private readonly CoreDbContext context;
    private readonly IAuditService audit;
    private readonly TimeProvider timeProvider;

    public PostgresAgentControlService(
        CoreDbContext context,
        IAuditService audit,
        TimeProvider timeProvider)
    {
        this.context = context;
        this.audit = audit;
        this.timeProvider = timeProvider;
    }

    public async Task<AgentEnrollmentTokenResponse> CreateEnrollmentTokenAsync(
        Guid actorUserId,
        CreateAgentEnrollmentTokenRequest request,
        string correlationId,
        string? remoteAddress,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (actorUserId == Guid.Empty
            || string.IsNullOrWhiteSpace(request.Description)
            || request.Description != request.Description.Trim()
            || request.Description.Length > 256
            || request.LifetimeMinutes is < 1 or > 1440)
        {
            throw Failure("agent.enrollment_request_invalid", "Agent enrollment request is invalid.");
        }

        var raw = RandomNumberGenerator.GetBytes(48);
        try
        {
            var token = Base64Url(raw);
            var now = this.timeProvider.GetUtcNow();
            var row = new AgentEnrollmentTokenRow
            {
                Id = Guid.CreateVersion7(now),
                TokenHash = SHA256.HashData(raw),
                CreatedByUserId = actorUserId,
                Description = request.Description,
                CreatedAtUtc = now,
                ExpiresAtUtc = now.AddMinutes(request.LifetimeMinutes),
            };
            this.context.AgentEnrollmentTokens.Add(row);
            this.audit.Stage(new AuditRecord(
                actorUserId,
                AgentId: null,
                SourcePackageId: null,
                Action: "agent.enrollment_token.create",
                TargetType: "agent_enrollment_token",
                TargetId: row.Id.ToString("D"),
                AuditOutcome.Succeeded,
                correlationId,
                remoteAddress,
                "Agent enrollment token created.",
                "expiresAtUtc=" + row.ExpiresAtUtc.ToString("O", CultureInfo.InvariantCulture)));
            await this.context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return new AgentEnrollmentTokenResponse(row.Id, token, row.ExpiresAtUtc);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(raw);
        }
    }

    public async Task<AgentCredential> RedeemEnrollmentTokenAsync(
        RedeemAgentEnrollmentRequest request,
        string correlationId,
        string? remoteAddress,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateEnrollment(request);
        var tokenBytes = DecodeBase64Url(request.Token);
        var credentialBytes = DecodeEnrollmentCredential(request.Credential);
        try
        {
            var hash = SHA256.HashData(tokenBytes);
            await using var transaction = await this.context.Database
                .BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
                .ConfigureAwait(false);
            var now = this.timeProvider.GetUtcNow();
            var token = await this.context.AgentEnrollmentTokens
                .SingleOrDefaultAsync(candidate => candidate.TokenHash == hash, cancellationToken)
                .ConfigureAwait(false)
                ?? throw Failure("agent.enrollment_token_invalid", "Enrollment token is invalid.");
            if (token.RedeemedAtUtc is not null)
            {
                var retry = await this.ResolveEnrollmentRetryAsync(
                    token,
                    request,
                    credentialBytes,
                    cancellationToken).ConfigureAwait(false);
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                return retry;
            }
            if (token.ExpiresAtUtc <= now)
            {
                throw Failure("agent.enrollment_token_expired", "Enrollment token has expired.");
            }

            var existing = await this.context.Agents.SingleOrDefaultAsync(
                agent => agent.MachineIdentity == request.MachineIdentity,
                cancellationToken).ConfigureAwait(false);
            if (existing is not null && existing.State != AgentConnectionState.Revoked)
            {
                throw Failure("agent.machine_already_enrolled", "This machine identity is already enrolled.");
            }

            var agentId = Guid.CreateVersion7(now);
            var agent = new AgentRow
            {
                Id = agentId,
                Name = request.Name,
                MachineIdentity = request.MachineIdentity,
                OperatingSystem = request.OperatingSystem,
                Architecture = request.Architecture,
                Version = request.Version,
                State = AgentConnectionState.Enrolled,
                EnrolledAtUtc = now,
                Revision = 1,
            };
            var credential = new AgentCredentialRow
            {
                AgentId = agentId,
                CredentialHash = SHA256.HashData(credentialBytes),
                CreatedAtUtc = now,
                Revision = 1,
            };
            token.RedeemedAtUtc = now;
            token.RedeemedByAgentId = agentId;
            this.context.Agents.Add(agent);
            this.context.AgentCredentials.Add(credential);
            this.audit.Stage(new AuditRecord(
                UserId: null,
                AgentId: agentId,
                SourcePackageId: null,
                Action: "agent.enroll",
                TargetType: "agent",
                TargetId: agentId.ToString("D"),
                AuditOutcome.Succeeded,
                correlationId,
                remoteAddress,
                "Agent enrolled.",
                "tokenId=" + token.Id.ToString("D", CultureInfo.InvariantCulture)
                    + ";machineIdentityHash=" + HashLabel(request.MachineIdentity)));
            await this.context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return new AgentCredential(agentId, request.Credential, now);
        }
        catch (DbUpdateException exception)
        {
            throw Failure("agent.enrollment_conflict", "Agent enrollment conflicted with another request.", exception);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(tokenBytes);
            CryptographicOperations.ZeroMemory(credentialBytes);
        }
    }

    private async Task<AgentCredential> ResolveEnrollmentRetryAsync(
        AgentEnrollmentTokenRow token,
        RedeemAgentEnrollmentRequest request,
        byte[] credentialBytes,
        CancellationToken cancellationToken)
    {
        if (token.RedeemedByAgentId is not Guid agentId)
        {
            throw Failure("agent.enrollment_token_reused", "Enrollment token was already redeemed.");
        }

        var agent = await this.context.Agents.AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == agentId, cancellationToken)
            .ConfigureAwait(false);
        var credential = await this.context.AgentCredentials.AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.AgentId == agentId, cancellationToken)
            .ConfigureAwait(false);
        var submittedHash = SHA256.HashData(credentialBytes);
        try
        {
            var matches = agent is not null
                && credential is not null
                && agent.State != AgentConnectionState.Revoked
                && credential.RevokedAtUtc is null
                && string.Equals(agent.Name, request.Name, StringComparison.Ordinal)
                && string.Equals(agent.MachineIdentity, request.MachineIdentity, StringComparison.Ordinal)
                && string.Equals(agent.OperatingSystem, request.OperatingSystem, StringComparison.Ordinal)
                && string.Equals(agent.Architecture, request.Architecture, StringComparison.Ordinal)
                && string.Equals(agent.Version, request.Version, StringComparison.Ordinal)
                && submittedHash.Length == credential.CredentialHash.Length
                && CryptographicOperations.FixedTimeEquals(submittedHash, credential.CredentialHash);
            if (!matches)
            {
                throw Failure("agent.enrollment_token_reused", "Enrollment token was already redeemed.");
            }

            return new AgentCredential(agentId, request.Credential, agent!.EnrolledAtUtc);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(submittedHash);
        }
    }

    public async Task<bool> AuthenticateAsync(
        Guid agentId,
        ReadOnlyMemory<byte> credential,
        CancellationToken cancellationToken = default)
    {
        if (agentId == Guid.Empty || credential.IsEmpty)
        {
            return false;
        }
        var row = await this.context.AgentCredentials.AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.AgentId == agentId, cancellationToken)
            .ConfigureAwait(false);
        var agent = await this.context.Agents.AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == agentId, cancellationToken)
            .ConfigureAwait(false);
        if (row is null || agent is null || row.RevokedAtUtc is not null || agent.State == AgentConnectionState.Revoked)
        {
            return false;
        }
        var hash = SHA256.HashData(credential.Span);
        return hash.Length == row.CredentialHash.Length
            && CryptographicOperations.FixedTimeEquals(hash, row.CredentialHash);
    }

    public async Task<IReadOnlyList<AgentResponse>> ListAsync(
        CancellationToken cancellationToken = default) =>
        await this.context.Agents.AsNoTracking()
            .OrderBy(agent => agent.Name)
            .Select(agent => ToResponse(agent))
            .ToArrayAsync(cancellationToken).ConfigureAwait(false);

    public async Task<AgentResponse> ReadAsync(
        Guid agentId,
        CancellationToken cancellationToken = default) =>
        ToResponse(await RequireAgentAsync(agentId, cancellationToken).ConfigureAwait(false));

    public async Task<AgentResponse> RevokeAsync(
        Guid actorUserId,
        Guid agentId,
        int revision,
        string correlationId,
        string? remoteAddress,
        CancellationToken cancellationToken = default)
    {
        var agent = await RequireAgentAsync(agentId, cancellationToken).ConfigureAwait(false);
        if (agent.Revision != revision)
        {
            throw new ConcurrencyConflictException(agent.Revision, new InvalidOperationException("Agent changed concurrently."));
        }
        if (agent.State != AgentConnectionState.Revoked)
        {
            var now = this.timeProvider.GetUtcNow();
            agent.State = AgentConnectionState.Revoked;
            agent.RevokedAtUtc = now;
            agent.Revision = checked(agent.Revision + 1);
            var credential = await this.context.AgentCredentials
                .SingleAsync(candidate => candidate.AgentId == agentId, cancellationToken)
                .ConfigureAwait(false);
            credential.RevokedAtUtc = now;
            credential.Revision = checked(credential.Revision + 1);
            this.audit.Stage(new AuditRecord(
                actorUserId,
                agentId,
                SourcePackageId: null,
                Action: "agent.revoke",
                TargetType: "agent",
                TargetId: agentId.ToString("D"),
                AuditOutcome.Succeeded,
                correlationId,
                remoteAddress,
                "Agent revoked.",
                "Credential invalidated."));
            await this.context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        return ToResponse(agent);
    }

    public async Task<AgentResponse> RecordHeartbeatAsync(
        Guid agentId,
        AgentHeartbeatRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateVersion(request.Version);
        var agent = await RequireActiveAgentAsync(agentId, cancellationToken).ConfigureAwait(false);
        var now = this.timeProvider.GetUtcNow();
        if (request.ObservedAtUtc > now.AddMinutes(5) || request.ObservedAtUtc < now.AddHours(-24))
        {
            throw Failure("agent.heartbeat_time_invalid", "Agent heartbeat time is outside the accepted range.");
        }
        if (request.Capabilities.Count > 256)
        {
            throw Failure("agent.capabilities_too_many", "Agent reported too many capabilities.");
        }

        agent.Version = request.Version;
        agent.LastSeenAtUtc = now;
        agent.State = AgentConnectionState.Connected;
        agent.Revision = checked(agent.Revision + 1);
        foreach (var capability in request.Capabilities)
        {
            ValidateCapability(capability);
            var row = await this.context.AgentCapabilities.SingleOrDefaultAsync(
                candidate => candidate.AgentId == agentId && candidate.CapabilityName == capability.Name,
                cancellationToken).ConfigureAwait(false);
            var metadata = capability.Metadata.GetRawText();
            if (row is null)
            {
                this.context.AgentCapabilities.Add(new AgentCapabilityRow
                {
                    Id = Guid.CreateVersion7(now),
                    AgentId = agentId,
                    CapabilityName = capability.Name,
                    CapabilityVersion = capability.Version,
                    Enabled = capability.Enabled,
                    MetadataVersion = capability.MetadataVersion,
                    Metadata = metadata,
                    ObservedAtUtc = request.ObservedAtUtc,
                    Revision = 1,
                });
            }
            else
            {
                row.CapabilityVersion = capability.Version;
                row.Enabled = capability.Enabled;
                row.MetadataVersion = capability.MetadataVersion;
                row.Metadata = metadata;
                row.ObservedAtUtc = request.ObservedAtUtc;
                row.Revision = checked(row.Revision + 1);
            }
        }
        await this.context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return ToResponse(agent);
    }

    public async Task StoreMetricsAsync(
        Guid agentId,
        AgentMetricBatchRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        _ = await RequireActiveAgentAsync(agentId, cancellationToken).ConfigureAwait(false);
        if (request.Metrics.Count is 0 or > MaximumMetricsPerBatch)
        {
            throw Failure("agent.metrics_batch_invalid", "Agent metric batch size is invalid.");
        }
        var now = this.timeProvider.GetUtcNow();
        foreach (var metric in request.Metrics)
        {
            ValidateMetric(metric, now);
            this.context.AgentMetricSamples.Add(new AgentMetricSampleRow
            {
                Id = Guid.CreateVersion7(now),
                AgentId = agentId,
                MetricName = metric.Name,
                Value = metric.Value,
                Unit = metric.Unit,
                LabelsJson = JsonSerializer.Serialize(metric.Labels),
                ObservedAtUtc = metric.ObservedAtUtc,
                ReceivedAtUtc = now,
            });
        }
        var cutoff = now.AddDays(-MaximumMetricAgeDays);
        await this.context.AgentMetricSamples
            .Where(sample => sample.ObservedAtUtc < cutoff)
            .ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        await this.context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<AgentMetricSeriesResponse>> ReadMetricsAsync(
        Guid agentId,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        CancellationToken cancellationToken = default)
    {
        if (agentId == Guid.Empty || fromUtc >= toUtc || toUtc - fromUtc > TimeSpan.FromDays(MaximumMetricAgeDays))
        {
            throw Failure("agent.metrics_range_invalid", "Agent metric range is invalid.");
        }
        var rows = await this.context.AgentMetricSamples.AsNoTracking()
            .Where(sample => sample.AgentId == agentId
                && sample.ObservedAtUtc >= fromUtc
                && sample.ObservedAtUtc <= toUtc)
            .OrderBy(sample => sample.ObservedAtUtc)
            .ToArrayAsync(cancellationToken).ConfigureAwait(false);
        return rows.GroupBy(row => new { row.MetricName, row.Unit, row.LabelsJson })
            .Select(group => new AgentMetricSeriesResponse(
                agentId,
                group.Key.MetricName,
                group.Key.Unit,
                JsonSerializer.Deserialize<Dictionary<string, string>>(group.Key.LabelsJson) ?? [],
                group.Select(point => new AgentMetricPointResponse(point.ObservedAtUtc, point.Value)).ToArray()))
            .ToArray();
    }

    public async Task<AgentCommandResponse> CreateCommandAsync(
        Guid actorUserId,
        Guid agentId,
        CreateAgentCommandRequest request,
        string correlationId,
        string? remoteAddress,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        _ = await RequireActiveAgentAsync(agentId, cancellationToken).ConfigureAwait(false);
        ValidateCommand(request);
        var existing = await this.context.AgentCommands.AsNoTracking().SingleOrDefaultAsync(
            command => command.AgentId == agentId && command.OperationKey == request.OperationKey,
            cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            return ToResponse(existing);
        }

        await this.EnsureCommandAdvertisedAsync(
            agentId,
            request.CommandType,
            cancellationToken).ConfigureAwait(false);

        var now = this.timeProvider.GetUtcNow();
        var row = new AgentCommandRow
        {
            Id = Guid.CreateVersion7(now),
            AgentId = agentId,
            OperationKey = request.OperationKey,
            CommandType = request.CommandType,
            PayloadJson = request.Payload.GetRawText(),
            State = "queued",
            CreatedAtUtc = now,
            ExpiresAtUtc = now.AddSeconds(request.LifetimeSeconds),
            Revision = 1,
        };
        this.context.AgentCommands.Add(row);
        this.audit.Stage(new AuditRecord(
            actorUserId,
            agentId,
            SourcePackageId: null,
            Action: "agent.command.create",
            TargetType: "agent_command",
            TargetId: row.Id.ToString("D"),
            AuditOutcome.Succeeded,
            correlationId,
            remoteAddress,
            "Agent command queued.",
            "type=" + request.CommandType
                + ";expiresAtUtc=" + row.ExpiresAtUtc.ToString("O", CultureInfo.InvariantCulture)));
        await this.context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return ToResponse(row);
    }

    public async Task<AgentCommandResponse?> AcquireNextCommandAsync(
        Guid agentId,
        CancellationToken cancellationToken = default)
    {
        _ = await RequireActiveAgentAsync(agentId, cancellationToken).ConfigureAwait(false);
        await using var transaction = await this.context.Database
            .BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken).ConfigureAwait(false);
        var now = this.timeProvider.GetUtcNow();
        await this.context.AgentCommands
            .Where(command => command.AgentId == agentId
                && command.State == "queued"
                && command.ExpiresAtUtc <= now)
            .ExecuteUpdateAsync(update => update
                .SetProperty(command => command.State, "expired")
                .SetProperty(command => command.CompletedAtUtc, now)
                .SetProperty(command => command.Revision, command => command.Revision + 1),
                cancellationToken).ConfigureAwait(false);
        var row = await this.context.AgentCommands
            .Where(command => command.AgentId == agentId && command.State == "queued")
            .OrderBy(command => command.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        if (row is null)
        {
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return null;
        }
        row.State = "running";
        row.StartedAtUtc = now;
        row.Revision = checked(row.Revision + 1);
        await this.context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return ToResponse(row);
    }

    public async Task<AgentCommandResponse> CompleteCommandAsync(
        Guid agentId,
        Guid commandId,
        CompleteAgentCommandRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var row = await this.context.AgentCommands.SingleOrDefaultAsync(
            command => command.Id == commandId && command.AgentId == agentId,
            cancellationToken).ConfigureAwait(false)
            ?? throw Failure("agent.command_not_found", "Agent command does not exist.");
        if (row.State != "running")
        {
            throw Failure("agent.command_state_invalid", "Agent command is not running.");
        }
        if (row.Revision != request.Revision)
        {
            throw new ConcurrencyConflictException(row.Revision, new InvalidOperationException("Command changed concurrently."));
        }
        if (Encoding.UTF8.GetByteCount(request.Result.GetRawText()) > MaximumPayloadBytes
            || request.ErrorCode?.Length > 256)
        {
            throw Failure("agent.command_result_invalid", "Agent command result is invalid.");
        }
        row.State = request.Succeeded ? "succeeded" : "failed";
        row.ResultJson = request.Result.GetRawText();
        row.ErrorCode = request.Succeeded ? null : request.ErrorCode ?? "agent.command_failed";
        row.CompletedAtUtc = this.timeProvider.GetUtcNow();
        row.Revision = checked(row.Revision + 1);
        await this.context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return ToResponse(row);
    }

    private async Task<AgentRow> RequireAgentAsync(Guid agentId, CancellationToken cancellationToken) =>
        agentId == Guid.Empty
            ? throw Failure("agent.not_found", "Agent does not exist.")
            : await this.context.Agents.SingleOrDefaultAsync(
                candidate => candidate.Id == agentId,
                cancellationToken).ConfigureAwait(false)
                ?? throw Failure("agent.not_found", "Agent does not exist.");

    private async Task<AgentRow> RequireActiveAgentAsync(Guid agentId, CancellationToken cancellationToken)
    {
        var agent = await RequireAgentAsync(agentId, cancellationToken).ConfigureAwait(false);
        if (agent.State == AgentConnectionState.Revoked)
        {
            throw Failure("agent.revoked", "Agent is revoked.");
        }
        return agent;
    }

    private static AgentResponse ToResponse(AgentRow agent) => new(
        agent.Id,
        agent.Name,
        agent.MachineIdentity,
        agent.OperatingSystem,
        agent.Architecture,
        agent.Version,
        agent.State.ToString().ToLowerInvariant(),
        agent.EnrolledAtUtc,
        agent.LastSeenAtUtc,
        agent.RevokedAtUtc,
        agent.Revision);

    private static AgentCommandResponse ToResponse(AgentCommandRow command)
    {
        using var payload = JsonDocument.Parse(command.PayloadJson);
        JsonElement? result = null;
        if (command.ResultJson is not null)
        {
            using var resultDocument = JsonDocument.Parse(command.ResultJson);
            result = resultDocument.RootElement.Clone();
        }
        return new AgentCommandResponse(
            command.Id,
            command.AgentId,
            command.OperationKey,
            command.CommandType,
            payload.RootElement.Clone(),
            command.State,
            command.CreatedAtUtc,
            command.ExpiresAtUtc,
            command.StartedAtUtc,
            command.CompletedAtUtc,
            result,
            command.ErrorCode,
            command.Revision);
    }

    private static void ValidateEnrollment(RedeemAgentEnrollmentRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Token)
            || string.IsNullOrWhiteSpace(request.Credential)
            || !SafeName().IsMatch(request.Name)
            || !MachineIdentity().IsMatch(request.MachineIdentity)
            || !SafeValue().IsMatch(request.OperatingSystem)
            || !SafeValue().IsMatch(request.Architecture))
        {
            throw Failure("agent.enrollment_request_invalid", "Agent enrollment request is invalid.");
        }
        ValidateVersion(request.Version);
    }

    private static void ValidateVersion(string value)
    {
        if (!Version().IsMatch(value))
        {
            throw Failure("agent.version_invalid", "Agent version is invalid.");
        }
    }

    private static void ValidateCapability(AgentCapabilityContract capability)
    {
        if (!CapabilityName().IsMatch(capability.Name)
            || capability.Version < 1
            || capability.MetadataVersion < 1
            || Encoding.UTF8.GetByteCount(capability.Metadata.GetRawText()) > MaximumPayloadBytes)
        {
            throw Failure("agent.capability_invalid", "Agent capability is invalid.");
        }
    }

    private static void ValidateMetric(AgentMetricContract metric, DateTimeOffset now)
    {
        if (!MetricName().IsMatch(metric.Name)
            || !Unit().IsMatch(metric.Unit)
            || metric.Labels.Count > 32
            || metric.Labels.Any(pair => !Label().IsMatch(pair.Key) || pair.Value.Length > 256)
            || metric.ObservedAtUtc > now.AddMinutes(5)
            || metric.ObservedAtUtc < now.AddDays(-MaximumMetricAgeDays))
        {
            throw Failure("agent.metric_invalid", "Agent metric is invalid.");
        }
    }

    private static void ValidateCommand(CreateAgentCommandRequest request)
    {
        if (!OperationKey().IsMatch(request.OperationKey)
            || !AllowedCommandTypes.Contains(request.CommandType)
            || request.LifetimeSeconds is < 5 or > 3600
            || Encoding.UTF8.GetByteCount(request.Payload.GetRawText()) > MaximumPayloadBytes)
        {
            throw Failure("agent.command_invalid", "Agent command is invalid or not allowlisted.");
        }
    }

    private static byte[] DecodeBase64Url(string value)
    {
        try
        {
            var normalized = value.Replace('-', '+').Replace('_', '/');
            normalized += (normalized.Length % 4) switch
            {
                0 => string.Empty,
                2 => "==",
                3 => "=",
                _ => throw new FormatException(),
            };
            var bytes = Convert.FromBase64String(normalized);
            if (bytes.Length != 48)
            {
                throw new FormatException();
            }
            return bytes;
        }
        catch (FormatException exception)
        {
            throw Failure("agent.enrollment_token_invalid", "Enrollment token is invalid.", exception);
        }
    }

    private static byte[] DecodeEnrollmentCredential(string value)
    {
        try
        {
            var normalized = value.Replace('-', '+').Replace('_', '/');
            normalized += (normalized.Length % 4) switch
            {
                0 => string.Empty,
                2 => "==",
                3 => "=",
                _ => throw new FormatException(),
            };
            var bytes = Convert.FromBase64String(normalized);
            if (bytes.Length != 48)
            {
                CryptographicOperations.ZeroMemory(bytes);
                throw new FormatException();
            }

            return bytes;
        }
        catch (FormatException exception)
        {
            throw Failure(
                "agent.enrollment_credential_invalid",
                "Enrollment credential is invalid.",
                exception);
        }
    }

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes)
        .TrimEnd('=')
        .Replace('+', '-')
        .Replace('/', '_');

    private static string HashLabel(string value) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)))[..16];

    private static AgentControlException Failure(string code, string message, Exception? inner = null) =>
        new(code, message, inner);

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9 ._-]{0,127}$", RegexOptions.CultureInvariant)]
    private static partial Regex SafeName();

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._:-]{7,255}$", RegexOptions.CultureInvariant)]
    private static partial Regex MachineIdentity();

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9 ._()/+-]{0,127}$", RegexOptions.CultureInvariant)]
    private static partial Regex SafeValue();

    [GeneratedRegex("^[0-9]+\\.[0-9]+\\.[0-9]+(?:-[0-9A-Za-z.-]+)?$", RegexOptions.CultureInvariant)]
    private static partial Regex Version();

    [GeneratedRegex("^[a-z][a-z0-9._-]{0,127}$", RegexOptions.CultureInvariant)]
    private static partial Regex CapabilityName();

    [GeneratedRegex("^[a-z][a-z0-9._-]{0,127}$", RegexOptions.CultureInvariant)]
    private static partial Regex MetricName();

    [GeneratedRegex("^[A-Za-z%/0-9._-]{1,32}$", RegexOptions.CultureInvariant)]
    private static partial Regex Unit();

    [GeneratedRegex("^[a-z][a-z0-9._-]{0,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex Label();

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._:-]{0,255}$", RegexOptions.CultureInvariant)]
    private static partial Regex OperationKey();
}
