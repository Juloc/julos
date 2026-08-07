using System.Data.Common;
using System.Globalization;

using Microsoft.Data.Sqlite;

using Npgsql;

namespace JulOS.Browser.Worker;

/// <summary>
/// Small package-owned store for Browser profile metadata.
/// Chromium profile bytes remain in isolated runtime volumes and never enter this database.
/// </summary>
public sealed class BrowserProfileStore
{
    private const int SchemaVersion = 1;
    private readonly string provider;
    private readonly string connectionString;

    /// <summary>Creates the store from the package database environment supplied by the worker supervisor.</summary>
    public BrowserProfileStore(string provider, string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        if (provider is not ("sqlite" or "postgresql"))
        {
            throw new ArgumentOutOfRangeException(nameof(provider), provider, "Unsupported Browser package database provider.");
        }
        this.provider = provider;
        this.connectionString = connectionString;
    }

    /// <summary>Reads the package database identity from the worker process environment.</summary>
    public static BrowserProfileStore FromEnvironment()
    {
        var provider = Environment.GetEnvironmentVariable("JULOS_PACKAGE_DATABASE_PROVIDER");
        var connectionString = Environment.GetEnvironmentVariable("JULOS_PACKAGE_DATABASE");
        if (string.IsNullOrWhiteSpace(provider) || string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("Browser package database environment is unavailable.");
        }
        return new BrowserProfileStore(provider, connectionString);
    }

    /// <summary>Creates the initial package-owned schema and refuses unknown future schema versions.</summary>
    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        await ExecuteAsync(connection, transaction, """
            CREATE TABLE IF NOT EXISTS browser_schema (
                schema_version INTEGER NOT NULL PRIMARY KEY
            )
            """, cancellationToken).ConfigureAwait(false);
        await ExecuteAsync(connection, transaction, """
            INSERT INTO browser_schema (schema_version)
            SELECT 1
            WHERE NOT EXISTS (SELECT 1 FROM browser_schema)
            """, cancellationToken).ConfigureAwait(false);

        var schemaVersion = await ScalarIntAsync(
            connection,
            transaction,
            "SELECT schema_version FROM browser_schema",
            cancellationToken).ConfigureAwait(false);
        if (schemaVersion != SchemaVersion)
        {
            throw new InvalidOperationException($"Browser package database schema {schemaVersion} is not supported.");
        }

        await ExecuteAsync(connection, transaction, """
            CREATE TABLE IF NOT EXISTS browser_network_profiles (
                profile_key TEXT NOT NULL PRIMARY KEY,
                runtime_network TEXT NOT NULL,
                proxy_secret_reference_id TEXT NULL,
                revision INTEGER NOT NULL CHECK (revision >= 1)
            )
            """, cancellationToken).ConfigureAwait(false);
        await ExecuteAsync(connection, transaction, """
            CREATE TABLE IF NOT EXISTS browser_profiles (
                profile_id TEXT NOT NULL PRIMARY KEY,
                owner_user_id TEXT NOT NULL,
                display_name TEXT NOT NULL,
                mode TEXT NOT NULL CHECK (mode IN ('persistent', 'application')),
                network_profile_key TEXT NOT NULL,
                start_url TEXT NULL,
                application_key TEXT NULL,
                created_at_utc TEXT NOT NULL,
                updated_at_utc TEXT NOT NULL,
                revision INTEGER NOT NULL CHECK (revision >= 1),
                FOREIGN KEY (network_profile_key) REFERENCES browser_network_profiles(profile_key)
            )
            """, cancellationToken).ConfigureAwait(false);
        await ExecuteAsync(connection, transaction, """
            CREATE INDEX IF NOT EXISTS ix_browser_profiles_owner
            ON browser_profiles (owner_user_id, display_name)
            """, cancellationToken).ConfigureAwait(false);

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Persists one validated network profile. Existing keys are not overwritten implicitly.</summary>
    public async Task CreateNetworkProfileAsync(
        BrowserNetworkProfile profile,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(profile);
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO browser_network_profiles (
                profile_key,
                runtime_network,
                proxy_secret_reference_id,
                revision)
            VALUES (@key, @network, @secret, @revision)
            """;
        Add(command, "@key", profile.Key);
        Add(command, "@network", profile.RuntimeNetwork);
        Add(command, "@secret", profile.ProxySecretReferenceId?.ToString("D"));
        Add(command, "@revision", profile.Revision);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Lists package-wide network profiles without exposing any secret value.</summary>
    public async Task<IReadOnlyList<BrowserNetworkProfile>> ListNetworkProfilesAsync(
        CancellationToken cancellationToken)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT profile_key, runtime_network, proxy_secret_reference_id, revision
            FROM browser_network_profiles
            ORDER BY profile_key
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var result = new List<BrowserNetworkProfile>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            result.Add(new BrowserNetworkProfile(
                reader.GetString(0),
                reader.GetString(1),
                reader.IsDBNull(2) ? null : Guid.Parse(reader.GetString(2)),
                reader.GetInt32(3)));
        }
        return result;
    }

    /// <summary>Persists a retained profile after verifying that its selected network profile exists.</summary>
    public async Task CreateProfileAsync(BrowserProfile profile, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (profile.Mode == BrowserProfileMode.Temporary)
        {
            throw new InvalidOperationException("Temporary Browser profiles must not be persisted.");
        }

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        if (!await NetworkProfileExistsAsync(
                connection,
                transaction,
                profile.NetworkProfileKey,
                cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidOperationException("Browser network profile does not exist.");
        }

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO browser_profiles (
                profile_id,
                owner_user_id,
                display_name,
                mode,
                network_profile_key,
                start_url,
                application_key,
                created_at_utc,
                updated_at_utc,
                revision)
            VALUES (
                @id,
                @owner,
                @name,
                @mode,
                @network,
                @startUrl,
                @applicationKey,
                @created,
                @updated,
                @revision)
            """;
        Add(command, "@id", profile.ProfileId.ToString("D"));
        Add(command, "@owner", profile.OwnerUserId.ToString("D"));
        Add(command, "@name", profile.DisplayName);
        Add(command, "@mode", ModeName(profile.Mode));
        Add(command, "@network", profile.NetworkProfileKey);
        Add(command, "@startUrl", profile.StartUrl?.AbsoluteUri);
        Add(command, "@applicationKey", profile.ApplicationKey);
        Add(command, "@created", profile.CreatedAtUtc.ToString("O", CultureInfo.InvariantCulture));
        Add(command, "@updated", profile.UpdatedAtUtc.ToString("O", CultureInfo.InvariantCulture));
        Add(command, "@revision", profile.Revision);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Lists only retained profiles owned by the authenticated user.</summary>
    public async Task<IReadOnlyList<BrowserProfile>> ListProfilesAsync(
        Guid ownerUserId,
        CancellationToken cancellationToken)
    {
        RequireUser(ownerUserId);
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                profile_id,
                owner_user_id,
                display_name,
                mode,
                network_profile_key,
                start_url,
                application_key,
                created_at_utc,
                updated_at_utc,
                revision
            FROM browser_profiles
            WHERE owner_user_id = @owner
            ORDER BY display_name, profile_id
            """;
        Add(command, "@owner", ownerUserId.ToString("D"));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var result = new List<BrowserProfile>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            result.Add(ReadProfile(reader));
        }
        return result;
    }

    /// <summary>Reads one profile only when its owner matches the authenticated user.</summary>
    public async Task<BrowserProfile?> ReadProfileAsync(
        Guid ownerUserId,
        Guid profileId,
        CancellationToken cancellationToken)
    {
        RequireUser(ownerUserId);
        if (profileId == Guid.Empty)
        {
            throw new ArgumentException("Browser profile ID is required.", nameof(profileId));
        }

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                profile_id,
                owner_user_id,
                display_name,
                mode,
                network_profile_key,
                start_url,
                application_key,
                created_at_utc,
                updated_at_utc,
                revision
            FROM browser_profiles
            WHERE profile_id = @id AND owner_user_id = @owner
            """;
        Add(command, "@id", profileId.ToString("D"));
        Add(command, "@owner", ownerUserId.ToString("D"));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
            ? ReadProfile(reader)
            : null;
    }

    /// <summary>Deletes one retained profile using both owner identity and optimistic revision.</summary>
    public async Task<bool> DeleteProfileAsync(
        Guid ownerUserId,
        Guid profileId,
        int revision,
        CancellationToken cancellationToken)
    {
        RequireUser(ownerUserId);
        if (profileId == Guid.Empty)
        {
            throw new ArgumentException("Browser profile ID is required.", nameof(profileId));
        }
        if (revision < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(revision));
        }

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            DELETE FROM browser_profiles
            WHERE profile_id = @id AND owner_user_id = @owner AND revision = @revision
            """;
        Add(command, "@id", profileId.ToString("D"));
        Add(command, "@owner", ownerUserId.ToString("D"));
        Add(command, "@revision", revision);
        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) == 1;
    }

    private DbConnection CreateConnection() => this.provider switch
    {
        "sqlite" => new SqliteConnection(this.connectionString),
        "postgresql" => new NpgsqlConnection(this.connectionString),
        _ => throw new InvalidOperationException("Browser package database provider is unsupported."),
    };

    private static async Task<bool> NetworkProfileExistsAsync(
        DbConnection connection,
        DbTransaction transaction,
        string key,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT 1 FROM browser_network_profiles WHERE profile_key = @key";
        Add(command, "@key", key);
        return await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is not null;
    }

    private static BrowserProfile ReadProfile(DbDataReader reader)
    {
        var mode = reader.GetString(3) switch
        {
            "persistent" => BrowserProfileMode.Persistent,
            "application" => BrowserProfileMode.Application,
            _ => throw new InvalidOperationException("Stored Browser profile mode is invalid."),
        };
        var startUrl = reader.IsDBNull(5) ? null : new Uri(reader.GetString(5), UriKind.Absolute);
        return new BrowserProfile(
            Guid.Parse(reader.GetString(0)),
            Guid.Parse(reader.GetString(1)),
            reader.GetString(2),
            mode,
            reader.GetString(4),
            startUrl,
            reader.IsDBNull(6) ? null : reader.GetString(6),
            DateTimeOffset.Parse(reader.GetString(7), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
            DateTimeOffset.Parse(reader.GetString(8), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
            reader.GetInt32(9));
    }

    private static string ModeName(BrowserProfileMode mode) => mode switch
    {
        BrowserProfileMode.Persistent => "persistent",
        BrowserProfileMode.Application => "application",
        BrowserProfileMode.Temporary => throw new InvalidOperationException("Temporary Browser profiles are not persisted."),
        _ => throw new ArgumentOutOfRangeException(nameof(mode)),
    };

    private static async Task ExecuteAsync(
        DbConnection connection,
        DbTransaction transaction,
        string sql,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<int> ScalarIntAsync(
        DbConnection connection,
        DbTransaction transaction,
        string sql,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        var value = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Browser package schema version is missing.");
        return Convert.ToInt32(value, CultureInfo.InvariantCulture);
    }

    private static void Add(DbCommand command, string name, object? value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value ?? DBNull.Value;
        command.Parameters.Add(parameter);
    }

    private static void RequireUser(Guid ownerUserId)
    {
        if (ownerUserId == Guid.Empty)
        {
            throw new ArgumentException("Browser profile owner is required.", nameof(ownerUserId));
        }
    }
}
