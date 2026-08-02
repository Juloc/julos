using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

using Npgsql;

namespace JulOS.Infrastructure.Packages;

public sealed record PackageDatabaseIdentity(
    string PackageId,
    string Schema,
    string Role,
    string Password);

/// <summary>Creates a schema and restricted login role that cannot access another package schema.</summary>
public sealed partial class PostgresPackageStorageProvisioner
{
    private readonly string administrativeConnectionString;

    public PostgresPackageStorageProvisioner(string administrativeConnectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(administrativeConnectionString);
        this.administrativeConnectionString = administrativeConnectionString;
    }

    public async Task<PackageDatabaseIdentity> ProvisionAsync(
        string packageId,
        CancellationToken cancellationToken = default)
    {
        ValidatePackageId(packageId);
        var suffix = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(packageId)))[..20];
        var schema = $"pkg_{suffix}";
        var role = $"julos_pkg_{suffix}";
        var password = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));

        await using var connection = new NpgsqlConnection(this.administrativeConnectionString);
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
                $"ALTER ROLE {Quote(role)} PASSWORD {Literal(password)} NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT NOREPLICATION",
                cancellationToken).ConfigureAwait(false);
        }

        await ExecuteAsync(connection, transaction, $"REVOKE ALL ON SCHEMA public FROM {Quote(role)}", cancellationToken)
            .ConfigureAwait(false);
        await ExecuteAsync(connection, transaction, $"REVOKE ALL ON DATABASE current_database() FROM {Quote(role)}", cancellationToken)
            .ConfigureAwait(false);
        await ExecuteAsync(connection, transaction, $"GRANT CONNECT ON DATABASE {Quote(connection.Database)} TO {Quote(role)}", cancellationToken)
            .ConfigureAwait(false);
        await ExecuteAsync(connection, transaction, $"GRANT USAGE, CREATE ON SCHEMA {Quote(schema)} TO {Quote(role)}", cancellationToken)
            .ConfigureAwait(false);
        await ExecuteAsync(
            connection,
            transaction,
            $"ALTER ROLE {Quote(role)} SET search_path = {Quote(schema)}",
            cancellationToken).ConfigureAwait(false);
        await ExecuteAsync(
            connection,
            transaction,
            $"ALTER DEFAULT PRIVILEGES IN SCHEMA {Quote(schema)} GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO {Quote(role)}",
            cancellationToken).ConfigureAwait(false);
        await ExecuteAsync(
            connection,
            transaction,
            $"ALTER DEFAULT PRIVILEGES IN SCHEMA {Quote(schema)} GRANT USAGE, SELECT, UPDATE ON SEQUENCES TO {Quote(role)}",
            cancellationToken).ConfigureAwait(false);

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return new PackageDatabaseIdentity(packageId, schema, role, password);
    }

    public async Task DropAsync(
        string packageId,
        bool deleteData,
        CancellationToken cancellationToken = default)
    {
        ValidatePackageId(packageId);
        var suffix = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(packageId)))[..20];
        var schema = $"pkg_{suffix}";
        var role = $"julos_pkg_{suffix}";
        await using var connection = new NpgsqlConnection(this.administrativeConnectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        if (deleteData)
        {
            await ExecuteAsync(connection, transaction, $"DROP SCHEMA IF EXISTS {Quote(schema)} CASCADE", cancellationToken)
                .ConfigureAwait(false);
        }
        await ExecuteAsync(connection, transaction, $"DROP ROLE IF EXISTS {Quote(role)}", cancellationToken)
            .ConfigureAwait(false);
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
