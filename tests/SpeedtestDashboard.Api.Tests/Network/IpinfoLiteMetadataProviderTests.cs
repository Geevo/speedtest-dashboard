using System.Net;
using System.Net.Http.Headers;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SpeedtestDashboard.Core.Network;
using SpeedtestDashboard.Infrastructure.Network;

namespace SpeedtestDashboard.Api.Tests.Network;

public sealed class IpinfoLiteMetadataProviderTests
{
    [Fact]
    public async Task FullFixture_MapsOnlySupportedFieldsAndKeepsTokenOutOfUrl()
    {
        HttpRequestMessage? capturedRequest = null;
        using var factory = CreateFactory(async (request, _) =>
        {
            capturedRequest = request;
            return JsonResponse(await ReadFixtureAsync("ipinfo-full.json"));
        });
        var provider = CreateProvider(factory);

        var metadata = await provider.GetMetadataAsync(
            IPAddress.Parse("8.8.8.8"),
            CancellationToken.None);

        Assert.NotNull(metadata);
        Assert.Equal("AS15169", metadata.Asn);
        Assert.Equal("Google LLC", metadata.AsName);
        Assert.Equal("US", metadata.CountryCode);
        Assert.Equal("United States", metadata.CountryName);
        Assert.Null(metadata.Isp);
        Assert.Null(metadata.Region);
        Assert.Null(metadata.City);
        Assert.Equal("IPinfo Lite", metadata.Source);
        Assert.Equal(new AuthenticationHeaderValue("Bearer", "test-token"), capturedRequest?.Headers.Authorization);
        Assert.DoesNotContain("test-token", capturedRequest?.RequestUri?.ToString());
    }

    [Fact]
    public async Task MissingOptionalFields_ReturnsAddressOnlyMetadata()
    {
        using var factory = CreateFixtureFactory("ipinfo-missing.json");
        var metadata = await CreateProvider(factory).GetMetadataAsync(
            IPAddress.Parse("8.8.8.8"),
            CancellationToken.None);

        Assert.NotNull(metadata);
        Assert.Null(metadata.Asn);
        Assert.Null(metadata.AsName);
        Assert.Null(metadata.CountryCode);
    }

    [Fact]
    public async Task MalformedAsn_IsOmittedWithoutDiscardingValidMetadata()
    {
        using var factory = CreateFixtureFactory("ipinfo-malformed-asn.json");
        var metadata = await CreateProvider(factory).GetMetadataAsync(
            IPAddress.Parse("8.8.8.8"),
            CancellationToken.None);

        Assert.NotNull(metadata);
        Assert.Null(metadata.Asn);
        Assert.Equal("Google LLC", metadata.AsName);
        Assert.Equal("US", metadata.CountryCode);
    }

    [Fact]
    public async Task UnknownCountryFields_AreOmittedTogether()
    {
        using var factory = CreateFixtureFactory("ipinfo-unknown-country.json");
        var metadata = await CreateProvider(factory).GetMetadataAsync(
            IPAddress.Parse("8.8.8.8"),
            CancellationToken.None);

        Assert.NotNull(metadata);
        Assert.Null(metadata.CountryCode);
        Assert.Null(metadata.CountryName);
    }

    [Fact]
    public async Task MalformedJson_ReturnsNull()
    {
        using var factory = CreateFixtureFactory("ipinfo-malformed.json");

        Assert.Null(await CreateProvider(factory).GetMetadataAsync(
            IPAddress.Parse("8.8.8.8"),
            CancellationToken.None));
    }

    [Fact]
    public async Task UpstreamFailure_ReturnsNull()
    {
        using var factory = CreateFactory((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)));

        Assert.Null(await CreateProvider(factory).GetMetadataAsync(
            IPAddress.Parse("8.8.8.8"),
            CancellationToken.None));
    }

    private static FakeHttpClientFactory CreateFixtureFactory(string name) => CreateFactory(async (_, _) =>
        JsonResponse(await ReadFixtureAsync(name)));

    private static FakeHttpClientFactory CreateFactory(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
    {
        var factory = new FakeHttpClientFactory();
        factory.Add(IpinfoLiteMetadataProvider.ClientName, new StubHttpMessageHandler(handler));
        return factory;
    }

    private static IpinfoLiteMetadataProvider CreateProvider(IHttpClientFactory factory) => new(
        factory,
        Options.Create(new NetworkIdentityOptions { Ipinfo = new IpinfoOptions { Token = "test-token" } }),
        NullLogger<IpinfoLiteMetadataProvider>.Instance);

    private static HttpResponseMessage JsonResponse(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
    };

    private static Task<string> ReadFixtureAsync(string name) =>
        File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));
}
