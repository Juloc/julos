namespace JulOS.Infrastructure.Agents;

internal sealed partial class PostgresAgentControlService
{
    static PostgresAgentControlService()
    {
        AllowedCommandTypes.Add("container.inventory.read");
        AllowedCommandTypes.Add("container.logs.read");
        AllowedCommandTypes.Add("container.control");
    }
}
