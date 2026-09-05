namespace SpeedtestDashboard.Core.Network;

public interface INetworkIdentityService
{
    Task<NetworkIdentity> GetAsync(bool forceRefresh, CancellationToken cancellationToken);
}

public sealed class NetworkIdentityRefreshThrottledException(TimeSpan retryAfter)
    : Exception("Network identity was refreshed too recently.")
{
    public TimeSpan RetryAfter { get; } = retryAfter;
}

