using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using SpeedtestDashboard.Infrastructure.Persistence;

namespace SpeedtestDashboard.Infrastructure.Authentication;

public enum LocalLoginStatus { Succeeded, InvalidCredentials, LockedOut }
public sealed record LocalLoginResult(LocalLoginStatus Status, ApplicationUser? User = null);
public enum LocalSetupStatus { Succeeded, InvalidCredentials, InvalidAccountState }
public sealed record LocalSetupResult(LocalSetupStatus Status, ApplicationUser? User = null, bool ShowDisabledWarning = true);
public sealed record LocalSettings(bool AuthenticationEnabled, bool ShowDisabledWarning, bool AccountConfigured);

public sealed class LocalAccountService(
    SqliteConnectionFactory connectionFactory,
    IPasswordHasher<ApplicationUser> passwordHasher,
    TimeProvider timeProvider)
{
    private const int MaximumFailedAttempts = 5;
    private static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);

    public async Task<LocalSettings> GetSettingsAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connectionFactory.CreateCommand(connection, """
            SELECT AuthenticationEnabled, ShowAuthenticationDisabledWarning,
                   EXISTS(SELECT 1 FROM Users) FROM DashboardSettings WHERE Id = 1;
            """);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) throw new InvalidOperationException("Dashboard settings are missing.");
        return new LocalSettings(reader.GetBoolean(0), reader.GetBoolean(1), reader.GetBoolean(2));
    }

    public async Task<int> GetAccountCountAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connectionFactory.CreateCommand(connection, "SELECT COUNT(*) FROM Users;");
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
    }

    public async Task<ApplicationUser?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        return await FindAsync(connection, "Id = @value", id, null, cancellationToken);
    }

    public async Task<LocalLoginResult> AuthenticateAsync(string username, string password, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        var user = await FindAsync(connection, "NormalizedUserName = @value", Normalize(username), transaction, cancellationToken);
        if (user is null) return new(LocalLoginStatus.InvalidCredentials);
        var now = timeProvider.GetUtcNow();
        if (user.LockoutEnd is not null && user.LockoutEnd > now) return new(LocalLoginStatus.LockedOut);

        var verification = passwordHasher.VerifyHashedPassword(user, user.PasswordHash!, password);
        if (verification == PasswordVerificationResult.Failed)
        {
            user.AccessFailedCount++;
            if (user.AccessFailedCount >= MaximumFailedAttempts)
            {
                user.AccessFailedCount = 0;
                user.LockoutEnd = now + LockoutDuration;
            }
            await UpdateLoginStateAsync(connection, transaction, user, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new(user.LockoutEnd > now ? LocalLoginStatus.LockedOut : LocalLoginStatus.InvalidCredentials);
        }

        user.AccessFailedCount = 0;
        user.LockoutEnd = null;
        user.LastLoginAtUtc = now;
        if (verification == PasswordVerificationResult.SuccessRehashNeeded)
            user.PasswordHash = passwordHasher.HashPassword(user, password);
        await UpdateLoginStateAsync(connection, transaction, user, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new(LocalLoginStatus.Succeeded, user);
    }

    public async Task<LocalSetupResult> EnableAsync(string username, string password, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        var users = await ReadUsersAsync(connection, transaction, 2, cancellationToken);
        if (users.Count > 1) return new(LocalSetupStatus.InvalidAccountState);

        ApplicationUser user;
        if (users.Count == 0)
        {
            user = new ApplicationUser
            {
                Id = Guid.NewGuid(),
                UserName = username,
                NormalizedUserName = Normalize(username),
                SecurityStamp = Guid.NewGuid().ToString("N"),
                CreatedAtUtc = timeProvider.GetUtcNow(),
                LockoutEnabled = true
            };
            user.PasswordHash = passwordHasher.HashPassword(user, password);
            await InsertAsync(connection, transaction, user, cancellationToken);
        }
        else
        {
            user = users[0];
            if (user.NormalizedUserName != Normalize(username) ||
                passwordHasher.VerifyHashedPassword(user, user.PasswordHash!, password) == PasswordVerificationResult.Failed)
                return new(LocalSetupStatus.InvalidCredentials);
        }

        user.LastLoginAtUtc = timeProvider.GetUtcNow();
        await UpdateLoginStateAsync(connection, transaction, user, cancellationToken);
        await using var settings = connectionFactory.CreateCommand(connection,
            "UPDATE DashboardSettings SET AuthenticationEnabled = 1 WHERE Id = 1 RETURNING ShowAuthenticationDisabledWarning;", transaction);
        var warning = Convert.ToBoolean(await settings.ExecuteScalarAsync(cancellationToken));
        await transaction.CommitAsync(cancellationToken);
        return new(LocalSetupStatus.Succeeded, user, warning);
    }

    public async Task<bool> DisableAsync(Guid userId, string password, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        var user = await FindAsync(connection, "Id = @value", userId, transaction, cancellationToken);
        if (user is null || passwordHasher.VerifyHashedPassword(user, user.PasswordHash!, password) == PasswordVerificationResult.Failed)
            return false;
        await using (var settings = connectionFactory.CreateCommand(connection,
            "UPDATE DashboardSettings SET AuthenticationEnabled = 0 WHERE Id = 1;", transaction))
            await settings.ExecuteNonQueryAsync(cancellationToken);
        await using (var delete = connectionFactory.CreateCommand(connection, "DELETE FROM Users WHERE Id = @id;", transaction))
        {
            delete.Parameters.AddWithValue("@id", userId);
            await delete.ExecuteNonQueryAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    public async Task UpdatePreferenceAsync(bool showWarning, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connectionFactory.CreateCommand(connection,
            "UPDATE DashboardSettings SET ShowAuthenticationDisabledWarning = @value WHERE Id = 1;");
        command.Parameters.AddWithValue("@value", showWarning);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<ApplicationUser?> ChangePasswordAsync(Guid userId, string currentPassword, string newPassword,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        var user = await FindAsync(connection, "Id = @value", userId, transaction, cancellationToken);
        if (user is null || passwordHasher.VerifyHashedPassword(user, user.PasswordHash!, currentPassword) == PasswordVerificationResult.Failed)
            return null;
        user.PasswordHash = passwordHasher.HashPassword(user, newPassword);
        user.SecurityStamp = Guid.NewGuid().ToString("N");
        await using var command = connectionFactory.CreateCommand(connection,
            "UPDATE Users SET PasswordHash = @hash, SecurityStamp = @stamp WHERE Id = @id;", transaction);
        command.Parameters.AddWithValue("@hash", user.PasswordHash);
        command.Parameters.AddWithValue("@stamp", user.SecurityStamp);
        command.Parameters.AddWithValue("@id", user.Id);
        await command.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return user;
    }

    private async Task<List<ApplicationUser>> ReadUsersAsync(SqliteConnection connection, SqliteTransaction transaction,
        int limit, CancellationToken cancellationToken)
    {
        await using var command = connectionFactory.CreateCommand(connection,
            $"SELECT {Columns} FROM Users ORDER BY Id LIMIT @limit;", transaction);
        command.Parameters.AddWithValue("@limit", limit);
        var users = new List<ApplicationUser>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) users.Add(Read(reader));
        return users;
    }

    private async Task<ApplicationUser?> FindAsync(SqliteConnection connection, string predicate, object value,
        SqliteTransaction? transaction, CancellationToken cancellationToken)
    {
        await using var command = connectionFactory.CreateCommand(connection,
            $"SELECT {Columns} FROM Users WHERE {predicate};", transaction);
        command.Parameters.AddWithValue("@value", value);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? Read(reader) : null;
    }

    private async Task InsertAsync(SqliteConnection connection, SqliteTransaction transaction, ApplicationUser user,
        CancellationToken cancellationToken)
    {
        await using var command = connectionFactory.CreateCommand(connection, """
            INSERT INTO Users (Id, UserName, NormalizedUserName, PasswordHash, SecurityStamp, AccessFailedCount, LockoutEndUtc, CreatedAtUtc, LastLoginAtUtc)
            VALUES (@id, @name, @normalized, @hash, @stamp, @failures, @lockout, @created, @lastLogin);
            """, transaction);
        Bind(command, user);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task UpdateLoginStateAsync(SqliteConnection connection, SqliteTransaction transaction,
        ApplicationUser user, CancellationToken cancellationToken)
    {
        await using var command = connectionFactory.CreateCommand(connection, """
            UPDATE Users SET PasswordHash = @hash, AccessFailedCount = @failures, LockoutEndUtc = @lockout,
                LastLoginAtUtc = @lastLogin WHERE Id = @id;
            """, transaction);
        Bind(command, user);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void Bind(SqliteCommand command, ApplicationUser user)
    {
        Add(command, "@id", user.Id); Add(command, "@name", user.UserName); Add(command, "@normalized", user.NormalizedUserName);
        Add(command, "@hash", user.PasswordHash); Add(command, "@stamp", user.SecurityStamp);
        Add(command, "@failures", user.AccessFailedCount); Add(command, "@lockout", user.LockoutEnd);
        Add(command, "@created", user.CreatedAtUtc); Add(command, "@lastLogin", user.LastLoginAtUtc);
    }

    private static ApplicationUser Read(SqliteDataReader reader) => new()
    {
        Id = reader.GetGuid(0),
        UserName = reader.GetString(1),
        NormalizedUserName = reader.GetString(2),
        PasswordHash = reader.GetString(3),
        SecurityStamp = reader.GetString(4),
        AccessFailedCount = reader.GetInt32(5),
        LockoutEnd = reader.IsDBNull(6) ? null : reader.GetDateTimeOffset(6),
        CreatedAtUtc = reader.GetDateTimeOffset(7),
        LastLoginAtUtc = reader.IsDBNull(8) ? null : reader.GetDateTimeOffset(8),
        LockoutEnabled = true
    };

    private const string Columns = "Id, UserName, NormalizedUserName, PasswordHash, SecurityStamp, AccessFailedCount, LockoutEndUtc, CreatedAtUtc, LastLoginAtUtc";
    private static string Normalize(string username) => username.Normalize().ToUpperInvariant();
    private static void Add(SqliteCommand command, string name, object? value) => command.Parameters.AddWithValue(name, value ?? DBNull.Value);
}
