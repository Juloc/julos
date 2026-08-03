using System.Net;

namespace JulOS.Agent;

internal sealed class AgentProvisioner
{
    private readonly AgentIdentityStore identityStore;
    private readonly AgentEnrollmentClient enrollmentClient;
    private readonly TimeProvider timeProvider;

    internal AgentProvisioner(
        AgentIdentityStore identityStore,
        AgentEnrollmentClient enrollmentClient,
        TimeProvider timeProvider)
    {
        this.identityStore = identityStore ?? throw new ArgumentNullException(nameof(identityStore));
        this.enrollmentClient = enrollmentClient ?? throw new ArgumentNullException(nameof(enrollmentClient));
        this.timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    internal async Task<AgentProvisioningState> ResolveAsync(
        AgentBootstrapOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        using var provisioningLock = this.identityStore.AcquireProvisioningLock();
        var state = await this.identityStore.LoadAsync(cancellationToken).ConfigureAwait(false);
        if (state is null)
        {
            if (options.EnrollmentToken is null)
            {
                throw new InvalidOperationException(
                    "No Agent identity exists and JULOS_AGENT_ENROLLMENT_TOKEN is missing.");
            }

            state = await AgentProvisioningState.CreatePendingAsync(
                options,
                cancellationToken).ConfigureAwait(false);
            await this.identityStore.SaveAsync(state, cancellationToken).ConfigureAwait(false);
        }

        if (state.Status == AgentProvisioningStatus.Enrolled)
        {
            return state;
        }

        var retryDelay = TimeSpan.FromSeconds(1);
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var enrolled = await this.enrollmentClient.EnrollAsync(
                    options,
                    state,
                    cancellationToken).ConfigureAwait(false);
                await this.identityStore.SaveAsync(enrolled, cancellationToken).ConfigureAwait(false);
                return enrolled;
            }
            catch (HttpRequestException exception) when (IsRetryable(exception))
            {
                await Task.Delay(retryDelay, this.timeProvider, cancellationToken).ConfigureAwait(false);
                retryDelay = TimeSpan.FromSeconds(Math.Min(30, retryDelay.TotalSeconds * 2));
            }
        }

        throw new OperationCanceledException(cancellationToken);
    }

    private static bool IsRetryable(HttpRequestException exception) =>
        exception.StatusCode is null
        || exception.StatusCode == HttpStatusCode.RequestTimeout
        || (int)exception.StatusCode.Value >= 500;
}
