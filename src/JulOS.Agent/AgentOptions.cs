namespace JulOS.Agent;

internal sealed record AgentOptions(
    Uri ServerEndpoint,
    Guid AgentId,
    string Credential,
    string Version,
    TimeSpan HeartbeatInterval,
    TimeSpan CommandPollInterval)
{
    internal static AgentOptions Create(
        AgentBootstrapOptions bootstrap,
        AgentProvisioningState identity)
    {
        ArgumentNullException.ThrowIfNull(bootstrap);
        ArgumentNullException.ThrowIfNull(identity);
        identity.Validate();
        if (identity.Status != AgentProvisioningStatus.Enrolled
            || identity.AgentId is not Guid agentId
            || identity.HeartbeatIntervalSeconds is not int heartbeatSeconds
            || identity.CommandPollIntervalSeconds is not int pollSeconds)
        {
            throw new InvalidOperationException("The Agent identity is not enrolled.");
        }

        return new AgentOptions(
            bootstrap.ServerEndpoint,
            agentId,
            identity.Credential,
            identity.Version,
            bootstrap.HeartbeatIntervalOverride ?? TimeSpan.FromSeconds(heartbeatSeconds),
            bootstrap.CommandPollIntervalOverride ?? TimeSpan.FromSeconds(pollSeconds));
    }
}
