using System.Data.Common;

using JulOS.Infrastructure.Persistence.Core;

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Diagnostics.HealthChecks;

using Npgsql;

namespace JulOS.Infrastructure.Health;

/// <summary>Reports whether the configured core database accepts queries.</summary>
public sealed class PostgreSqlHealthCheck : IHealthCheck
{
    private readonly CoreDatabaseConfiguration database;

    /// <summary>Creates a PostgreSQL check for compatibility with existing callers.</summary>
    public PostgreSqlHealthCheck(string connectionString)
        : this(new CoreDatabaseConfiguration(CoreDatabaseProvider.PostgreSql, connectionString))
    {
    }

    /// <summary>Creates the check for the configured core database.</summary>
    public PostgreSqlHealthCheck(CoreDatabaseConfiguration database)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentException.ThrowIfNullOrWhiteSpace(database.ConnectionString);
        this.database = database;
    }

    /// <inheritdoc />
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        try
        {
            await using var connection = CreateConnection();
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT 1";
            _ = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);

            return HealthCheckResult.Healthy("The core database accepted a query.");
        }
        catch (DbException exception)
        {
            return new HealthCheckResult(
                context.Registration.FailureStatus,
                "The core database is not reachable.",
                exception);
        }
    }

    private DbConnection CreateConnection() => this.database.Provider switch
    {
        CoreDatabaseProvider.Sqlite => new SqliteConnection(this.database.ConnectionString),
        _ => new NpgsqlConnection(this.database.ConnectionString),
    };
}
