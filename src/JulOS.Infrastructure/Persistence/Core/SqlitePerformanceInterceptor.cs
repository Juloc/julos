using System.Data.Common;

using Microsoft.EntityFrameworkCore.Diagnostics;

namespace JulOS.Infrastructure.Persistence.Core;

/// <summary>
/// Applies the SQLite pragmas that let a single-file database carry JulOS's concurrent
/// background writes (reconciler, per-session activity pings, audit, layout saves):
/// <list type="bullet">
/// <item>WAL journal mode so readers never block the single writer and vice versa;</item>
/// <item>a five-second busy timeout so a contended write waits briefly instead of
/// failing immediately with "database is locked";</item>
/// <item>NORMAL synchronous, which stays crash-safe under WAL but is markedly faster
/// than the FULL default.</item>
/// </list>
/// The interceptor is registered only for the SQLite provider; PostgreSQL deployments
/// never see it and keep their own tuning.
/// </summary>
internal sealed class SqlitePerformanceInterceptor : DbConnectionInterceptor
{
    internal static readonly SqlitePerformanceInterceptor Instance = new();

    private const string Pragmas =
        "PRAGMA journal_mode=WAL;"
        + "PRAGMA busy_timeout=5000;"
        + "PRAGMA synchronous=NORMAL;";

    private SqlitePerformanceInterceptor()
    {
    }

    /// <inheritdoc />
    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData) =>
        Apply(connection);

    /// <inheritdoc />
    public override async Task ConnectionOpenedAsync(
        DbConnection connection,
        ConnectionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = Pragmas;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static void Apply(DbConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = Pragmas;
        command.ExecuteNonQuery();
    }
}
