using SpeedtestDashboard.Infrastructure.Providers.Ookla;

namespace SpeedtestDashboard.Api.Tests.Ookla;

public sealed class OoklaServerListParserTests
{
    private readonly OoklaServerListParser _parser = new();

    [Fact]
    public void ValidList_IsNormalizedWithoutInventingUnavailableMeasurements()
    {
        var servers = _parser.Parse(OoklaTestFactory.Fixture("servers-normal.txt"), 100);

        Assert.Equal(2, servers.Count);
        Assert.Equal("12345", servers[0].Id);
        Assert.Equal("Example ISP", servers[0].Name);
        Assert.Equal("London, United Kingdom", servers[0].Location);
        Assert.Null(servers[0].DistanceKilometres);
        Assert.Null(servers[0].LatencyMilliseconds);
    }

    [Fact]
    public void EmptyList_IsValid()
    {
        Assert.Empty(_parser.Parse(OoklaTestFactory.Fixture("servers-empty.txt"), 100));
    }

    [Fact]
    public void UnicodeText_IsPreserved()
    {
        var server = Assert.Single(_parser.Parse(OoklaTestFactory.Fixture("servers-unicode.txt"), 100));

        Assert.Equal("Réseau Étoile", server.Name);
        Assert.Equal("Montréal, Canada", server.Location);
    }

    [Fact]
    public void DuplicateIds_KeepTheFirstValidRow()
    {
        var server = Assert.Single(_parser.Parse(OoklaTestFactory.Fixture("servers-duplicate.txt"), 100));

        Assert.Equal("First ISP", server.Name);
    }

    [Theory]
    [InlineData("servers-malformed.txt")]
    [InlineData("servers-large-id.txt")]
    public void MalformedRows_AreRejected(string fixture)
    {
        Assert.Throws<OoklaOutputException>(() => _parser.Parse(OoklaTestFactory.Fixture(fixture), 100));
    }

    [Fact]
    public void ResultCount_IsBounded()
    {
        var servers = _parser.Parse(OoklaTestFactory.Fixture("servers-normal.txt"), 1);

        Assert.Single(servers);
    }
}
