using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using SpeedtestDashboard.Infrastructure.Persistence;
using SpeedtestDashboard.Infrastructure.Persistence.Entities;

namespace SpeedtestDashboard.Infrastructure.ApiKeys;

public sealed class ApiCredentialService : IApiCredentialService
{
    public const string DataProtectionPurpose = "SpeedtestDashboard.ApiCredential.v1";
    private const string KeyPrefix = "std_";
    private const int SecretEntropyBytes = 32;
    private static readonly TimeSpan LastUsedWriteThrottle = TimeSpan.FromMinutes(1);

    private readonly IDbContextFactory<DashboardDbContext> _contextFactory;
    private readonly IDataProtector _protector;
    private readonly TimeProvider _timeProvider;
    private long _lastPersistedTouchTicks;

    public ApiCredentialService(
        IDbContextFactory<DashboardDbContext> contextFactory,
        IDataProtectionProvider dataProtectionProvider,
        TimeProvider timeProvider)
    {
        _contextFactory = contextFactory;
        _protector = dataProtectionProvider.CreateProtector(DataProtectionPurpose);
        _timeProvider = timeProvider;
    }

    public async Task<ApiCredentialSnapshot?> GetAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await context.ApiCredentials.SingleOrDefaultAsync(cancellationToken);
        if (entity is null)
        {
            return null;
        }

        return TryUnprotect(entity.ProtectedSecret, out var key)
            ? new ApiCredentialSnapshot(key, entity.CreatedAtUtc, entity.LastUsedAtUtc)
            : null;
    }

    public async Task<ApiCredentialSnapshot> RegenerateAsync(CancellationToken cancellationToken = default)
    {
        var secret = GenerateSecret();
        var protectedSecret = _protector.Protect(secret);
        var now = _timeProvider.GetUtcNow();

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await context.ApiCredentials.SingleOrDefaultAsync(cancellationToken);
        if (entity is null)
        {
            entity = new ApiCredentialEntity { Id = ApiCredentialEntity.SingletonId };
            context.ApiCredentials.Add(entity);
        }

        entity.ProtectedSecret = protectedSecret;
        entity.CreatedAtUtc = now;
        entity.LastUsedAtUtc = null;
        await context.SaveChangesAsync(cancellationToken);

        Interlocked.Exchange(ref _lastPersistedTouchTicks, 0);
        return new ApiCredentialSnapshot(secret, now, null);
    }

    public async Task<bool> RevokeAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var deleted = await context.ApiCredentials
            .Where(credential => credential.Id == ApiCredentialEntity.SingletonId)
            .ExecuteDeleteAsync(cancellationToken);
        return deleted > 0;
    }

    public async Task<bool> ValidateAsync(string presentedKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(presentedKey) || presentedKey.Length > 512)
        {
            return false;
        }

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await context.ApiCredentials.SingleOrDefaultAsync(cancellationToken);
        if (entity is null || !TryUnprotect(entity.ProtectedSecret, out var actualKey))
        {
            return false;
        }

        if (!FixedTimeEquals(presentedKey, actualKey))
        {
            return false;
        }

        await TouchLastUsedAsync(entity.Id, cancellationToken);
        return true;
    }

    private async Task TouchLastUsedAsync(int id, CancellationToken cancellationToken)
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

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        await context.ApiCredentials
            .Where(credential => credential.Id == id)
            .ExecuteUpdateAsync(setters => setters.SetProperty(credential => credential.LastUsedAtUtc, now), cancellationToken);
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
