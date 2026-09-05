using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using SpeedtestDashboard.Infrastructure.Network;

namespace SpeedtestDashboard.Api.Tests.Network;

public sealed class IpifyPublicIpResolverTests
{
    [Fact]
    public async Task ResolveIPv4_AcceptsPublicIPv4()
    {
        using var factory = CreateFactory("8.8.8.8", "2606:4700:4700::1111");
        var resolver = CreateResolver(factory);

        Assert.Equal(IPAddress.Parse("8.8.8.8"), await resolver.ResolveIPv4Async(CancellationToken.None));
    }

    [Fact]
    public async Task ResolveIPv6_AcceptsPublicIPv6()
    {
        using var factory = CreateFactory("8.8.8.8", "2606:4700:4700::1111");
        var resolver = CreateResolver(factory);

        Assert.Equal(IPAddress.Parse("2606:4700:4700::1111"), await resolver.ResolveIPv6Async(CancellationToken.None));
    }

    [Theory]
    [InlineData("2606:4700:4700::1111")]
    [InlineData("not-an-address")]
    [InlineData("")]
    [InlineData("127.0.0.1")]
    [InlineData("10.0.0.1")]
    public async Task ResolveIPv4_RejectsWrongFamilyMalformedEmptyAndNonPublic(string response)
    {
        using var factory = CreateFactory(response, "2606:4700:4700::1111");
        var resolver = CreateResolver(factory);

        Assert.Null(await resolver.ResolveIPv4Async(CancellationToken.None));
    }

    [Theory]
    [InlineData("8.8.8.8")]
    [InlineData("not-an-address")]
    [InlineData("")]
    [InlineData("::1")]
    [InlineData("fe80::1")]
    public async Task ResolveIPv6_RejectsWrongFamilyMalformedEmptyAndNonPublic(string response)
    {
        using var factory = CreateFactory("8.8.8.8", response);
        var resolver = CreateResolver(factory);

        Assert.Null(await resolver.ResolveIPv6Async(CancellationToken.None));
    }

    [Fact]
    public async Task Resolve_ReturnsNullForUpstreamFailure()
    {
        using var factory = new FakeHttpClientFactory();
        factory.Add(IpifyPublicIpResolver.IPv4ClientName, new StubHttpMessageHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadGateway))));
        factory.Add(IpifyPublicIpResolver.IPv6ClientName, new StubHttpMessageHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadGateway))));
        var resolver = CreateResolver(factory);

        Assert.Null(await resolver.ResolveIPv4Async(CancellationToken.None));
        Assert.Null(await resolver.ResolveIPv6Async(CancellationToken.None));
    }

    [Fact]
    public async Task Resolve_ReturnsNullWhenHttpClientTimesOut()
    {
        using var factory = new FakeHttpClientFactory();
        factory.Add(
            IpifyPublicIpResolver.IPv4ClientName,
            new StubHttpMessageHandler(async (_, token) =>
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
                return new HttpResponseMessage(HttpStatusCode.OK);
            }),
            TimeSpan.FromMilliseconds(20));
        factory.Add(IpifyPublicIpResolver.IPv6ClientName, Response("2606:4700:4700::1111"));
        var resolver = CreateResolver(factory);

        Assert.Null(await resolver.ResolveIPv4Async(CancellationToken.None));
    }

    [Fact]
    public async Task Resolve_PropagatesCallerCancellation()
    {
        using var factory = new FakeHttpClientFactory();
        factory.Add(IpifyPublicIpResolver.IPv4ClientName, new StubHttpMessageHandler(async (_, token) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }));
        factory.Add(IpifyPublicIpResolver.IPv6ClientName, Response("2606:4700:4700::1111"));
        var resolver = CreateResolver(factory);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(20));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => resolver.ResolveIPv4Async(cancellation.Token));
    }

    private static FakeHttpClientFactory CreateFactory(string ipv4, string ipv6)
    {
        var factory = new FakeHttpClientFactory();
        factory.Add(IpifyPublicIpResolver.IPv4ClientName, Response(ipv4));
        factory.Add(IpifyPublicIpResolver.IPv6ClientName, Response(ipv6));
        return factory;
    }

    private static StubHttpMessageHandler Response(string content) => new((_, _) =>
        Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(content)
        }));

    private static IpifyPublicIpResolver CreateResolver(IHttpClientFactory factory) =>
        new(factory, NullLogger<IpifyPublicIpResolver>.Instance);
}
