using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using SpeedtestDashboard.Infrastructure.Persistence;

namespace SpeedtestDashboard.Infrastructure.ApiKeys;

public sealed class ApiCredentialService : IApiCredentialService
{
    public const string DataProtectionPurpose = "SpeedtestDashboard.ApiCredential.v1";
    private const string KeyPrefix = "std_";
    private const int SecretEntropyBytes = 32;
    private static readonly TimeSpan LastUsedWriteThrottle = TimeSpan.FromMinutes(1);

    private readonly SqliteConnectionFactory _connectionFactory;
    private readonly IDataProtector _protector;
    private readonly TimeProvider _timeProvider;
    private long _lastPersistedTouchTicks;

    public ApiCredentialService(
        SqliteConnectionFactory connectionFactory,
        IDataProtectionProvider dataProtectionProvider,
        TimeProvider timeProvider)
    {
        _connectionFactory = connectionFactory;
        _protector = dataProtectionProvider.CreateProtector(DataProtectionPurpose);
        _timeProvider = timeProvider;
    }

    public async Task<ApiCredentialSnapshot?> GetAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = _connectionFactory.CreateCommand(connection,
            "SELECT ProtectedSecret, CreatedAtUtc, LastUsedAtUtc FROM ApiCredentials WHERE Id = 1;");
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return TryUnprotect(reader.GetString(0), out var key)
            ? new ApiCredentialSnapshot(key, reader.GetDateTimeOffset(1), reader.IsDBNull(2) ? null : reader.GetDateTimeOffset(2))
            : null;
    }

    public async Task<ApiCredentialSnapshot> RegenerateAsync(CancellationToken cancellationToken = default)
    {
        var secret = GenerateSecret();
        var protectedSecret = _protector.Protect(secret);
        var now = _timeProvider.GetUtcNow();

        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = _connectionFactory.CreateCommand(connection, """
            INSERT INTO ApiCredentials (Id, ProtectedSecret, CreatedAtUtc, LastUsedAtUtc) VALUES (1, @secret, @created, NULL)
            ON CONFLICT(Id) DO UPDATE SET ProtectedSecret = excluded.ProtectedSecret, CreatedAtUtc = excluded.CreatedAtUtc, LastUsedAtUtc = NULL;
            """);
        command.Parameters.AddWithValue("@secret", protectedSecret);
        command.Parameters.AddWithValue("@created", now);
        await command.ExecuteNonQueryAsync(cancellationToken);

        Interlocked.Exchange(ref _lastPersistedTouchTicks, 0);
        return new ApiCredentialSnapshot(secret, now, null);
    }

    public async Task<bool> RevokeAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = _connectionFactory.CreateCommand(connection, "DELETE FROM ApiCredentials WHERE Id = 1;");
        var deleted = await command.ExecuteNonQueryAsync(cancellationToken);
        return deleted > 0;
    }

    public async Task<bool> ValidateAsync(string presentedKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(presentedKey) || presentedKey.Length > 512)
        {
            return false;
        }

        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = _connectionFactory.CreateCommand(connection, "SELECT ProtectedSecret FROM ApiCredentials WHERE Id = 1;");
        var protectedSecret = await command.ExecuteScalarAsync(cancellationToken) as string;
        if (protectedSecret is null || !TryUnprotect(protectedSecret, out var actualKey))
        {
            return false;
        }

        if (!FixedTimeEquals(presentedKey, actualKey))
        {
            return false;
        }

        await TouchLastUsedAsync(cancellationToken);
        return true;
    }

    private async Task TouchLastUsedAsync(CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow();
        var nowTicks = now.UtcTicks;
        var lastTicks = Interlocked.Read(ref _lastPersistedTouchTicks);
        if (lastTicks != 0 && now - new DateTimeOffset(lastTicks, TimeSpan.Zero) < LastUsedWriteThrottle)
        {
            return;
        }

        if (Interlocked.CompareExchange(ref _lastPersistedTouchTicks, nowTicks, lastTicks) != lastTicks)
        {
            return;
        }

        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = _connectionFactory.CreateCommand(connection,
            "UPDATE ApiCredentials SET LastUsedAtUtc = @now WHERE Id = 1;");
        command.Parameters.AddWithValue("@now", now);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private bool TryUnprotect(string protectedSecret, out string key)
    {
        try
        {
            key = _protector.Unprotect(protectedSecret);
            return true;
        }
        catch (CryptographicException)
        {
            key = string.Empty;
            return false;
        }
    }

    private static bool FixedTimeEquals(string presented, string actual)
    {
        var presentedBytes = Encoding.UTF8.GetBytes(presented);
        var actualBytes = Encoding.UTF8.GetBytes(actual);
        if (presentedBytes.Length != actualBytes.Length)
        {
            CryptographicOperations.FixedTimeEquals(actualBytes, actualBytes);
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(presentedBytes, actualBytes);
    }

    private static string GenerateSecret()
    {
        Span<byte> buffer = stackalloc byte[SecretEntropyBytes];
        RandomNumberGenerator.Fill(buffer);
        return KeyPrefix + Base64UrlEncode(buffer);
    }

    private static string Base64UrlEncode(ReadOnlySpan<byte> bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
