using System.Net;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SpeedtestDashboard.Core.Network;

namespace SpeedtestDashboard.Infrastructure.Network;

public sealed class NetworkIdentityService(
    IPublicIpResolver publicIpResolver,
    IEnumerable<IIpMetadataProvider> metadataProviders,
    IOptions<NetworkIdentityOptions> options,
    TimeProvider timeProvider,
    ILogger<NetworkIdentityService> logger) : INetworkIdentityService
{
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private readonly IIpMetadataProvider? _metadataProvider = metadataProviders.SingleOrDefault();
    private CacheEntry? _cache;
    private DateTimeOffset? _lastManualRefreshUtc;

    public async Task<NetworkIdentity> GetAsync(bool forceRefresh, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var cached = Volatile.Read(ref _cache);
        if (!forceRefresh && cached is not null && cached.ExpiresAtUtc > now)
        {
            return cached.Identity;
        }

        await _refreshGate.WaitAsync(cancellationToken);
        try
        {
            now = timeProvider.GetUtcNow();
            cached = Volatile.Read(ref _cache);

            if (forceRefresh)
            {
                EnforceManualRefreshThrottle(now);
                _lastManualRefreshUtc = now;
            }
            else if (cached is not null && cached.ExpiresAtUtc > now)
            {
                return cached.Identity;
            }

            var identity = await ResolveIdentityAsync(cancellationToken);
            if (identity.State == NetworkIdentityState.Unavailable &&
                cached?.Identity is { State: not NetworkIdentityState.Unavailable } previous)
            {
                identity = previous with
                {
                    IsStale = true,
                    Warning = NetworkIdentityWarning.RefreshFailed
                };
            }

            var ttlSeconds = identity.State == NetworkIdentityState.Unavailable || identity.IsStale
                ? options.Value.FailureCacheSeconds
                : options.Value.SuccessCacheSeconds;
            Volatile.Write(ref _cache, new CacheEntry(identity, now.AddSeconds(ttlSeconds)));

            return identity;
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    private void EnforceManualRefreshThrottle(DateTimeOffset now)
    {
        if (_lastManualRefreshUtc is not { } lastRefresh)
        {
            return;
        }

        var interval = TimeSpan.FromSeconds(options.Value.RefreshThrottleSeconds);
        var retryAfter = interval - (now - lastRefresh);
        if (retryAfter > TimeSpan.Zero)
        {
            throw new NetworkIdentityRefreshThrottledException(retryAfter);
        }
    }

    private async Task<NetworkIdentity> ResolveIdentityAsync(CancellationToken cancellationToken)
    {
        var ipv4Task = ResolveAddressAsync(publicIpResolver.ResolveIPv4Async, "IPv4", cancellationToken);
        var ipv6Task = ResolveAddressAsync(publicIpResolver.ResolveIPv6Async, "IPv6", cancellationToken);
        await Task.WhenAll(ipv4Task, ipv6Task);

        var ipv4 = await ipv4Task;
        var ipv6 = await ipv6Task;

        var ipv4MetadataTask = GetMetadataAsync(ipv4.Value, "IPv4", cancellationToken);
        var ipv6MetadataTask = GetMetadataAsync(ipv6.Value, "IPv6", cancellationToken);
        await Task.WhenAll(ipv4MetadataTask, ipv6MetadataTask);

        var ipv4Metadata = await ipv4MetadataTask;
        var ipv6Metadata = await ipv6MetadataTask;
        var hasAddress = ipv4.Value is not null || ipv6.Value is not null;
        var hasFailure = ipv4.Failed || ipv6.Failed || ipv4Metadata.Failed || ipv6Metadata.Failed;

        var state = !hasAddress
            ? NetworkIdentityState.Unavailable
            : hasFailure
                ? NetworkIdentityState.Partial
                : NetworkIdentityState.Complete;

        return new NetworkIdentity(
            ToIdentity(ipv4.Value, NetworkAddressFamily.IPv4, ipv4Metadata.Value),
            ToIdentity(ipv6.Value, NetworkAddressFamily.IPv6, ipv6Metadata.Value),
            timeProvider.GetUtcNow(),
            state);
    }

    private async Task<LookupResult<IPAddress>> ResolveAddressAsync(
        Func<CancellationToken, Task<IPAddress?>> resolver,
        string family,
        CancellationToken cancellationToken)
    {
        try
        {
            return new LookupResult<IPAddress>(await resolver(cancellationToken), Failed: false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Public {AddressFamily} resolution failed.", family);
            return new LookupResult<IPAddress>(Value: null, Failed: true);
        }
    }

    private async Task<LookupResult<IpMetadata>> GetMetadataAsync(
        IPAddress? address,
        string family,
        CancellationToken cancellationToken)
    {
        if (address is null || _metadataProvider is null)
        {
            return new LookupResult<IpMetadata>(Value: null, Failed: false);
        }

        try
        {
            var metadata = await _metadataProvider.GetMetadataAsync(address, cancellationToken);
            return new LookupResult<IpMetadata>(metadata, Failed: metadata is null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Public {AddressFamily} metadata enrichment failed.", family);
            return new LookupResult<IpMetadata>(Value: null, Failed: true);
        }
    }

    private static NetworkAddressIdentity? ToIdentity(
        IPAddress? address,
        NetworkAddressFamily family,
        IpMetadata? metadata)
    {
        if (address is null)
        {
            return null;
        }

        return new NetworkAddressIdentity(
            address.ToString(),
            family,
            metadata?.Asn,
            metadata?.AsName,
            metadata?.Isp,
            metadata?.CountryCode,
            metadata?.CountryName,
            metadata?.Region,
            metadata?.City,
            AddressSource: "ipify",
            MetadataSource: metadata?.Source);
    }

    private sealed record CacheEntry(NetworkIdentity Identity, DateTimeOffset ExpiresAtUtc);

    private sealed record LookupResult<T>(T? Value, bool Failed) where T : class;
}

