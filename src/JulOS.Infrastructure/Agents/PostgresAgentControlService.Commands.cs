using System.Text.Json;

using Microsoft.EntityFrameworkCore;

namespace JulOS.Infrastructure.Agents;

internal sealed partial class PostgresAgentControlService
{
    private const string CommandCapabilityName = "agent.commands.core";
    private const int CommandCapabilityVersion = 1;
    private const int CommandMetadataVersion = 1;
    private const int MaximumAdvertisedCommands = 64;
    private static readonly TimeSpan MaximumCommandCapabilityAge = TimeSpan.FromMinutes(5);

    private async Task EnsureCommandAdvertisedAsync(
        Guid agentId,
        string commandType,
        CancellationToken cancellationToken)
    {
        var capability = await this.context.AgentCapabilities.AsNoTracking()
            .SingleOrDefaultAsync(
                candidate => candidate.AgentId == agentId
                    && candidate.CapabilityName == CommandCapabilityName,
                cancellationToken)
            .ConfigureAwait(false)
            ?? throw Failure(
                "agent.command_capability_unavailable",
                "The Agent has not advertised command execution support.");

        if (!capability.Enabled)
        {
            throw Failure(
                "agent.command_capability_disabled",
                "The Agent command capability is disabled.");
        }

        if (capability.CapabilityVersion != CommandCapabilityVersion
            || capability.MetadataVersion != CommandMetadataVersion)
        {
            throw Failure(
                "agent.command_capability_incompatible",
                "The Agent command capability version is incompatible.");
        }

        var now = this.timeProvider.GetUtcNow();
        if (capability.ObservedAtUtc > now.AddMinutes(5)
            || capability.ObservedAtUtc < now.Subtract(MaximumCommandCapabilityAge))
        {
            throw Failure(
                "agent.command_capability_stale",
                "The Agent command capability observation is stale.");
        }

        HashSet<string> commands;
        try
        {
            commands = ParseAdvertisedCommands(capability.Metadata);
        }
        catch (JsonException exception)
        {
            throw Failure(
                "agent.command_capability_invalid",
                "The Agent command capability metadata is invalid.",
                exception);
        }

        if (!commands.Contains(commandType))
        {
            throw Failure(
                "agent.command_not_advertised",
                "The Agent did not advertise the requested command.");
        }
    }

    private static HashSet<string> ParseAdvertisedCommands(string metadata)
    {
        using var document = JsonDocument.Parse(metadata);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("commands", out var commandsElement)
            || commandsElement.ValueKind != JsonValueKind.Array
            || commandsElement.GetArrayLength() is < 1 or > MaximumAdvertisedCommands)
        {
            throw new JsonException("Command capability metadata shape is invalid.");
        }

        var commands = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in commandsElement.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
            {
                throw new JsonException("Advertised command identity must be a string.");
            }

            var value = item.GetString();
            if (value is null
                || !AllowedCommandTypes.Contains(value)
                || !commands.Add(value))
            {
                throw new JsonException("Advertised command identity is invalid or duplicated.");
            }
        }

        return commands;
    }
}
