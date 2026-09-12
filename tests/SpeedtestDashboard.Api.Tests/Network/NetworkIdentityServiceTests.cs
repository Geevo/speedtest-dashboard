using System.Collections.Concurrent;
using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SpeedtestDashboard.Core.Network;
using SpeedtestDashboard.Infrastructure.Network;

namespace SpeedtestDashboard.Api.Tests.Network;

public sealed class NetworkIdentityServiceTests
{
    private static readonly IPAddress IPv4 = IPAddress.Parse("8.8.8.8");
    private static readonly IPAddress IPv6 = IPAddress.Parse("2606:4700:4700::1111");

    [Theory]
    [InlineData(true, false, NetworkIdentityState.Complete)]
    [InlineData(false, true, NetworkIdentityState.Complete)]
    [InlineData(true, true, NetworkIdentityState.Complete)]
    [InlineData(false, false, NetworkIdentityState.Unavailable)]
    public async Task AddressFamilyCombinations_ReturnHonestState(
        bool hasIPv4,
        bool hasIPv6,
        NetworkIdentityState expectedState)
    {
        var resolver = new FakePublicIpResolver
        {
            IPv4Result = hasIPv4 ? IPv4 : null,
            IPv6Result = hasIPv6 ? IPv6 : null
        };
        var service = CreateService(resolver);

        var result = await service.GetAsync(false, CancellationToken.None);

        Assert.Equal(expectedState, result.State);
        Assert.Equal(hasIPv4, result.IPv4 is not null);
        Assert.Equal(hasIPv6, result.IPv6 is not null);
    }

    [Fact]
    public async Task ConfiguredMetadataProvider_EnrichesFamiliesIndependently()
    {
        var resolver = new FakePublicIpResolver { IPv4Result = IPv4, IPv6Result = IPv6 };
        var metadata = new FakeMetadataProvider(address => Task.FromResult<IpMetadata?>(
            address.Equals(IPv4)
                ? new IpMetadata(address, "AS15169", "Google LLC", null, "US", "United States", null, null, "fixture")
                : null));
        var service = CreateService(resolver, metadata);

        var result = await service.GetAsync(false, CancellationToken.None);

        Assert.Equal(NetworkIdentityState.Partial, result.State);
        Assert.Equal("AS15169", result.IPv4?.Asn);
        Assert.Null(result.IPv6?.Asn);
        Assert.Equal("fixture", result.IPv4?.MetadataSource);
    }

    [Fact]
    public async Task MetadataUnavailable_PreservesPublicAddressAsPartial()
    {
        var service = CreateService(
            new FakePublicIpResolver { IPv4Result = IPv4 },
            new FakeMetadataProvider(_ => Task.FromResult<IpMetadata?>(null)));

        var result = await service.GetAsync(false, CancellationToken.None);

        Assert.Equal(NetworkIdentityState.Partial, result.State);
        Assert.Equal(IPv4.ToString(), result.IPv4?.Address);
        Assert.Null(result.IPv4?.Asn);
    }

    [Fact]
    public async Task CacheHit_DoesNotRepeatExternalLookups()
    {
        var resolver = new FakePublicIpResolver { IPv4Result = IPv4 };
        var service = CreateService(resolver);

        var first = await service.GetAsync(false, CancellationToken.None);
        var second = await service.GetAsync(false, CancellationToken.None);

        Assert.Same(first, second);
        Assert.Equal(1, resolver.IPv4Calls);
        Assert.Equal(1, resolver.IPv6Calls);
    }

    [Fact]
    public async Task CacheExpiry_PerformsAnotherLookup()
    {
        var clock = new ManualTimeProvider(new DateTimeOffset(2026, 9, 3, 12, 0, 0, TimeSpan.Zero));
        var resolver = new FakePublicIpResolver { IPv4Result = IPv4 };
        var service = CreateService(resolver, clock: clock, successCacheSeconds: 5);

        await service.GetAsync(false, CancellationToken.None);
        clock.Advance(TimeSpan.FromSeconds(6));
        await service.GetAsync(false, CancellationToken.None);

        Assert.Equal(2, resolver.IPv4Calls);
        Assert.Equal(2, resolver.IPv6Calls);
    }

    [Fact]
    public async Task ConcurrentCacheMisses_ShareOneLogicalRefresh()
    {
        var releaseLookup = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var resolver = new FakePublicIpResolver
        {
            IPv4Handler = async token =>
            {
                await releaseLookup.Task.WaitAsync(token);
                return IPv4;
            }
        };
        var service = CreateService(resolver);

        var requests = Enumerable.Range(0, 5)
            .Select(_ => service.GetAsync(false, CancellationToken.None))
            .ToArray();
        await WaitUntilAsync(() => resolver.IPv4Calls == 1);
        releaseLookup.SetResult();
        await Task.WhenAll(requests);

        Assert.Equal(1, resolver.IPv4Calls);
        Assert.Equal(1, resolver.IPv6Calls);
    }

    [Fact]
    public async Task ManualRefresh_BypassesValidCache()
    {
        var resolver = new FakePublicIpResolver { IPv4Result = IPv4 };
        var service = CreateService(resolver);

        await service.GetAsync(false, CancellationToken.None);
        await service.GetAsync(true, CancellationToken.None);

        Assert.Equal(2, resolver.IPv4Calls);
    }

    [Fact]
    public async Task ManualRefresh_AllowsFourRequestsWithinThrottleWindow()
    {
        var resolver = new FakePublicIpResolver { IPv4Result = IPv4 };
        var service = CreateService(resolver, refreshThrottleSeconds: 10);

        for (var attempt = 0; attempt < 4; attempt++)
        {
            await service.GetAsync(true, CancellationToken.None);
        }

        Assert.Equal(4, resolver.IPv4Calls);
    }

    [Fact]
    public async Task FifthManualRefresh_IsThrottledWithRetryDelay()
    {
        var clock = new ManualTimeProvider(new DateTimeOffset(2026, 9, 3, 12, 0, 0, TimeSpan.Zero));
        var resolver = new FakePublicIpResolver { IPv4Result = IPv4 };
        var service = CreateService(resolver, clock: clock, refreshThrottleSeconds: 10);

        for (var attempt = 0; attempt < 4; attempt++)
        {
            await service.GetAsync(true, CancellationToken.None);
        }

        clock.Advance(TimeSpan.FromSeconds(2));
        var exception = await Assert.ThrowsAsync<NetworkIdentityRefreshThrottledException>(
            () => service.GetAsync(true, CancellationToken.None));

        Assert.Equal(TimeSpan.FromSeconds(8), exception.RetryAfter);
        Assert.Equal(4, resolver.IPv4Calls);
    }

    [Fact]
    public async Task ManualRefresh_IsAllowedAgainWhenThrottleWindowMoves()
    {
        var clock = new ManualTimeProvider(new DateTimeOffset(2026, 9, 3, 12, 0, 0, TimeSpan.Zero));
        var resolver = new FakePublicIpResolver { IPv4Result = IPv4 };
        var service = CreateService(resolver, clock: clock, refreshThrottleSeconds: 10);

        for (var attempt = 0; attempt < 4; attempt++)
        {
            await service.GetAsync(true, CancellationToken.None);
        }

        clock.Advance(TimeSpan.FromSeconds(10));
        await service.GetAsync(true, CancellationToken.None);

        Assert.Equal(5, resolver.IPv4Calls);
    }

    [Fact]
    public async Task CompleteRefreshFailure_ReturnsLastKnownIdentityAsStale()
    {
        var resolver = new FakePublicIpResolver { IPv4Result = IPv4 };
        var service = CreateService(resolver);
        var first = await service.GetAsync(false, CancellationToken.None);
        resolver.IPv4Result = null;

        var refreshed = await service.GetAsync(true, CancellationToken.None);

        Assert.Equal(first.IPv4, refreshed.IPv4);
        Assert.True(refreshed.IsStale);
        Assert.Equal(NetworkIdentityWarning.RefreshFailed, refreshed.Warning);
    }

    [Fact]
    public async Task Cancellation_StopsAnInFlightRefresh()
    {
        var resolver = new FakePublicIpResolver
        {
            IPv4Handler = async token =>
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
                return IPv4;
            }
        };
        var service = CreateService(resolver);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(20));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => service.GetAsync(false, cancellation.Token));
    }

    private static NetworkIdentityService CreateService(
        FakePublicIpResolver resolver,
        IIpMetadataProvider? metadataProvider = null,
        ManualTimeProvider? clock = null,
        int successCacheSeconds = 300,
        int refreshThrottleSeconds = 10)
    {
        return new NetworkIdentityService(
            resolver,
            metadataProvider is null ? [] : [metadataProvider],
            Options.Create(new NetworkIdentityOptions
            {
                SuccessCacheSeconds = successCacheSeconds,
                FailureCacheSeconds = 30,
                RefreshThrottleSeconds = refreshThrottleSeconds
            }),
            clock ?? new ManualTimeProvider(DateTimeOffset.UtcNow),
            NullLogger<NetworkIdentityService>.Instance);
    }

    private static async Task WaitUntilAsync(Func<bool> predicate)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        while (!predicate())
        {
            await Task.Delay(5, timeout.Token);
        }
    }

    private sealed class FakePublicIpResolver : IPublicIpResolver
    {
        private int _ipv4Calls;
        private int _ipv6Calls;

        public IPAddress? IPv4Result { get; set; }

        public IPAddress? IPv6Result { get; set; }

        public Func<CancellationToken, Task<IPAddress?>>? IPv4Handler { get; set; }

        public int IPv4Calls => _ipv4Calls;

        public int IPv6Calls => _ipv6Calls;

        public Task<IPAddress?> ResolveIPv4Async(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _ipv4Calls);
            return IPv4Handler?.Invoke(cancellationToken) ?? Task.FromResult(IPv4Result);
        }

        public Task<IPAddress?> ResolveIPv6Async(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _ipv6Calls);
            return Task.FromResult(IPv6Result);
        }
    }

    private sealed class FakeMetadataProvider(
        Func<IPAddress, Task<IpMetadata?>> handler) : IIpMetadataProvider
    {
        private readonly ConcurrentDictionary<IPAddress, int> _calls = new();

        public async Task<IpMetadata?> GetMetadataAsync(IPAddress address, CancellationToken cancellationToken)
        {
            _calls.AddOrUpdate(address, 1, (_, count) => count + 1);
            return await handler(address);
        }
    }

    private sealed class ManualTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan duration) => _now += duration;
    }
}
