using System.Text.Json;
using SpeedtestDashboard.Core.Providers;
using SpeedtestDashboard.Infrastructure.Providers.FastCom;

namespace SpeedtestDashboard.Api.Tests.FastCom;

public sealed class FastComResultParserTests
{
    private readonly FastComResultParser _parser = new();

    [Fact]
    public void CompleteResult_MapsUpstreamJsonWithoutChangingReportedUnits()
    {
        var result = _parser.Parse(FastComTestFactory.Fixture("result-complete.json"));

        Assert.Equal(ProviderId.FastCom, result.ProviderId);
        Assert.Equal(131m, result.DownloadMbps);
        Assert.Equal(42m, result.UploadMbps);
        Assert.Equal(20.8m, result.LatencyMilliseconds);
        Assert.Null(result.ServerId);
        Assert.Null(result.ServerName);
        Assert.Null(result.JitterMilliseconds);
        Assert.Null(result.PacketLossPercent);
        Assert.Null(result.ResultUrl);
    }

    [Fact]
    public void Metadata_RecordsOnlyStableAdapterSemantics()
    {
        var result = _parser.Parse(FastComTestFactory.Fixture("result-complete.json"));
        using var metadata = JsonDocument.Parse(result.ProviderMetadataJson!);

        Assert.Equal("http-head", metadata.RootElement.GetProperty("latencyMethod").GetString());
        Assert.Equal("fast.com-managed", metadata.RootElement.GetProperty("serverSelection").GetString());
    }

    [Fact]
    public void UpstreamErrorField_IsFailureEvenWhenProcessExitedZero()
    {
        var exception = Assert.Throws<FastComOutputException>(() =>
            _parser.Parse(FastComTestFactory.Fixture("result-error.json")));

        Assert.True(exception.ProviderReportedFailure);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-json")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("{\"download_mbps\":-1,\"upload_mbps\":2,\"ping_ms\":3,\"error\":null}")]
    public void InvalidResults_AreRejected(string output)
    {
        Assert.Throws<FastComOutputException>(() => _parser.Parse(output));
    }

    [Fact]
    public void MissingUpload_IsRejectedBecauseProviderAlwaysRequestsUpload()
    {
        Assert.Throws<FastComOutputException>(() =>
            _parser.Parse(FastComTestFactory.Fixture("result-missing-upload.json")));
    }
}
