using System.Text.Json;
using SpeedtestDashboard.Infrastructure.Providers.Ookla;

namespace SpeedtestDashboard.Api.Tests.Ookla;

public sealed class OoklaResultParserTests
{
    private readonly OoklaResultParser _parser = new();

    [Fact]
    public void CompleteResult_NormalizesCommonMetricsAndKnownMetadata()
    {
        var result = _parser.Parse(OoklaTestFactory.Fixture("result-complete.json"));

        Assert.Equal("ookla", result.ProviderId.Value);
        Assert.Equal("12345", result.ServerId);
        Assert.Equal("Fixture ISP", result.ServerName);
        Assert.Equal("London, United Kingdom", result.ServerLocation);
        Assert.Equal(1000m, result.DownloadMbps);
        Assert.Equal(104.2m, result.UploadMbps);
        Assert.Equal(11.4m, result.LatencyMilliseconds);
        Assert.Equal(0.7m, result.JitterMilliseconds);
        Assert.Equal(0m, result.PacketLossPercent);
        Assert.Equal(
            "https://www.speedtest.net/result/c/00000000-0000-4000-8000-000000000001",
            result.ResultUrl);

        using var metadata = JsonDocument.Parse(result.ProviderMetadataJson!);
        Assert.Equal("Fixture Network", metadata.RootElement.GetProperty("isp").GetString());
        Assert.Equal("198.51.100.20", metadata.RootElement.GetProperty("ooklaReportedExternalIp").GetString());
        Assert.Equal(14.2m, metadata.RootElement.GetProperty("downloadLatency").GetProperty("iqm").GetDecimal());
        Assert.Equal("00000000-0000-4000-8000-000000000001", metadata.RootElement.GetProperty("resultId").GetString());
    }

    [Fact]
    public void ExactBandwidthConversion_UsesDecimalBytesPerSecond()
    {
        Assert.Equal(1000m, OoklaResultParser.BytesPerSecondToMbps(125_000_000m));
    }

    [Fact]
    public void UnavailablePacketLossAndMissingUrl_RemainNull()
    {
        var result = _parser.Parse(OoklaTestFactory.Fixture("result-packet-loss-unavailable.json"));

        Assert.Null(result.PacketLossPercent);
        Assert.Null(result.ResultUrl);
        Assert.Equal("Reading, United Kingdom", result.ServerLocation);
    }

    [Fact]
    public void InvalidResultUrl_IsOmittedWithoutDiscardingMeasurements()
    {
        var result = _parser.Parse(OoklaTestFactory.Fixture("result-invalid-url.json"));

        Assert.Null(result.ResultUrl);
        Assert.Equal(1m, result.DownloadMbps);
    }

    [Theory]
    [InlineData("result-malformed.json")]
    [InlineData("result-root-array.json")]
    [InlineData("result-missing-download.json")]
    [InlineData("result-missing-upload.json")]
    [InlineData("result-missing-ping.json")]
    [InlineData("result-negative.json")]
    [InlineData("result-overflow.json")]
    public void InvalidOrIncompleteResults_AreRejected(string fixture)
    {
        Assert.Throws<OoklaOutputException>(() => _parser.Parse(OoklaTestFactory.Fixture(fixture)));
    }

    [Fact]
    public void EmptyOutput_IsRejected()
    {
        Assert.Throws<OoklaOutputException>(() => _parser.Parse(string.Empty));
    }
}
