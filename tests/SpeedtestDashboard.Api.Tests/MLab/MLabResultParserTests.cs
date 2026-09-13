using System.Text.Json;
using SpeedtestDashboard.Core.Providers;
using SpeedtestDashboard.Infrastructure.Providers.MLab;

namespace SpeedtestDashboard.Api.Tests.MLab;

public sealed class MLabResultParserTests
{
    private readonly MLabResultParser _parser = new();

    [Fact]
    public void CompleteResultMapsOfficialSummaryWithoutInventingMeasurements()
    {
        var result = _parser.Parse(MLabTestFactory.Fixture("result-complete.json"));

        Assert.Equal(MLabProviderDefinition.Id, result.ProviderId);
        Assert.Equal(507.25m, result.DownloadMbps);
        Assert.Equal(314.4m, result.UploadMbps);
        Assert.Equal(8.75m, result.LatencyMilliseconds);
        Assert.Equal("ndt-iupui-mlab1-lhr03.mlab-oti.measurement-lab.org", result.ServerName);
        Assert.Null(result.ServerId);
        Assert.Null(result.ServerLocation);
        Assert.Null(result.JitterMilliseconds);
        Assert.Null(result.PacketLossPercent);
        Assert.Null(result.ResultUrl);
    }

    [Fact]
    public void MetadataDistinguishesRetransmissionFromPacketLossAndRecordsPublication()
    {
        var result = _parser.Parse(MLabTestFactory.Fixture("result-complete.json"));
        using var metadata = JsonDocument.Parse(result.ProviderMetadataJson!);

        Assert.Equal("ndt7", metadata.RootElement.GetProperty("measurementProtocol").GetString());
        Assert.Equal("download-min-rtt", metadata.RootElement.GetProperty("latencyMethod").GetString());
        Assert.Equal(0.012m, metadata.RootElement.GetProperty("downloadRetransmissionPercent").GetDecimal());
        Assert.Equal("measurement-lab-public", metadata.RootElement.GetProperty("dataPublication").GetString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-json")]
    [InlineData("[]")]
    [InlineData("{}")]
    public void InvalidResultsAreRejected(string output)
    {
        Assert.Throws<MLabOutputException>(() => _parser.Parse(output));
    }

    [Theory]
    [InlineData("result-missing-upload.json")]
    [InlineData("result-invalid-unit.json")]
    public void IncompleteOrUnitAmbiguousResultsAreRejected(string fixture)
    {
        Assert.Throws<MLabOutputException>(() => _parser.Parse(MLabTestFactory.Fixture(fixture)));
    }
}
