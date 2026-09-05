namespace SpeedtestDashboard.Infrastructure.ApiKeys;

public sealed record ApiCredentialSnapshot(string Key, DateTimeOffset CreatedAtUtc, DateTimeOffset? LastUsedAtUtc);

public interface IApiCredentialService
{
    Task<ApiCredentialSnapshot?> GetAsync(CancellationToken cancellationToken = default);

    Task<ApiCredentialSnapshot> RegenerateAsync(CancellationToken cancellationToken = default);

    Task<bool> RevokeAsync(CancellationToken cancellationToken = default);

    Task<bool> ValidateAsync(string presentedKey, CancellationToken cancellationToken = default);
}
