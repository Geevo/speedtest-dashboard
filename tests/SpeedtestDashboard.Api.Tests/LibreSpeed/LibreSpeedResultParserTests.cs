using System.Text.Json;
using SpeedtestDashboard.Core.Providers;
using SpeedtestDashboard.Infrastructure.Providers.LibreSpeed;

namespace SpeedtestDashboard.Api.Tests.LibreSpeed;

public sealed class LibreSpeedResultParserTests
{
    private readonly LibreSpeedResultParser _parser = new();
    private readonly IReadOnlyList<LibreSpeedServerDefinition> _catalog = new LibreSpeedServerCatalogParser()
        .Parse(LibreSpeedTestFactory.Fixture("servers-normal.json"), 250);

    [Fact]
    public void CompleteResult_MapsPinnedJsonShapeWithoutOoklaUnitConversion()
    {
        var result = _parser.Parse(LibreSpeedTestFactory.Fixture("result-complete.json"), catalog: _catalog);

        Assert.Equal(LibreSpeedProviderDefinition.Id, result.ProviderId);
        Assert.Equal(934.25m, result.DownloadMbps);
        Assert.Equal(104.2m, result.UploadMbps);
        Assert.Equal(11.4m, result.LatencyMilliseconds);
        Assert.Equal(0.7m, result.JitterMilliseconds);
        Assert.Equal("49", result.ServerId);
        Assert.Equal("London, England (Example Network)", result.ServerName);
        Assert.Equal("London, England (Example Network)", result.ServerLocation);
    }

    [Fact]
    public void UnsupportedMeasurementsAndSharingRemainNull()
    {
        var fixture = LibreSpeedTestFactory.Fixture("result-complete.json")
            .Replace("\"share\": \"\"", "\"share\": \"https://unexpected.example/result\"", StringComparison.Ordinal);

        var result = _parser.Parse(fixture, catalog: _catalog);

        Assert.Null(result.PacketLossPercent);
        Assert.Null(result.ResultUrl);
    }

    [Fact]
    public void Metadata_IsKnownBoundedSubsetAndKeepsProviderIdentitySeparate()
    {
        var result = _parser.Parse(LibreSpeedTestFactory.Fixture("result-complete.json"), catalog: _catalog);
        using var metadata = JsonDocument.Parse(result.ProviderMetadataJson!);

        Assert.Equal((ulong)157286400, metadata.RootElement.GetProperty("bytesSent").GetUInt64());
        Assert.Equal((ulong)1401375000, metadata.RootElement.GetProperty("bytesReceived").GetUInt64());
        Assert.Equal("192.0.2.40", metadata.RootElement.GetProperty("libreSpeedReportedClientIp").GetString());
        Assert.Equal("AS64500 Example Network", metadata.RootElement.GetProperty("libreSpeedReportedClientOrganization").GetString());
        Assert.True(metadata.RootElement.GetProperty("httpPing").GetBoolean());
        Assert.False(metadata.RootElement.TryGetProperty("unknownFutureField", out _));
    }

    [Fact]
    public void ExplicitSelection_RetainsRequestedId()
    {
        var result = _parser.Parse(LibreSpeedTestFactory.Fixture("result-complete.json"), "82", _catalog);

        Assert.Equal("82", result.ServerId);
        Assert.Equal("Tōkyō, Japan (A573)", result.ServerLocation);
        Assert.Equal("London, England (Example Network)", result.ServerName);
    }

    [Fact]
    public void AutomaticSelection_LeavesIdNullWhenNoUniqueCatalogueMatchExists()
    {
        var result = _parser.Parse(LibreSpeedTestFactory.Fixture("result-complete.json"));

        Assert.Null(result.ServerId);
        Assert.Null(result.ServerLocation);
        Assert.Equal("London, England (Example Network)", result.ServerName);
    }

    [Theory]
    [InlineData("result-empty.json")]
    [InlineData("result-multiple.json")]
    [InlineData("result-missing-download.json")]
    [InlineData("result-negative.json")]
    [InlineData("result-overflow.json")]
    public void InvalidOrIncompleteResults_AreRejected(string fixture)
    {
        Assert.Throws<LibreSpeedOutputException>(() => _parser.Parse(LibreSpeedTestFactory.Fixture(fixture)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-json")]
    [InlineData("{}")]
    [InlineData("null")]
    public void MalformedRoot_IsRejected(string output)
    {
        Assert.Throws<LibreSpeedOutputException>(() => _parser.Parse(output));
    }
}
