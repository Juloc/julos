using JulOS.Infrastructure.Persistence.Core;

namespace JulOS.Server;

/// <summary>The explicit core database migration process.</summary>
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

        var connectionString = configuration.GetConnectionString("CoreDatabase")
            ?? throw new InvalidOperationException(
                "The connection string 'CoreDatabase' is not configured. "
                + "Set ConnectionStrings__CoreDatabase or see deploy/compose/README.md.");

        await CoreDatabaseMigrator
            .MigrateAsync(connectionString, cancellationToken)
            .ConfigureAwait(false);

        return 0;
    }
}
