using JulOS.Infrastructure.Persistence.Core;

using Microsoft.AspNetCore.Mvc.Testing;

namespace JulOS.Integration.Tests;

/// <summary>A migrated SQLite database with a real Server host in front of it.</summary>
/// <remarks>
/// Used where the assertions are about transport — cookies, problem codes, status codes —
/// and must therefore run on every machine rather than report inconclusive wherever no
/// PostgreSQL container is available.
/// </remarks>
internal sealed class SqliteServerHost : IAsyncDisposable
{
    private readonly string directory;
    private readonly ServerHost host;

    private SqliteServerHost(string directory, ServerHost host, string connectionString)
    {
        this.directory = directory;
        this.host = host;
        this.ConnectionString = connectionString;
    }

    internal string ConnectionString { get; }

    internal static Task<SqliteServerHost> CreateAsync(string name) =>
        CreateAsync(name, settings: null);

    /// <summary>Creates a host with extra deployment settings, such as trusted publishers.</summary>
    internal static async Task<SqliteServerHost> CreateAsync(
        string name,
        IReadOnlyDictionary<string, string?>? settings)
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "julos-integration-tests",
            name,
            Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(directory);
        var connectionString = $"Data Source={Path.Combine(directory, "julos.db")};Pooling=False";

        await CoreDatabaseMigrator.MigrateAsync(
            new CoreDatabaseConfiguration(CoreDatabaseProvider.Sqlite, connectionString))
            .ConfigureAwait(false);

        var configuration = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Database:Provider"] = "sqlite",
        };
        foreach (var setting in settings ?? new Dictionary<string, string?>(StringComparer.Ordinal))
        {
            configuration[setting.Key] = setting.Value;
        }

        return new SqliteServerHost(
            directory,
            new ServerHost(connectionString, configuration),
            connectionString);
    }

    internal HttpClient CreateClient(WebApplicationFactoryClientOptions options) =>
        this.host.CreateClient(options);

    public ValueTask DisposeAsync()
    {
        this.host.Dispose();
        try
        {
            Directory.Delete(this.directory, recursive: true);
        }
        catch (IOException)
        {
            // Temporary test files the operating system still holds are not a failure.
        }

        return ValueTask.CompletedTask;
    }
}
