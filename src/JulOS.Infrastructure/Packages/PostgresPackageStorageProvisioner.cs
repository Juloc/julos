using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

using JulOS.Infrastructure.Persistence.Core;

using Microsoft.Data.Sqlite;

using Npgsql;

namespace JulOS.Infrastructure.Packages;

/// <summary>Restricted database identity created for one package.</summary>
/// <param name="PackageId">Owning package identity.</param>
/// <param name="Schema">Package-owned schema name.</param>
/// <param name="Role">Restricted login role.</param>
/// <param name="Password">New plaintext password returned only to the caller.</param>
public sealed record PackageDatabaseIdentity(
    string PackageId,
    string Schema,
    string Role,
    string Password)
{
    /// <summary>Database provider used by the package store.</summary>
    public string Provider { get; init; } = "postgresql";

    /// <summary>Direct package connection string for providers without roles.</summary>
    public string? ConnectionString { get; init; }
}

/// <summary>Creates isolated PostgreSQL schemas or SQLite package files.</summary>
public sealed partial class PostgresPackageStorageProvisioner
{
    private readonly CoreDatabaseConfiguration database;
    private readonly string packageRoot;

    /// <summary>Creates the PostgreSQL package storage provisioner.</summary>
    public PostgresPackageStorageProvisioner(string administrativeConnectionString)
        : this(
            new CoreDatabaseConfiguration(
                CoreDatabaseProvider.PostgreSql,
                administrativeConnectionString),
            "/var/lib/julos/packages")
    {
    }

    /// <summary>Creates a provider-aware package storage provisioner.</summary>
    public PostgresPackageStorageProvisioner(
        CoreDatabaseConfiguration database,
        string packageRoot)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentException.ThrowIfNullOrWhiteSpace(database.ConnectionString);
        ArgumentException.ThrowIfNullOrWhiteSpace(packageRoot);
        this.database = database;
        this.packageRoot = Path.GetFullPath(packageRoot);
    }

    /// <summary>Creates or reactivates isolated package storage.</summary>
    public Task<PackageDatabaseIdentity> ProvisionAsync(
        string packageId,
        CancellationToken cancellationToken = default) =>
        this.database.Provider == CoreDatabaseProvider.Sqlite
            ? ProvisionSqliteAsync(packageId, cancellationToken)
            : ProvisionPostgreSqlAsync(packageId, cancellationToken);

    /// <summary>Disables package access and optionally destroys isolated package data.</summary>
    public Task DropAsync(
        string packageId,
        bool deleteData,
        CancellationToken cancellationToken = default) =>
        this.database.Provider == CoreDatabaseProvider.Sqlite
            ? DropSqliteAsync(packageId, deleteData, cancellationToken)
            : DropPostgreSqlAsync(packageId, deleteData, cancellationToken);

    private async Task<PackageDatabaseIdentity> ProvisionSqliteAsync(
        string packageId,
        CancellationToken cancellationToken)
    {
        ValidatePackageId(packageId);
        var directory = Path.Combine(this.packageRoot, packageId, "data");
        Directory.CreateDirectory(directory);
        var databasePath = Path.Combine(directory, "package.db");
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
        }.ToString();

        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        return new PackageDatabaseIdentity(packageId, "main", string.Empty, string.Empty)
        {
            Provider = "sqlite",
            ConnectionString = connectionString,
        };
    }

    private Task DropSqliteAsync(
        string packageId,
        bool deleteData,
        CancellationToken cancellationToken)
    {
        ValidatePackageId(packageId);
        cancellationToken.ThrowIfCancellationRequested();
        if (!deleteData)
        {
            return Task.CompletedTask;
        }

        var databasePath = Path.Combine(this.packageRoot, packageId, "data", "package.db");

        // The package database is opened with connection pooling, so a pooled
        // connection keeps the file open after every SqliteConnection is disposed.
        // Clear the pool for this database before deleting its files: otherwise the
        // delete throws "file in use" on Windows and leaks the open handle on Linux,
        // where the unlinked file lingers until the pool is recycled.
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
        }.ToString();
        using (var pooledConnection = new SqliteConnection(connectionString))
        {
            SqliteConnection.ClearPool(pooledConnection);
        }

        DeleteIfPresent(databasePath);
        DeleteIfPresent(databasePath + "-wal");
        DeleteIfPresent(databasePath + "-shm");
        return Task.CompletedTask;
    }

    private async Task<PackageDatabaseIdentity> ProvisionPostgreSqlAsync(
        string packageId,
        CancellationToken cancellationToken)
    {
        ValidatePackageId(packageId);
        var suffix = PackageSuffix(packageId);
        var schema = $"pkg_{suffix}";
        var role = $"julos_pkg_{suffix}";
        var passwordBytes = RandomNumberGenerator.GetBytes(48);
        var password = Convert.ToBase64String(passwordBytes);
        CryptographicOperations.ZeroMemory(passwordBytes);

        await using var connection = new NpgsqlConnection(this.database.ConnectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        await ExecuteAsync(connection, transaction, $"CREATE SCHEMA IF NOT EXISTS {Quote(schema)}", cancellationToken)
            .ConfigureAwait(false);
        var roleExists = await RoleExistsAsync(connection, transaction, role, cancellationToken).ConfigureAwait(false);
        if (!roleExists)
        {
            await ExecuteAsync(
                connection,
                transaction,
                $"CREATE ROLE {Quote(role)} LOGIN PASSWORD {Literal(password)} NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT NOREPLICATION",
                cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await ExecuteAsync(
                connection,
                transaction,
                $"ALTER ROLE {Quote(role)} LOGIN PASSWORD {Literal(password)} NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT NOREPLICATION",
                cancellationToken).ConfigureAwait(false);
        }

        await ExecuteAsync(
            connection,
            transaction,
            $"REVOKE ALL ON DATABASE {Quote(connection.Database)} FROM {Quote(role)}",
            cancellationToken).ConfigureAwait(false);
        await ExecuteAsync(
            connection,
            transaction,
            $"GRANT CONNECT ON DATABASE {Quote(connection.Database)} TO {Quote(role)}",
            cancellationToken).ConfigureAwait(false);
        await ExecuteAsync(
            connection,
            transaction,
            $"REVOKE ALL ON SCHEMA public FROM {Quote(role)}",
            cancellationToken).ConfigureAwait(false);
        await ExecuteAsync(
            connection,
            transaction,
            $"REVOKE ALL ON SCHEMA {Quote(schema)} FROM PUBLIC",
            cancellationToken).ConfigureAwait(false);
        await ExecuteAsync(
            connection,
            transaction,
            $"GRANT USAGE, CREATE ON SCHEMA {Quote(schema)} TO {Quote(role)}",
            cancellationToken).ConfigureAwait(false);
        await ExecuteAsync(
            connection,
            transaction,
            $"ALTER ROLE {Quote(role)} SET search_path = {Quote(schema)}, pg_catalog",
            cancellationToken).ConfigureAwait(false);

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return new PackageDatabaseIdentity(packageId, schema, role, password);
    }

    private async Task DropPostgreSqlAsync(
        string packageId,
        bool deleteData,
        CancellationToken cancellationToken)
    {
        ValidatePackageId(packageId);
        var suffix = PackageSuffix(packageId);
        var schema = $"pkg_{suffix}";
        var role = $"julos_pkg_{suffix}";
        await using var connection = new NpgsqlConnection(this.database.ConnectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        if (!await RoleExistsAsync(connection, transaction, role, cancellationToken).ConfigureAwait(false))
        {
            if (deleteData)
            {
                await ExecuteAsync(
                    connection,
                    transaction,
                    $"DROP SCHEMA IF EXISTS {Quote(schema)} CASCADE",
                    cancellationToken).ConfigureAwait(false);
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        if (!deleteData)
        {
            await ExecuteAsync(
                connection,
                transaction,
                $"ALTER ROLE {Quote(role)} NOLOGIN PASSWORD NULL",
                cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        await ExecuteAsync(
            connection,
            transaction,
            $"DROP SCHEMA IF EXISTS {Quote(schema)} CASCADE",
            cancellationToken).ConfigureAwait(false);
        await ExecuteAsync(
            connection,
            transaction,
            $"DROP OWNED BY {Quote(role)}",
            cancellationToken).ConfigureAwait(false);
        await ExecuteAsync(
            connection,
            transaction,
            $"DROP ROLE {Quote(role)}",
            cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<bool> RoleExistsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string role,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("SELECT 1 FROM pg_roles WHERE rolname = $1", connection, transaction);
        command.Parameters.AddWithValue(role);
        return await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is not null;
    }

    private static async Task ExecuteAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string sql,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static string PackageSuffix(string packageId) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(packageId)))[..20];

    private static void DeleteIfPresent(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private static string Quote(string identifier) => $"\"{identifier.Replace("\"", "\"\"")}\"";

    private static string Literal(string value) => $"'{value.Replace("'", "''")}'";

    private static void ValidatePackageId(string packageId)
    {
        if (!PackageIdPattern().IsMatch(packageId))
        {
            throw new ArgumentException("Package identifier is invalid.", nameof(packageId));
        }
    }

    [GeneratedRegex("^[a-z][a-z0-9]*(?:\\.[a-z][a-z0-9-]*)+$", RegexOptions.CultureInvariant)]
    private static partial Regex PackageIdPattern();
}
