using JulOS.Infrastructure.Persistence.Core;

namespace JulOS.Server;

/// <summary>The explicit core database initialization process.</summary>
internal static class DatabaseMigrationCommand
{
    private const string CommandSwitch = "--migrate-database";

    internal static bool IsRequested(string[] arguments) =>
        Array.IndexOf(arguments, CommandSwitch) >= 0;

    internal static async Task<int> RunAsync(
        IConfiguration configuration,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var database = CoreDatabaseConfiguration.Read(configuration);
        await CoreDatabaseMigrator
            .MigrateAsync(database, cancellationToken)
            .ConfigureAwait(false);

        return 0;
    }
}
