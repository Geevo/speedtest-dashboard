using Microsoft.Data.Sqlite;
using SpeedtestDashboard.Core.Providers;
using SpeedtestDashboard.Core.Statistics;

namespace SpeedtestDashboard.Infrastructure.Persistence;

public sealed class SqliteStatisticsService(SqliteConnectionFactory connectionFactory, TimeProvider timeProvider)
    : ISpeedTestStatisticsService
{
    private const string CompletedStatus = "completed";

    public async Task<SpeedTestStatistics> GetAsync(StatisticsQuery query, CancellationToken cancellationToken)
    {
        var toUtc = timeProvider.GetUtcNow();
        var fromUtc = StartOf(query.Range, toUtc);
        DateTimeOffset? previousFromUtc = fromUtc is null ? null : fromUtc.Value - (toUtc - fromUtc.Value);
        var rows = await LoadAsync(previousFromUtc ?? fromUtc, toUtc, cancellationToken);
        var current = Filter(rows, query.ProviderId, fromUtc, toUtc).ToArray();
        var completed = current.Where(row => row.Status == CompletedStatus).ToArray();
        var previous = fromUtc is null ? null : Filter(rows, query.ProviderId, previousFromUtc, fromUtc.Value, true)
            .Where(row => row.Status == CompletedStatus).ToArray();

        return new SpeedTestStatistics(
            RangeValue(query.Range), query.ProviderId?.Value, fromUtc, toUtc, CountTests(current),
            Metric(completed, previous, row => row.DownloadMbps, false),
            Metric(completed, previous, row => row.UploadMbps, false),
            Metric(completed, previous, row => row.LatencyMs, true),
            Metric(completed, previous, row => row.JitterMs, true),
            Metric(completed, previous, row => row.PacketLossPercent, false),
            Chart(completed, toUtc, query.Range),
            ProviderComparisons(Filter(rows, null, fromUtc, toUtc)));
    }

    private async Task<List<StatisticsRow>> LoadAsync(DateTimeOffset? fromUtc, DateTimeOffset toUtc, CancellationToken cancellationToken)
    {
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connectionFactory.CreateCommand(connection, """
            SELECT Id, ProviderId, Status, CompletedAtUtc, DownloadMbps, UploadMbps, LatencyMs, JitterMs, PacketLossPercent
            FROM SpeedTestResults
            WHERE (@fromUtc IS NULL OR CompletedAtUtc >= @fromUtc) AND CompletedAtUtc <= @toUtc
            ORDER BY CompletedAtUtc DESC, Id DESC;
            """);
        Add(command, "@fromUtc", fromUtc?.UtcDateTime);
        Add(command, "@toUtc", toUtc.UtcDateTime);
        var rows = new List<StatisticsRow>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new StatisticsRow(reader.GetString(1), reader.GetString(2), reader.GetDateTime(3),
                Number(reader, 4), Number(reader, 5), Number(reader, 6), Number(reader, 7), Number(reader, 8)));
        }
        return rows;
    }

    private static IEnumerable<StatisticsRow> Filter(IEnumerable<StatisticsRow> rows, ProviderId? providerId,
        DateTimeOffset? fromUtc, DateTimeOffset toUtc, bool endExclusive = false)
    {
        var start = fromUtc?.UtcDateTime;
        var end = toUtc.UtcDateTime;
        return rows.Where(row => (providerId is null || row.ProviderId == providerId.Value.Value) &&
            (start is null || row.CompletedAtUtc >= start.Value) &&
            (endExclusive ? row.CompletedAtUtc < end : row.CompletedAtUtc <= end));
    }

    private static TestCountStatistics CountTests(IEnumerable<StatisticsRow> rows)
    {
        var counts = rows.GroupBy(row => row.Status).ToDictionary(group => group.Key, group => group.Count());
        var completed = counts.GetValueOrDefault(CompletedStatus);
        var failed = counts.GetValueOrDefault("failed");
        var cancelled = counts.GetValueOrDefault("cancelled");
        var total = completed + failed + cancelled;
        return new TestCountStatistics(total, completed, failed, cancelled,
            total == 0 ? null : Round(100m * completed / total));
    }

    private static MetricStatistics? Metric(IReadOnlyList<StatisticsRow> current, IReadOnlyList<StatisticsRow>? previous,
        Func<StatisticsRow, double?> selector, bool includeP95)
    {
        var values = current.Select(selector).Where(value => value.HasValue).Select(value => value!.Value).ToArray();
        if (values.Length == 0) return null;
        var ordered = values.Order().ToArray();
        var latest = current.Select(selector).First(value => value.HasValue)!.Value;
        var median = Percentile(ordered, 0.5, true);
        decimal? trend = null;
        if (previous is not null && values.Length >= 2)
        {
            var old = previous.Select(selector).Where(value => value.HasValue).Select(value => value!.Value).Order().ToArray();
            if (old.Length >= 2)
            {
                var oldMedian = Percentile(old, 0.5, true);
                if (oldMedian != 0) trend = Round((ToDecimal(median)!.Value - ToDecimal(oldMedian)!.Value) / Math.Abs(ToDecimal(oldMedian)!.Value) * 100m);
            }
        }
        return new MetricStatistics(values.Length, ToDecimal(latest), ToDecimal(values.Average()), ToDecimal(median),
            ToDecimal(ordered[0]), ToDecimal(ordered[^1]), includeP95 ? ToDecimal(Percentile(ordered, 0.95, false)) : null, trend);
    }

    private static double Percentile(IReadOnlyList<double> ordered, double percentile, bool interpolateMedian)
    {
        if (interpolateMedian)
        {
            var offset = (ordered.Count - 1) / 2;
            return ordered.Count % 2 == 0 ? (ordered[offset] + ordered[offset + 1]) / 2 : ordered[offset];
        }
        return ordered[Math.Max(1, (int)Math.Ceiling(percentile * ordered.Count)) - 1];
    }

    private static IReadOnlyList<ProviderStatisticsComparison> ProviderComparisons(IEnumerable<StatisticsRow> source) =>
        source.GroupBy(row => row.ProviderId).OrderBy(group => group.Key)
            .Where(group => ProviderId.TryParse(group.Key, out _))
            .Select(group =>
            {
                var rows = group.ToArray();
                var completed = rows.Where(row => row.Status == CompletedStatus).ToArray();
                return new ProviderStatisticsComparison(group.Key, CountTests(rows),
                    Median(completed, row => row.DownloadMbps), Median(completed, row => row.UploadMbps),
                    Median(completed, row => row.LatencyMs), Median(completed, row => row.JitterMs),
                    Median(completed, row => row.PacketLossPercent));
            }).ToArray();

    private static decimal? Median(IEnumerable<StatisticsRow> rows, Func<StatisticsRow, double?> selector)
    {
        var values = rows.Select(selector).Where(value => value.HasValue).Select(value => value!.Value).Order().ToArray();
        return values.Length == 0 ? null : ToDecimal(Percentile(values, 0.5, true));
    }

    private static IReadOnlyList<StatisticsChartPoint> Chart(IReadOnlyList<StatisticsRow> rows, DateTimeOffset toUtc, StatisticsRange range)
    {
        var seconds = BucketSeconds(rows, toUtc, range);
        return rows.GroupBy(row => new DateTimeOffset(DateTime.SpecifyKind(row.CompletedAtUtc, DateTimeKind.Utc)).ToUnixTimeSeconds() / seconds)
            .OrderBy(group => group.Key).Take(241)
            .Select(group => new StatisticsChartPoint(DateTimeOffset.FromUnixTimeSeconds(group.Key * seconds),
                Average(group, row => row.DownloadMbps), Average(group, row => row.UploadMbps),
                Average(group, row => row.LatencyMs), Average(group, row => row.JitterMs))).ToArray();
    }

    private static long BucketSeconds(IReadOnlyList<StatisticsRow> rows, DateTimeOffset toUtc, StatisticsRange range)
    {
        if (range != StatisticsRange.AllTime)
        {
            return range switch
            {
                StatisticsRange.Last24Hours => 15 * 60,
                StatisticsRange.Last7Days => 60 * 60,
                StatisticsRange.Last30Days => 6 * 60 * 60,
                StatisticsRange.Last90Days => 24 * 60 * 60,
                _ => throw new ArgumentOutOfRangeException(nameof(range))
            };
        }
        if (rows.Count == 0) return 24 * 60 * 60;
        var days = Math.Max(1, (toUtc.UtcDateTime - rows.Min(row => row.CompletedAtUtc)).TotalDays);
        return days <= 240 ? 24 * 60 * 60 : Math.Max(1, (int)Math.Ceiling(days / (240d * 7d))) * 7L * 24L * 60L * 60L;
    }

    private static decimal? Average(IEnumerable<StatisticsRow> rows, Func<StatisticsRow, double?> selector)
    {
        var values = rows.Select(selector).Where(value => value.HasValue).Select(value => value!.Value).ToArray();
        return values.Length == 0 ? null : ToDecimal(values.Average());
    }

    private static DateTimeOffset? StartOf(StatisticsRange range, DateTimeOffset now) => range switch
    {
        StatisticsRange.Last24Hours => now - TimeSpan.FromHours(24),
        StatisticsRange.Last7Days => now - TimeSpan.FromDays(7),
        StatisticsRange.Last30Days => now - TimeSpan.FromDays(30),
        StatisticsRange.Last90Days => now - TimeSpan.FromDays(90),
        StatisticsRange.AllTime => null,
        _ => throw new ArgumentOutOfRangeException(nameof(range))
    };

    private static string RangeValue(StatisticsRange range) => range switch
    {
        StatisticsRange.Last24Hours => "24h",
        StatisticsRange.Last7Days => "7d",
        StatisticsRange.Last30Days => "30d",
        StatisticsRange.Last90Days => "90d",
        StatisticsRange.AllTime => "all",
        _ => throw new ArgumentOutOfRangeException(nameof(range))
    };

    private static void Add(SqliteCommand command, string name, object? value) => command.Parameters.AddWithValue(name, value ?? DBNull.Value);
    private static double? Number(SqliteDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : reader.GetDouble(ordinal);
    private static decimal? ToDecimal(double? value) => value is null ? null : Round((decimal)value.Value);
    private static decimal Round(decimal value) => Math.Round(value, 3, MidpointRounding.AwayFromZero);

    private sealed record StatisticsRow(string ProviderId, string Status, DateTime CompletedAtUtc,
        double? DownloadMbps, double? UploadMbps, double? LatencyMs, double? JitterMs, double? PacketLossPercent);
}
