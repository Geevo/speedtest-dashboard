using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using SpeedtestDashboard.Infrastructure.Network;

namespace SpeedtestDashboard.Api.Tests.Network;

public sealed class IpConfigIoMetadataProviderTests
{
    [Fact]
    public async Task FullFixture_MapsSupportedFieldsWithoutAuthentication()
    {
        HttpRequestMessage? capturedRequest = null;
        using var factory = CreateFactory(async (request, _) =>
        {
            capturedRequest = request;
            return JsonResponse(await ReadFixtureAsync("ipconfig-full.json"));
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
        // Recorded from the live service for an anycast address, which the address database
        // cannot place. Unmapped response fields are ignored rather than rejected.
        Assert.Null(metadata.Region);
        Assert.Null(metadata.City);
        Assert.Equal("IPConfig.io", metadata.Source);
        Assert.Null(capturedRequest?.Headers.Authorization);
        Assert.Equal("/json?ip=8.8.8.8", capturedRequest?.RequestUri?.PathAndQuery);
    }

    [Fact]
    public async Task LocatableAddress_MapsRegionAndCityWhenSupplied()
    {
        using var factory = CreateFixtureFactory("ipconfig-city.json");

        var metadata = await CreateProvider(factory).GetMetadataAsync(
            IPAddress.Parse("8.8.8.8"),
            CancellationToken.None);

        Assert.NotNull(metadata);
        Assert.Equal("California", metadata.Region);
        Assert.Equal("Mountain View", metadata.City);
        Assert.Equal("AS15169", metadata.Asn);
        Assert.Equal("US", metadata.CountryCode);
    }

    [Fact]
    public async Task MissingOptionalFields_ReturnsAddressOnlyMetadata()
    {
        using var factory = CreateFixtureFactory("ipconfig-missing.json");
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
        using var factory = CreateFixtureFactory("ipconfig-malformed-asn.json");
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
        using var factory = CreateFixtureFactory("ipconfig-unknown-country.json");
        var metadata = await CreateProvider(factory).GetMetadataAsync(
            IPAddress.Parse("8.8.8.8"),
            CancellationToken.None);

        Assert.NotNull(metadata);
        Assert.Null(metadata.CountryCode);
        Assert.Null(metadata.CountryName);
    }

    [Theory]
    [InlineData("gb", "GB")]
    [InlineData("JP", "JP")]
    [InlineData("za", "ZA")]
    public async Task AssignedCountryCodes_AreAcceptedWithoutRuntimeCultureData(
        string returnedCode,
        string expectedCode)
    {
        using var factory = CreateFactory((_, _) => Task.FromResult(JsonResponse(
            $$"""{"ip":"8.8.8.8","country_iso":"{{returnedCode}}","country":"Fixture country"}""")));

        var metadata = await CreateProvider(factory).GetMetadataAsync(
            IPAddress.Parse("8.8.8.8"),
            CancellationToken.None);

        Assert.NotNull(metadata);
        Assert.Equal(expectedCode, metadata.CountryCode);
        Assert.Equal("Fixture country", metadata.CountryName);
    }

    [Fact]
    public async Task MalformedJson_ReturnsNull()
    {
        using var factory = CreateFixtureFactory("ipconfig-malformed.json");

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

    [Theory]
    [InlineData("8.8.8.8")]
    [InlineData("2001:4860:4860::8888")]
    public async Task Lookup_UsesRequestedAddressForEitherFamily(string address)
    {
        using var factory = CreateFactory((request, _) =>
        {
            Assert.Equal("/json?ip=" + Uri.EscapeDataString(address), request.RequestUri?.PathAndQuery);
            return Task.FromResult(JsonResponse("{\"ip\":\"" + address + "\"}"));
        });
        var metadata = await CreateProvider(factory).GetMetadataAsync(IPAddress.Parse(address), CancellationToken.None);
        Assert.NotNull(metadata);
        Assert.Equal(IPAddress.Parse(address), metadata.Address);
    }

    [Theory]
    [InlineData("{\"ip\":\"1.1.1.1\"}")]
    [InlineData("{\"ip\":\"invalid\"}")]
    [InlineData("{\"ip\":123}")]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("null")]
    public async Task InvalidOrMismatchedAddress_ReturnsNull(string json)
    {
        using var factory = CreateFactory((_, _) => Task.FromResult(JsonResponse(json)));
        Assert.Null(await CreateProvider(factory).GetMetadataAsync(IPAddress.Parse("8.8.8.8"), CancellationToken.None));
    }

    [Fact]
    public async Task InvalidOptionalFields_AreOmitted()
    {
        using var factory = CreateFactory((_, _) => Task.FromResult(JsonResponse(
            """{"ip":"8.8.8.8","asn_org":42,"region_name":" ","city":null,"country_iso":"US"}""")));
        var metadata = await CreateProvider(factory).GetMetadataAsync(IPAddress.Parse("8.8.8.8"), CancellationToken.None);
        Assert.NotNull(metadata);
        Assert.Null(metadata.AsName);
        Assert.Null(metadata.Region);
        Assert.Null(metadata.City);
        Assert.Null(metadata.CountryCode);
        Assert.Null(metadata.CountryName);
    }

    [Fact]
    public async Task Timeout_ReturnsNull()
    {
        using var factory = CreateFactory((_, _) => throw new OperationCanceledException());
        Assert.Null(await CreateProvider(factory).GetMetadataAsync(IPAddress.Parse("8.8.8.8"), CancellationToken.None));
    }

    [Fact]
    public async Task NetworkFailure_ReturnsNull()
    {
        using var factory = CreateFactory((_, _) => throw new HttpRequestException());
        Assert.Null(await CreateProvider(factory).GetMetadataAsync(IPAddress.Parse("8.8.8.8"), CancellationToken.None));
    }

    [Fact]
    public async Task CallerCancellation_Propagates()
    {
        using var cancellation = new CancellationTokenSource();
        using var factory = CreateFactory((_, _) =>
        {
            cancellation.Cancel();
            throw new OperationCanceledException(cancellation.Token);
        });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            CreateProvider(factory).GetMetadataAsync(IPAddress.Parse("8.8.8.8"), cancellation.Token));
    }

    private static FakeHttpClientFactory CreateFixtureFactory(string name) => CreateFactory(async (_, _) =>
        JsonResponse(await ReadFixtureAsync(name)));

    private static FakeHttpClientFactory CreateFactory(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
    {
        var factory = new FakeHttpClientFactory();
        factory.Add(IpConfigIoMetadataProvider.ClientName, new StubHttpMessageHandler(handler));
        return factory;
    }

    private static IpConfigIoMetadataProvider CreateProvider(IHttpClientFactory factory) => new(
        factory,
        NullLogger<IpConfigIoMetadataProvider>.Instance);

    private static HttpResponseMessage JsonResponse(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
    };

    private static Task<string> ReadFixtureAsync(string name) =>
        File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));
}
