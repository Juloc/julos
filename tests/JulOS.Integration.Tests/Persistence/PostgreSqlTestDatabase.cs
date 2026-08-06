using Npgsql;

namespace JulOS.Integration.Tests.Persistence;

internal sealed class PostgreSqlTestDatabase : IAsyncDisposable
{
    private const string EnvironmentVariable = "JULOS_TEST_POSTGRES";

    private readonly string maintenanceConnectionString;

    private PostgreSqlTestDatabase(string maintenanceConnectionString, string databaseName, string connectionString)
    {
        this.maintenanceConnectionString = maintenanceConnectionString;
        this.DatabaseName = databaseName;
        this.ConnectionString = connectionString;
    }

    internal string DatabaseName { get; }

    internal string ConnectionString { get; }

    internal static async Task<PostgreSqlTestDatabase> CreateAsync()
    {
        var configured = Environment.GetEnvironmentVariable(EnvironmentVariable);

        if (string.IsNullOrWhiteSpace(configured))
        {
            Assert.Inconclusive(
                $"Set {EnvironmentVariable} to a PostgreSQL maintenance database. CI supplies a real PostgreSQL service.");
        }

        var maintenance = new NpgsqlConnectionStringBuilder(configured)
        {
            Database = "postgres",
            Pooling = false,
        };
        var databaseName = $"julos_test_{Guid.NewGuid():N}";

        await using (var connection = new NpgsqlConnection(maintenance.ConnectionString))
        {
            await connection.OpenAsync().ConfigureAwait(false);
            await using var command = new NpgsqlCommand($"CREATE DATABASE \"{databaseName}\"", connection);
            await command.ExecuteNonQueryAsync().ConfigureAwait(false);
        }

        var database = new NpgsqlConnectionStringBuilder(maintenance.ConnectionString)
        {
            Database = databaseName,
            Pooling = false,
        };

        return new PostgreSqlTestDatabase(maintenance.ConnectionString, databaseName, database.ConnectionString);
    }

    public async ValueTask DisposeAsync()
    {
        NpgsqlConnection.ClearAllPools();

        await using var connection = new NpgsqlConnection(this.maintenanceConnectionString);
        await connection.OpenAsync().ConfigureAwait(false);

        await using var terminate = new NpgsqlCommand(
            "SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname = @database AND pid <> pg_backend_pid()",
            connection);
        terminate.Parameters.AddWithValue("database", this.DatabaseName);
        await terminate.ExecuteNonQueryAsync().ConfigureAwait(false);

        await using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{this.DatabaseName}\"", connection);
        await drop.ExecuteNonQueryAsync().ConfigureAwait(false);
    }
}
