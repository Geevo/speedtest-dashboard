namespace SpeedtestDashboard.Infrastructure.ApiKeys;

public sealed record ApiIdempotencyRecord(string Key, string RequestHash, Guid JobId, DateTimeOffset CreatedAtUtc);

public interface IApiIdempotencyStore
{
    Task<ApiIdempotencyRecord?> TryGetAsync(string key, CancellationToken cancellationToken = default);

    Task SaveAsync(string key, string requestHash, Guid jobId, CancellationToken cancellationToken = default);
}
