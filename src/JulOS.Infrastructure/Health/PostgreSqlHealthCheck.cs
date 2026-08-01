using Microsoft.Extensions.Diagnostics.HealthChecks;

using Npgsql;

namespace JulOS.Infrastructure.Health;

/// <summary>
/// Reports whether the core database is reachable and accepting queries.
/// </summary>
/// <remarks>
/// The check opens a connection and runs a trivial statement rather than only
/// resolving the host, because a reachable server that refuses authentication or
/// has not finished starting is not a ready dependency.
/// </remarks>
public sealed class PostgreSqlHealthCheck : IHealthCheck
{
    private readonly string connectionString;

    /// <summary>Creates the check for one core database connection string.</summary>
    /// <param name="connectionString">A Npgsql connection string. Never logged or reported.</param>
    public PostgreSqlHealthCheck(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        this.connectionString = connectionString;
    }

    /// <inheritdoc />
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        try
        {
            await using var connection = new NpgsqlConnection(this.connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            await using var command = new NpgsqlCommand("SELECT 1", connection);
            _ = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);

            return HealthCheckResult.Healthy("The core database accepted a query.");
        }
        catch (NpgsqlException exception)
        {
            // The message describes the transport failure and holds no credentials.
            return new HealthCheckResult(
                context.Registration.FailureStatus,
                "The core database is not reachable.",
                exception);
        }
    }
}
