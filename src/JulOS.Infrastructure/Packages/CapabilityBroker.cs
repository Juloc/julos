using JulOS.Application.Auditing;
using JulOS.Domain.Observability;
using JulOS.PackageSdk;

namespace JulOS.Infrastructure.Packages;

/// <summary>Resolves capability providers without exposing package implementation references.</summary>
public sealed class CapabilityBroker : ICapabilityClient
{
    private readonly Dictionary<string, List<ICapabilityProvider>> providers = new(StringComparer.Ordinal);
    private readonly Dictionary<string, HashSet<string>> grants = new(StringComparer.Ordinal);
    private readonly IAuditService audit;
    private readonly TimeProvider timeProvider;
    private readonly object sync = new();

    /// <summary>Creates a capability broker with audit and deadline dependencies.</summary>
    /// <param name="audit">Append-only audit service.</param>
    /// <param name="timeProvider">Authoritative clock.</param>
    public CapabilityBroker(IAuditService audit, TimeProvider timeProvider)
    {
        this.audit = audit ?? throw new ArgumentNullException(nameof(audit));
        this.timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    /// <summary>Replaces the capabilities one package is allowed to invoke.</summary>
    /// <param name="packageId">Caller package identity.</param>
    /// <param name="capabilityNames">Explicitly granted capability names.</param>
    public void SetPackageGrants(string packageId, IEnumerable<string> capabilityNames)
    {
        ValidatePackageId(packageId);
        ArgumentNullException.ThrowIfNull(capabilityNames);
        lock (this.sync)
        {
            this.grants[packageId] = capabilityNames.ToHashSet(StringComparer.Ordinal);
        }
    }

    /// <summary>Registers or replaces one provider owned by the calling package.</summary>
    /// <param name="actorPackageId">Package performing the registration.</param>
    /// <param name="provider">Capability provider implementation.</param>
    public void Register(string actorPackageId, ICapabilityProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ValidatePackageId(actorPackageId);
        var descriptor = provider.Descriptor;
        if (!string.Equals(actorPackageId, descriptor.ProviderPackageId, StringComparison.Ordinal))
        {
            throw new CapabilityBrokerException(
                "capability.provider_identity_mismatch",
                "A package can only register itself as a capability provider.");
        }
        ValidateDescriptor(descriptor);
        var key = Identity(descriptor.CapabilityName, descriptor.ContractVersion);
        lock (this.sync)
        {
            if (!this.providers.TryGetValue(key, out var list))
            {
                list = [];
                this.providers.Add(key, list);
            }
            list.RemoveAll(candidate => string.Equals(
                candidate.Descriptor.ProviderPackageId,
                actorPackageId,
                StringComparison.Ordinal));
            list.Add(provider);
            list.Sort((left, right) => right.Descriptor.Priority.CompareTo(left.Descriptor.Priority));
        }
    }

    /// <summary>Removes all providers and invocation grants owned by one package.</summary>
    /// <param name="packageId">Package identity.</param>
    public void UnregisterPackage(string packageId)
    {
        ValidatePackageId(packageId);
        lock (this.sync)
        {
            foreach (var list in this.providers.Values)
            {
                list.RemoveAll(provider => string.Equals(
                    provider.Descriptor.ProviderPackageId,
                    packageId,
                    StringComparison.Ordinal));
            }
            this.grants.Remove(packageId);
        }
    }

    /// <inheritdoc />
    /// <remarks>The generic SDK interface cannot identify a package caller, so this overload always rejects the call.</remarks>
    public Task<CapabilityResponse> InvokeAsync(
        CapabilityRequest request,
        CancellationToken cancellationToken = default) =>
        throw new CapabilityBrokerException(
            "capability.caller_required",
            "Capability invocation requires an explicit caller package identity.");

    /// <summary>Invokes the highest-priority healthy compatible provider for an authorized package caller.</summary>
    /// <param name="callerPackageId">Package invoking the capability.</param>
    /// <param name="request">Versioned capability request with absolute deadline.</param>
    /// <param name="cancellationToken">Caller cancellation.</param>
    /// <returns>The provider response.</returns>
    public async Task<CapabilityResponse> InvokeAsync(
        string callerPackageId,
        CapabilityRequest request,
        CancellationToken cancellationToken = default)
    {
        ValidatePackageId(callerPackageId);
        ValidateRequest(request);
        if (request.DeadlineUtc <= this.timeProvider.GetUtcNow())
        {
            throw new CapabilityBrokerException("capability.deadline_expired", "Capability request deadline has expired.");
        }

        var caller = request.Caller ?? new CapabilityCallerContext(callerPackageId, UserId: null);
        ValidateCaller(caller);
        if (!string.Equals(caller.PackageId, callerPackageId, StringComparison.Ordinal))
        {
            throw new CapabilityBrokerException(
                "capability.caller_identity_mismatch",
                "Capability caller metadata does not match the authorized package caller.");
        }
        var effectiveRequest = request with { Caller = caller };

        ICapabilityProvider provider;
        lock (this.sync)
        {
            if (!this.grants.TryGetValue(callerPackageId, out var allowed)
                || !allowed.Contains(request.CapabilityName))
            {
                throw new CapabilityBrokerException(
                    "capability.permission_denied",
                    "The caller package is not granted this capability.");
            }

            var key = Identity(request.CapabilityName, request.ContractVersion);
            provider = this.providers.TryGetValue(key, out var candidates)
                ? candidates.FirstOrDefault(candidate => candidate.Descriptor.Healthy)
                    ?? throw new CapabilityUnavailableException(request.CapabilityName, request.ContractVersion)
                : throw new CapabilityUnavailableException(request.CapabilityName, request.ContractVersion);
        }

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(request.DeadlineUtc - this.timeProvider.GetUtcNow());
        try
        {
            var response = await provider.InvokeAsync(effectiveRequest, deadline.Token).ConfigureAwait(false);
            await this.audit.AppendAsync(new AuditRecord(
                caller.UserId,
                AgentId: null,
                SourcePackageId: callerPackageId,
                Action: "capability.invoke",
                TargetType: "capability",
                TargetId: request.CapabilityName,
                response.Succeeded ? AuditOutcome.Succeeded : AuditOutcome.Failed,
                request.CorrelationId,
                RemoteAddress: null,
                response.Succeeded ? "Capability invocation succeeded." : "Capability invocation failed.",
                $"provider={provider.Descriptor.ProviderPackageId};operation={request.Operation}"),
                cancellationToken).ConfigureAwait(false);
            return response;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await this.audit.AppendAsync(new AuditRecord(
                caller.UserId,
                AgentId: null,
                SourcePackageId: callerPackageId,
                Action: "capability.invoke",
                TargetType: "capability",
                TargetId: request.CapabilityName,
                AuditOutcome.Failed,
                request.CorrelationId,
                RemoteAddress: null,
                "Capability invocation failed.",
                $"provider={provider.Descriptor.ProviderPackageId};error={exception.GetType().Name}"),
                cancellationToken).ConfigureAwait(false);
            throw;
        }
    }

    private static void ValidateRequest(CapabilityRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateText(request.CapabilityName, 128);
        ValidateText(request.ContractVersion, 64);
        ValidateText(request.Operation, 128);
        ValidateText(request.CorrelationId, 64);
        if (request.Payload.GetRawText().Length > 1024 * 1024)
        {
            throw new CapabilityBrokerException("capability.payload_too_large", "Capability payload is too large.");
        }
    }

    private static void ValidateCaller(CapabilityCallerContext caller)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ValidatePackageId(caller.PackageId);
        if (caller.UserId == Guid.Empty)
        {
            throw new CapabilityBrokerException(
                "capability.user_identity_invalid",
                "Capability user identity is invalid.");
        }
    }

    private static void ValidateDescriptor(CapabilityProviderDescriptor descriptor)
    {
        ValidatePackageId(descriptor.ProviderPackageId);
        ValidateText(descriptor.CapabilityName, 128);
        ValidateText(descriptor.ContractVersion, 64);
        if (descriptor.Priority is < -1000 or > 1000)
        {
            throw new CapabilityBrokerException("capability.priority_invalid", "Capability provider priority is invalid.");
        }
    }

    private static void ValidatePackageId(string packageId) => ValidateText(packageId, 128);

    private static void ValidateText(string value, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value != value.Trim()
            || value.Length > maximumLength
            || value.Any(char.IsControl))
        {
            throw new CapabilityBrokerException("capability.value_invalid", "Capability value is invalid.");
        }
    }

    private static string Identity(string name, string version) => $"{name}\n{version}";
}

/// <summary>Stable caller-safe capability routing or authorization failure.</summary>
public sealed class CapabilityBrokerException : Exception
{
    /// <summary>Creates a capability broker failure.</summary>
    /// <param name="code">Stable machine-readable failure code.</param>
    /// <param name="message">Caller-safe explanation.</param>
    public CapabilityBrokerException(string code, string message)
        : base(message)
    {
        this.Code = code;
    }

    /// <summary>Gets the stable machine-readable failure code.</summary>
    public string Code { get; }
}
