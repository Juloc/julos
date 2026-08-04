using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

using JulOS.Contracts.Remote;

namespace JulOS.Application.Remote;

/// <summary>Validates protocol-neutral Remote session contracts before provider selection.</summary>
public sealed partial class RemoteSessionContractValidator
{
    private const int MinimumViewportWidth = 320;
    private const int MaximumViewportWidth = 7680;
    private const int MinimumViewportHeight = 240;
    private const int MaximumViewportHeight = 4320;
    private const int MinimumIdleTimeoutSeconds = 60;
    private const int MaximumIdleTimeoutSeconds = 86400;
    private const int MinimumSessionSeconds = 300;
    private const int MaximumSessionSeconds = 604800;
    private const int MaximumRequestLifetimeSeconds = 600;
    private static readonly JsonSerializerOptions IdentityJsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly Dictionary<string, IReadOnlySet<string>> AllowedTransitions =
        new(StringComparer.Ordinal)
        {
            [RemoteSessionStates.Requested] = Set(
                RemoteSessionStates.Provisioning,
                RemoteSessionStates.Cancelled,
                RemoteSessionStates.Expired,
                RemoteSessionStates.Failed),
            [RemoteSessionStates.Provisioning] = Set(
                RemoteSessionStates.Connecting,
                RemoteSessionStates.Cancelled,
                RemoteSessionStates.Expired,
                RemoteSessionStates.Failed),
            [RemoteSessionStates.Connecting] = Set(
                RemoteSessionStates.Connected,
                RemoteSessionStates.Disconnecting,
                RemoteSessionStates.Cancelled,
                RemoteSessionStates.Expired,
                RemoteSessionStates.Failed),
            [RemoteSessionStates.Connected] = Set(
                RemoteSessionStates.Disconnecting,
                RemoteSessionStates.Disconnected,
                RemoteSessionStates.Expired,
                RemoteSessionStates.Failed),
            [RemoteSessionStates.Disconnecting] = Set(
                RemoteSessionStates.Disconnected,
                RemoteSessionStates.Cancelled,
                RemoteSessionStates.Expired,
                RemoteSessionStates.Failed),
        };

    private readonly TimeProvider timeProvider;

    /// <summary>Creates a Remote contract validator.</summary>
    /// <param name="timeProvider">Authoritative clock used for request deadline validation.</param>
    public RemoteSessionContractValidator(TimeProvider timeProvider)
    {
        this.timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    /// <summary>Validates and normalizes one create request.</summary>
    /// <param name="request">Create request.</param>
    /// <returns>A normalized request suitable for canonical identity calculation.</returns>
    /// <exception cref="RemoteSessionContractException">The request is unsafe or invalid.</exception>
    public CreateRemoteSessionRequest ValidateCreate(CreateRemoteSessionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateOperationKey(request.OperationKey);
        if (!RemoteProtocolIds.IsSupported(request.Protocol))
        {
            throw Failure(
                RemoteSessionFailureCodes.ProtocolUnsupported,
                "The requested Remote protocol is unsupported.");
        }

        var target = ValidateTarget(request.Target);
        if (request.SecretReferenceId == Guid.Empty)
        {
            throw Failure(
                RemoteSessionFailureCodes.CredentialUnavailable,
                "A valid secret reference is required.");
        }
        ValidateOptionalReference(request.ProfileId, "remote.profile_invalid", "Remote profile identity is invalid.");
        ValidateOptionalReference(
            request.NetworkProfileId,
            RemoteSessionFailureCodes.NetworkProfileUnavailable,
            "Network profile identity is invalid.");
        var viewport = ValidateViewport(request.Viewport);
        if (request.IdleTimeoutSeconds is < MinimumIdleTimeoutSeconds or > MaximumIdleTimeoutSeconds)
        {
            throw Failure(
                "remote.idle_timeout_invalid",
                $"Idle timeout must be from {MinimumIdleTimeoutSeconds} through {MaximumIdleTimeoutSeconds} seconds.");
        }
        if (request.MaximumSessionSeconds is < MinimumSessionSeconds or > MaximumSessionSeconds)
        {
            throw Failure(
                "remote.maximum_duration_invalid",
                $"Maximum session duration must be from {MinimumSessionSeconds} through {MaximumSessionSeconds} seconds.");
        }
        if (request.IdleTimeoutSeconds > request.MaximumSessionSeconds)
        {
            throw Failure(
                "remote.timeout_order_invalid",
                "Idle timeout cannot exceed maximum session duration.");
        }

        var now = this.timeProvider.GetUtcNow();
        if (request.RequestedAtUtc > now.AddMinutes(1)
            || request.RequestedAtUtc < now.AddMinutes(-10))
        {
            throw Failure(
                "remote.request_timestamp_invalid",
                "Remote request timestamp is outside the accepted clock-skew window.");
        }
        if (request.DeadlineUtc <= now)
        {
            throw Failure(
                RemoteSessionFailureCodes.RequestExpired,
                "The Remote request deadline has expired.");
        }
        if (request.DeadlineUtc <= request.RequestedAtUtc
            || request.DeadlineUtc > request.RequestedAtUtc.AddSeconds(MaximumRequestLifetimeSeconds))
        {
            throw Failure(
                "remote.request_deadline_invalid",
                $"Remote request deadline must be after the request and within {MaximumRequestLifetimeSeconds} seconds.");
        }

        return request with
        {
            Protocol = request.Protocol.ToLowerInvariant(),
            Target = target,
            Viewport = viewport,
        };
    }

    /// <summary>Validates one read request.</summary>
    /// <param name="request">Read request.</param>
    /// <exception cref="RemoteSessionContractException">The session identity is invalid.</exception>
    public static void ValidateRead(ReadRemoteSessionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateSessionId(request.SessionId);
    }

    /// <summary>Validates and normalizes one list request.</summary>
    /// <param name="request">List request.</param>
    /// <returns>A normalized bounded list request.</returns>
    /// <exception cref="RemoteSessionContractException">The list filter is invalid.</exception>
    public static ListRemoteSessionsRequest ValidateList(ListRemoteSessionsRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.States);
        if (request.Limit is < 1 or > 200)
        {
            throw Failure("remote.list_limit_invalid", "Remote session list limit must be from 1 through 200.");
        }
        if (request.States.Count > 9)
        {
            throw Failure("remote.list_states_invalid", "Remote session state filter is too large.");
        }

        var states = request.States
            .Select(ValidateState)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var cursor = request.Cursor;
        if (cursor is not null)
        {
            ValidateBoundedText(cursor, 512, "remote.cursor_invalid", "Remote session cursor is invalid.");
            if (!Base64UrlPattern().IsMatch(cursor))
            {
                throw Failure("remote.cursor_invalid", "Remote session cursor is invalid.");
            }
        }

        return request with { States = states };
    }

    /// <summary>Validates one cancellation request.</summary>
    /// <param name="request">Cancellation request.</param>
    /// <returns>A normalized cancellation request.</returns>
    /// <exception cref="RemoteSessionContractException">The cancellation request is invalid.</exception>
    public static CancelRemoteSessionRequest ValidateCancel(CancelRemoteSessionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateSessionId(request.SessionId);
        ValidateOperationKey(request.OperationKey);
        if (request.ExpectedRevision < 1)
        {
            throw Failure("remote.revision_invalid", "Remote session revision must be positive.");
        }

        var reason = request.Reason;
        if (reason is not null)
        {
            reason = reason.Trim();
            ValidateBoundedText(
                reason,
                256,
                "remote.cancel_reason_invalid",
                "Remote cancellation reason is invalid.");
        }
        return request with { Reason = reason };
    }

    /// <summary>Validates one lifecycle transition.</summary>
    /// <param name="currentState">Current state.</param>
    /// <param name="nextState">Requested next state.</param>
    /// <exception cref="RemoteSessionContractException">The transition is invalid.</exception>
    public static void ValidateTransition(string currentState, string nextState)
    {
        currentState = ValidateState(currentState);
        nextState = ValidateState(nextState);
        if (!AllowedTransitions.TryGetValue(currentState, out var allowed)
            || !allowed.Contains(nextState))
        {
            throw Failure(
                RemoteSessionFailureCodes.StateTransitionInvalid,
                $"Remote session cannot transition from '{currentState}' to '{nextState}'.");
        }
    }

    /// <summary>Computes the canonical exact-idempotency identity for a validated create request.</summary>
    /// <param name="request">Validated and normalized create request.</param>
    /// <returns>Lowercase SHA-256 identity.</returns>
    public static string ComputeRequestIdentity(CreateRemoteSessionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(request, IdentityJsonOptions);
        return Convert.ToHexStringLower(SHA256.HashData(bytes));
    }

    private static RemoteTargetContract ValidateTarget(RemoteTargetContract target)
    {
        ArgumentNullException.ThrowIfNull(target);
        var host = target.Host.Trim().ToLowerInvariant();
        ValidateBoundedText(host, 253, RemoteSessionFailureCodes.TargetInvalid, "Remote target host is invalid.");
        var isIpAddress = IPAddress.TryParse(host, out _);
        if (host.Contains("//", StringComparison.Ordinal)
            || host.Contains('/')
            || host.Contains('\\')
            || host.Contains('@')
            || host.Contains('?')
            || host.Contains('#')
            || host.Contains(':') && !isIpAddress)
        {
            throw Failure(RemoteSessionFailureCodes.TargetInvalid, "Remote target host is invalid.");
        }
        if (!isIpAddress
            && Uri.CheckHostName(host) != UriHostNameType.Dns)
        {
            throw Failure(RemoteSessionFailureCodes.TargetInvalid, "Remote target host is invalid.");
        }
        if (target.Port is < 1 or > 65535)
        {
            throw Failure(RemoteSessionFailureCodes.TargetInvalid, "Remote target port is invalid.");
        }
        return target with { Host = host };
    }

    private static RemoteViewportContract ValidateViewport(RemoteViewportContract viewport)
    {
        ArgumentNullException.ThrowIfNull(viewport);
        if (viewport.Width is < MinimumViewportWidth or > MaximumViewportWidth
            || viewport.Height is < MinimumViewportHeight or > MaximumViewportHeight
            || viewport.DeviceScaleFactor is < 0.5m or > 4m)
        {
            throw Failure("remote.viewport_invalid", "Remote viewport is outside the supported bounds.");
        }
        return viewport;
    }

    private static string ValidateState(string state)
    {
        ValidateBoundedText(state, 32, "remote.state_invalid", "Remote session state is invalid.");
        return state switch
        {
            RemoteSessionStates.Requested => state,
            RemoteSessionStates.Provisioning => state,
            RemoteSessionStates.Connecting => state,
            RemoteSessionStates.Connected => state,
            RemoteSessionStates.Disconnecting => state,
            RemoteSessionStates.Disconnected => state,
            RemoteSessionStates.Cancelled => state,
            RemoteSessionStates.Expired => state,
            RemoteSessionStates.Failed => state,
            _ => throw Failure("remote.state_invalid", "Remote session state is invalid."),
        };
    }

    private static void ValidateSessionId(Guid sessionId)
    {
        if (sessionId == Guid.Empty)
        {
            throw Failure("remote.session_id_invalid", "Remote session identity is invalid.");
        }
    }

    private static void ValidateOperationKey(string operationKey)
    {
        ValidateBoundedText(
            operationKey,
            128,
            "remote.operation_key_invalid",
            "Remote operation key is invalid.");
        if (!OperationKeyPattern().IsMatch(operationKey))
        {
            throw Failure("remote.operation_key_invalid", "Remote operation key is invalid.");
        }
    }

    private static void ValidateOptionalReference(Guid? value, string code, string detail)
    {
        if (value == Guid.Empty)
        {
            throw Failure(code, detail);
        }
    }

    private static void ValidateBoundedText(string value, int maximumLength, string code, string detail)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value != value.Trim()
            || value.Length > maximumLength
            || value.Any(char.IsControl))
        {
            throw Failure(code, detail);
        }
    }

    private static HashSet<string> Set(params string[] states) =>
        new(states, StringComparer.Ordinal);

    private static RemoteSessionContractException Failure(string code, string detail) =>
        new(code, detail);

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._:-]{0,127}$", RegexOptions.CultureInvariant)]
    private static partial Regex OperationKeyPattern();

    [GeneratedRegex("^[A-Za-z0-9_-]{1,512}$", RegexOptions.CultureInvariant)]
    private static partial Regex Base64UrlPattern();
}

/// <summary>Stable caller-safe Remote contract validation failure.</summary>
public sealed class RemoteSessionContractException : Exception
{
    /// <summary>Creates a Remote contract validation failure.</summary>
    /// <param name="code">Stable machine-readable failure code.</param>
    /// <param name="message">Caller-safe detail.</param>
    public RemoteSessionContractException(string code, string message)
        : base(message)
    {
        this.Code = code;
    }

    /// <summary>Gets the stable machine-readable failure code.</summary>
    public string Code { get; }
}
