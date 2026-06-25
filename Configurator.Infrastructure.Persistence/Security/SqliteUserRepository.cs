using Configurator.Application.Services.Authorization;
using Configurator.Infrastructure.Persistence.Sqlite;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace Configurator.Infrastructure.Persistence.Security;

public sealed class SqliteUserRepository : IUserRepository
{
    private readonly SecuritySqliteMigrationRunner _migrationRunner;
    private readonly SecuritySqliteConnectionFactory _connectionFactory;
    private readonly SqlitePragmaInitializer _pragmaInitializer;
    private readonly IOptions<AuthenticationOptions> _options;

    public SqliteUserRepository(
        SecuritySqliteMigrationRunner migrationRunner,
        SecuritySqliteConnectionFactory connectionFactory,
        SqlitePragmaInitializer pragmaInitializer,
        IOptions<AuthenticationOptions> options)
    {
        _migrationRunner = migrationRunner ?? throw new ArgumentNullException(nameof(migrationRunner));
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        _pragmaInitializer = pragmaInitializer ?? throw new ArgumentNullException(nameof(pragmaInitializer));
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public async Task<long> CountAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenInitializedConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM app_user;";

        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false));
    }

    public async Task<AppUser?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenInitializedConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = SelectUserSql + " WHERE id = @id;";
        command.Parameters.AddWithValue("@id", id.ToString("D"));

        return await ReadSingleAsync(command, cancellationToken).ConfigureAwait(false);
    }

    public async Task<AppUser?> FindByNormalizedUsernameAsync(
        string normalizedUsername,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenInitializedConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = SelectUserSql + " WHERE normalized_username = @normalizedUsername;";
        command.Parameters.AddWithValue("@normalizedUsername", normalizedUsername.Trim());

        return await ReadSingleAsync(command, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<AppUser>> ListAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenInitializedConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = SelectUserSql + " ORDER BY normalized_username;";

        var users = new List<AppUser>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            users.Add(ReadUser(reader));
        }

        return users;
    }

    public async Task<UserRepositoryResult<AppUser>> CreateAsync(
        AppUser user,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);

        try
        {
            await using var connection = await OpenInitializedConnectionAsync(cancellationToken).ConfigureAwait(false);
            await InsertUserAsync(connection, null, user, cancellationToken).ConfigureAwait(false);

            return UserRepositoryResult<AppUser>.Success(user);
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 19)
        {
            return UserRepositoryResult<AppUser>.Failure(
                SecurityErrorCodes.UserDuplicate,
                "User already exists.");
        }
        catch (Exception ex) when (IsRepositoryException(ex))
        {
            return UserRepositoryResult<AppUser>.Failure(
                SecurityErrorCodes.SecurityRepositoryFailed,
                "User create failed.");
        }
    }

    public async Task<UserRepositoryResult<AppUser>> BootstrapAdministratorAsync(
        AppUser user,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);

        try
        {
            await using var connection = await OpenInitializedConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using var transaction = connection.BeginTransaction();
            var count = await CountUsersAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
            if (count != 0)
            {
                transaction.Rollback();

                return UserRepositoryResult<AppUser>.Failure(
                    SecurityErrorCodes.BootstrapAlreadyCompleted,
                    "Administrator bootstrap has already been completed.");
            }

            await InsertUserAsync(connection, transaction, user, cancellationToken).ConfigureAwait(false);
            transaction.Commit();

            return UserRepositoryResult<AppUser>.Success(user);
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 19)
        {
            return UserRepositoryResult<AppUser>.Failure(
                SecurityErrorCodes.UserDuplicate,
                "User already exists.");
        }
        catch (Exception ex) when (IsRepositoryException(ex))
        {
            return UserRepositoryResult<AppUser>.Failure(
                SecurityErrorCodes.SecurityRepositoryFailed,
                "Administrator bootstrap failed.");
        }
    }

    public async Task<UserRepositoryResult<AppUser>> UpdateAsync(
        AppUser user,
        long expectedRowVersion,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);

        try
        {
            await using var connection = await OpenInitializedConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using var command = connection.CreateCommand();
            command.CommandText = """
                UPDATE app_user
                SET username = @username,
                    normalized_username = @normalizedUsername,
                    password_hash = @passwordHash,
                    role = @role,
                    is_enabled = @isEnabled,
                    failed_login_count = @failedLoginCount,
                    lockout_until_utc_ms = @lockoutUntilUtcMs,
                    updated_at_utc_ms = @updatedAtUtcMs,
                    password_changed_at_utc_ms = @passwordChangedAtUtcMs,
                    last_login_at_utc_ms = @lastLoginAtUtcMs,
                    row_version = row_version + 1
                WHERE id = @id AND row_version = @expectedRowVersion;
                """;
            AddUserParameters(command, user);
            command.Parameters.AddWithValue("@expectedRowVersion", expectedRowVersion);

            var affected = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            if (affected != 1)
            {
                return UserRepositoryResult<AppUser>.Failure(
                    SecurityErrorCodes.UserRowVersionConflict,
                    "User row version conflict.");
            }

            var updated = await FindByIdOnConnectionAsync(connection, user.Id, cancellationToken).ConfigureAwait(false);
            return updated is null
                ? UserRepositoryResult<AppUser>.Failure(SecurityErrorCodes.UserNotFound, "User was not found after update.")
                : UserRepositoryResult<AppUser>.Success(updated);
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 19)
        {
            return UserRepositoryResult<AppUser>.Failure(
                SecurityErrorCodes.UserDuplicate,
                "User already exists.");
        }
        catch (Exception ex) when (IsRepositoryException(ex))
        {
            return UserRepositoryResult<AppUser>.Failure(
                SecurityErrorCodes.SecurityRepositoryFailed,
                "User update failed.");
        }
    }

    private async Task<SqliteConnection> OpenInitializedConnectionAsync(CancellationToken cancellationToken)
    {
        var migrationResult = await _migrationRunner.InitializeAsync(cancellationToken).ConfigureAwait(false);
        if (!migrationResult.Succeeded)
        {
            throw new InvalidOperationException(migrationResult.ErrorMessage ?? "Security database initialization failed.");
        }

        var connection = await _connectionFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
        var pragmaResult = await _pragmaInitializer
            .ApplyAsync(connection, GetBusyTimeoutMs(), cancellationToken)
            .ConfigureAwait(false);
        if (!pragmaResult.Succeeded)
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw new InvalidOperationException(pragmaResult.ErrorMessage ?? "Security database PRAGMA initialization failed.");
        }

        return connection;
    }

    private async Task<AppUser?> FindByIdOnConnectionAsync(
        SqliteConnection connection,
        Guid id,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = SelectUserSql + " WHERE id = @id;";
        command.Parameters.AddWithValue("@id", id.ToString("D"));

        return await ReadSingleAsync(command, cancellationToken).ConfigureAwait(false);
    }

    private static async Task InsertUserAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        AppUser user,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO app_user
            (id, username, normalized_username, password_hash, role, is_enabled,
             failed_login_count, lockout_until_utc_ms, created_at_utc_ms, updated_at_utc_ms,
             password_changed_at_utc_ms, last_login_at_utc_ms, row_version)
            VALUES
            (@id, @username, @normalizedUsername, @passwordHash, @role, @isEnabled,
             @failedLoginCount, @lockoutUntilUtcMs, @createdAtUtcMs, @updatedAtUtcMs,
             @passwordChangedAtUtcMs, @lastLoginAtUtcMs, @rowVersion);
            """;
        AddUserParameters(command, user);

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<long> CountUsersAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT COUNT(*) FROM app_user;";

        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false));
    }

    private static async Task<AppUser?> ReadSingleAsync(
        SqliteCommand command,
        CancellationToken cancellationToken)
    {
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        return ReadUser(reader);
    }

    private static AppUser ReadUser(SqliteDataReader reader)
        => new(
            reader.GetGuidFromString(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            (UserRole)reader.GetInt32(4),
            reader.GetInt32(5) != 0,
            reader.GetInt32(6),
            reader.GetNullableUtcFromUnixMilliseconds(7),
            reader.GetUtcFromUnixMilliseconds(8),
            reader.GetUtcFromUnixMilliseconds(9),
            reader.GetUtcFromUnixMilliseconds(10),
            reader.GetNullableUtcFromUnixMilliseconds(11),
            reader.GetInt64(12));

    private static void AddUserParameters(SqliteCommand command, AppUser user)
    {
        command.Parameters.AddWithValue("@id", user.Id.ToString("D"));
        command.Parameters.AddWithValue("@username", user.Username);
        command.Parameters.AddWithValue("@normalizedUsername", user.NormalizedUsername);
        command.Parameters.AddWithValue("@passwordHash", user.PasswordHash);
        command.Parameters.AddWithValue("@role", (int)user.Role);
        command.Parameters.AddWithValue("@isEnabled", user.IsEnabled ? 1 : 0);
        command.Parameters.AddWithValue("@failedLoginCount", user.FailedLoginCount);
        command.Parameters.AddWithValue("@lockoutUntilUtcMs", DbValue(user.LockoutUntilUtc));
        command.Parameters.AddWithValue("@createdAtUtcMs", user.CreatedAtUtc.ToUnixTimeMilliseconds());
        command.Parameters.AddWithValue("@updatedAtUtcMs", user.UpdatedAtUtc.ToUnixTimeMilliseconds());
        command.Parameters.AddWithValue("@passwordChangedAtUtcMs", user.PasswordChangedAtUtc.ToUnixTimeMilliseconds());
        command.Parameters.AddWithValue("@lastLoginAtUtcMs", DbValue(user.LastLoginAtUtc));
        command.Parameters.AddWithValue("@rowVersion", user.RowVersion);
    }

    private int GetBusyTimeoutMs()
        => Math.Max(1000, _options.Value.LockoutMinutes * 100);

    private static object DbValue(DateTimeOffset? value)
        => value?.ToUnixTimeMilliseconds() ?? (object)DBNull.Value;

    private static bool IsRepositoryException(Exception ex)
        => ex is SqliteException
            or InvalidOperationException
            or IOException
            or UnauthorizedAccessException
            or ArgumentException
            or NotSupportedException
            or PathTooLongException;

    private const string SelectUserSql = """
        SELECT id, username, normalized_username, password_hash, role, is_enabled,
               failed_login_count, lockout_until_utc_ms, created_at_utc_ms, updated_at_utc_ms,
               password_changed_at_utc_ms, last_login_at_utc_ms, row_version
        FROM app_user
        """;
}

internal static class SecuritySqliteDataReaderExtensions
{
    public static Guid GetGuidFromString(this SqliteDataReader reader, int ordinal)
        => Guid.Parse(reader.GetString(ordinal));

    public static DateTimeOffset GetUtcFromUnixMilliseconds(this SqliteDataReader reader, int ordinal)
        => DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(ordinal)).ToUniversalTime();

    public static DateTimeOffset? GetNullableUtcFromUnixMilliseconds(this SqliteDataReader reader, int ordinal)
        => reader.IsDBNull(ordinal)
            ? null
            : DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(ordinal)).ToUniversalTime();
}
