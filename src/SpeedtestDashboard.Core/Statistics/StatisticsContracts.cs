using SpeedtestDashboard.Core.Providers;

namespace SpeedtestDashboard.Core.Statistics;

public enum StatisticsRange
{
    Last24Hours,
    Last7Days,
    Last30Days,
    Last90Days,
    AllTime
}

public sealed record StatisticsQuery(StatisticsRange Range, ProviderId? ProviderId);

public sealed record TestCountStatistics(int Total, int Completed, int Failed, int Cancelled, decimal? SuccessRate);

public sealed record MetricStatistics(
    int Count,
    decimal? Latest,
    decimal? Average,
    decimal? Median,
    decimal? Minimum,
    decimal? Maximum,
    decimal? P95,
    decimal? TrendPercent);

public sealed record StatisticsChartPoint(
    DateTimeOffset BucketStartUtc,
    decimal? DownloadMbps,
    decimal? UploadMbps,
    decimal? LatencyMilliseconds,
    decimal? JitterMilliseconds);

public sealed record ProviderStatisticsComparison(
    string Provider,
    TestCountStatistics Tests,
    decimal? MedianDownloadMbps,
    decimal? MedianUploadMbps,
    decimal? MedianLatencyMilliseconds,
    decimal? MedianJitterMilliseconds,
    decimal? MedianPacketLossPercent);

public sealed record SpeedTestStatistics(
    string Range,
    string? Provider,
    DateTimeOffset? FromUtc,
    DateTimeOffset ToUtc,
    TestCountStatistics Tests,
    MetricStatistics? Download,
    MetricStatistics? Upload,
    MetricStatistics? Latency,
    MetricStatistics? Jitter,
    MetricStatistics? PacketLoss,
    IReadOnlyList<StatisticsChartPoint> Chart,
    IReadOnlyList<ProviderStatisticsComparison> Providers);

public interface ISpeedTestStatisticsService
{
    Task<SpeedTestStatistics> GetAsync(StatisticsQuery query, CancellationToken cancellationToken);
}
