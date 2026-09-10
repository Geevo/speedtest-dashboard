namespace SpeedtestDashboard.Infrastructure.ApiKeys;

public sealed record ApiIdempotencyRecord(string Key, string RequestHash, Guid JobId, DateTimeOffset CreatedAtUtc);

public interface IApiIdempotencyStore
{
    // Hold the lease across lookup, submission, and persistence for the same key.
    ValueTask<IDisposable> AcquireAsync(string key, CancellationToken cancellationToken = default);

    Task<ApiIdempotencyRecord?> TryGetAsync(string key, CancellationToken cancellationToken = default);

    Task SaveAsync(string key, string requestHash, Guid jobId, CancellationToken cancellationToken = default);
}
