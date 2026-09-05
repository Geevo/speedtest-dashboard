using SpeedtestDashboard.Core.Providers;
using SpeedtestDashboard.Infrastructure.Providers.LibreSpeed;

namespace SpeedtestDashboard.Api.Tests.LibreSpeed;

public sealed class LibreSpeedServerCatalogParserTests
{
    private readonly LibreSpeedServerCatalogParser _parser = new();

    [Fact]
    public void NormalCatalogue_MapsPublicFieldsAndIgnoresUnknownFields()
    {
        var servers = _parser.Parse(LibreSpeedTestFactory.Fixture("servers-normal.json"), 250);

        Assert.Equal(2, servers.Count);
        var london = servers[0];
        Assert.Equal(ProviderId.LibreSpeed, london.Server.ProviderId);
        Assert.Equal("49", london.Server.Id);
        Assert.Equal("Example Network", london.Server.Name);
        Assert.Equal("Example Network", london.Server.Sponsor);
        Assert.Equal("London, England (Example Network)", london.Server.Location);
        Assert.Equal("london.example.test", london.Server.Host);
        Assert.Null(london.Server.CountryCode);
        Assert.Null(london.Server.DistanceKilometres);
        Assert.Null(london.Server.LatencyMilliseconds);
    }

    [Fact]
    public void UnicodeCatalogueValues_ArePreserved()
    {
        var servers = _parser.Parse(LibreSpeedTestFactory.Fixture("servers-normal.json"), 250);

        Assert.Equal("Tōkyō, Japan (A573)", servers[1].Server.Location);
    }

    [Fact]
    public void MaximumServerLimit_IsAppliedDeterministically()
    {
        var servers = _parser.Parse(LibreSpeedTestFactory.Fixture("servers-normal.json"), 1);

        Assert.Single(servers);
        Assert.Equal("49", servers[0].Server.Id);
    }

    [Fact]
    public void EmptyCatalogue_IsValidAndHonest()
    {
        Assert.Empty(_parser.Parse(LibreSpeedTestFactory.Fixture("servers-empty.json"), 250));
    }

    [Theory]
    [InlineData("servers-duplicate.json")]
    [InlineData("servers-malformed.json")]
    public void InvalidCatalogue_IsRejected(string fixture)
    {
        Assert.Throws<LibreSpeedOutputException>(() =>
            _parser.Parse(LibreSpeedTestFactory.Fixture(fixture), 250));
    }

    [Theory]
    [InlineData("")]
    [InlineData("{}")]
    [InlineData("not-json")]
    public void MalformedCatalogueRoot_IsRejected(string json)
    {
        Assert.Throws<LibreSpeedOutputException>(() => _parser.Parse(json, 250));
    }
}
