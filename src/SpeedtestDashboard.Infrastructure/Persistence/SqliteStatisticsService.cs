using System.Data;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using SpeedtestDashboard.Core.Providers;
using SpeedtestDashboard.Core.Statistics;
using SpeedtestDashboard.Infrastructure.Persistence.Entities;

namespace SpeedtestDashboard.Infrastructure.Persistence;

public sealed class SqliteStatisticsService(
    IDbContextFactory<DashboardDbContext> contextFactory,
    TimeProvider timeProvider) : ISpeedTestStatisticsService
{
    private const string CompletedStatus = "completed";

    public async Task<SpeedTestStatistics> GetAsync(StatisticsQuery query, CancellationToken cancellationToken)
    {
        var toUtc = timeProvider.GetUtcNow();
        var fromUtc = StartOf(query.Range, toUtc);
        DateTimeOffset? previousFromUtc = fromUtc is null ? null : fromUtc.Value - (toUtc - fromUtc.Value);

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var current = Filter(context.SpeedTestResults.AsNoTracking(), query.ProviderId, fromUtc, toUtc);
        var completed = current.Where(result => result.Status == CompletedStatus);
        var previous = fromUtc is null
            ? null
            : Filter(context.SpeedTestResults.AsNoTracking(), query.ProviderId, previousFromUtc, fromUtc.Value, endExclusive: true)
                .Where(result => result.Status == CompletedStatus);

        var tests = await CountTestsAsync(current, cancellationToken);
        var download = await MetricAsync(completed, previous, result => result.DownloadMbps, includeP95: false, cancellationToken);
        var upload = await MetricAsync(completed, previous, result => result.UploadMbps, includeP95: false, cancellationToken);
        var latency = await MetricAsync(completed, previous, result => result.LatencyMs, includeP95: true, cancellationToken);
        var jitter = await MetricAsync(completed, previous, result => result.JitterMs, includeP95: true, cancellationToken);
        var packetLoss = await MetricAsync(completed, previous, result => result.PacketLossPercent, includeP95: false, cancellationToken);
        var chart = await ChartAsync(context, query.ProviderId, fromUtc, toUtc, query.Range, cancellationToken);
        var providers = await ProviderComparisonsAsync(context, fromUtc, toUtc, cancellationToken);

        return new SpeedTestStatistics(
            RangeValue(query.Range),
            query.ProviderId?.Value,
            fromUtc,
            toUtc,
            tests,
            download,
            upload,
            latency,
            jitter,
            packetLoss,
            chart,
            providers);
    }

    private static IQueryable<SpeedTestResultEntity> Filter(
        IQueryable<SpeedTestResultEntity> results,
        ProviderId? providerId,
        DateTimeOffset? fromUtc,
        DateTimeOffset toUtc,
        bool endExclusive = false)
    {
        if (providerId is not null)
        {
            var value = providerId.Value.Value;
            results = results.Where(result => result.ProviderId == value);
        }

        if (fromUtc is not null)
        {
            var start = fromUtc.Value.UtcDateTime;
            results = results.Where(result => result.CompletedAtUtc >= start);
        }

        var end = toUtc.UtcDateTime;
        return endExclusive
            ? results.Where(result => result.CompletedAtUtc < end)
            : results.Where(result => result.CompletedAtUtc <= end);
    }

    private static async Task<TestCountStatistics> CountTestsAsync(
        IQueryable<SpeedTestResultEntity> results,
        CancellationToken cancellationToken)
    {
        var counts = await results
            .GroupBy(result => result.Status)
            .Select(group => new { Status = group.Key, Count = group.Count() })
            .ToDictionaryAsync(item => item.Status, item => item.Count, cancellationToken);
        var completed = counts.GetValueOrDefault(CompletedStatus);
        var failed = counts.GetValueOrDefault("failed");
        var cancelled = counts.GetValueOrDefault("cancelled");
        var total = completed + failed + cancelled;
        return new TestCountStatistics(
            total,
            completed,
            failed,
            cancelled,
            total == 0 ? null : Round(100m * completed / total));
    }

    private static async Task<MetricStatistics?> MetricAsync(
        IQueryable<SpeedTestResultEntity> current,
        IQueryable<SpeedTestResultEntity>? previous,
        Expression<Func<SpeedTestResultEntity, double?>> selector,
        bool includeP95,
        CancellationToken cancellationToken)
    {
        var values = current.Select(selector).Where(value => value.HasValue).Select(value => value!.Value);
        var aggregate = await values.GroupBy(_ => 1).Select(group => new
        {
            Count = group.Count(),
            Average = group.Average(),
            Minimum = group.Min(),
            Maximum = group.Max()
        }).SingleOrDefaultAsync(cancellationToken);
        if (aggregate is null)
        {
            return null;
        }

        var latest = await current.OrderByDescending(result => result.CompletedAtUtc)
            .ThenByDescending(result => result.Id)
            .Select(selector)
            .FirstAsync(value => value.HasValue, cancellationToken);
        var median = await PercentileAsync(values, aggregate.Count, 0.5, interpolateMedian: true, cancellationToken);
        var p95 = includeP95
            ? await PercentileAsync(values, aggregate.Count, 0.95, interpolateMedian: false, cancellationToken)
            : null;

        decimal? trend = null;
        if (previous is not null && aggregate.Count >= 2)
        {
            var previousValues = previous.Select(selector).Where(value => value.HasValue).Select(value => value!.Value);
            var previousCount = await previousValues.CountAsync(cancellationToken);
            if (previousCount >= 2)
            {
                var previousMedian = await PercentileAsync(previousValues, previousCount, 0.5, interpolateMedian: true, cancellationToken);
                if (previousMedian is not null && previousMedian != 0)
                {
                    trend = Round((ToDecimal(median)!.Value - ToDecimal(previousMedian)!.Value) / Math.Abs(ToDecimal(previousMedian)!.Value) * 100m);
                }
            }
        }

        return new MetricStatistics(
            aggregate.Count,
            ToDecimal(latest),
            ToDecimal(aggregate.Average),
            ToDecimal(median),
            ToDecimal(aggregate.Minimum),
            ToDecimal(aggregate.Maximum),
            ToDecimal(p95),
            trend);
    }

    private static async Task<double?> PercentileAsync(
        IQueryable<double> values,
        int count,
        double percentile,
        bool interpolateMedian,
        CancellationToken cancellationToken)
    {
        if (count == 0)
        {
            return null;
        }

        var ordered = values.OrderBy(value => value);
        if (interpolateMedian)
        {
            var offset = (count - 1) / 2;
            var take = count % 2 == 0 ? 2 : 1;
            return await ordered.Skip(offset).Take(take).AverageAsync(cancellationToken);
        }

        var rank = Math.Max(1, (int)Math.Ceiling(percentile * count));
        return await ordered.Skip(rank - 1).FirstAsync(cancellationToken);
    }

    private static async Task<IReadOnlyList<ProviderStatisticsComparison>> ProviderComparisonsAsync(
        DashboardDbContext context,
        DateTimeOffset? fromUtc,
        DateTimeOffset toUtc,
        CancellationToken cancellationToken)
    {
        var comparisons = new List<ProviderStatisticsComparison>(2);
        foreach (var provider in new[] { ProviderId.LibreSpeed, ProviderId.Ookla })
        {
            var filtered = Filter(context.SpeedTestResults.AsNoTracking(), provider, fromUtc, toUtc);
            var tests = await CountTestsAsync(filtered, cancellationToken);
            var completed = filtered.Where(result => result.Status == CompletedStatus);
            comparisons.Add(new ProviderStatisticsComparison(
                provider.Value,
                tests,
                await MedianAsync(completed.Select(result => result.DownloadMbps), cancellationToken),
                await MedianAsync(completed.Select(result => result.UploadMbps), cancellationToken),
                await MedianAsync(completed.Select(result => result.LatencyMs), cancellationToken),
                await MedianAsync(completed.Select(result => result.JitterMs), cancellationToken),
                await MedianAsync(completed.Select(result => result.PacketLossPercent), cancellationToken)));
        }

        return comparisons;
    }

    private static async Task<decimal?> MedianAsync(IQueryable<double?> source, CancellationToken cancellationToken)
    {
        var values = source.Where(value => value.HasValue).Select(value => value!.Value);
        var count = await values.CountAsync(cancellationToken);
        return ToDecimal(await PercentileAsync(values, count, 0.5, interpolateMedian: true, cancellationToken));
    }

    private static async Task<IReadOnlyList<StatisticsChartPoint>> ChartAsync(
        DashboardDbContext context,
        ProviderId? providerId,
        DateTimeOffset? fromUtc,
        DateTimeOffset toUtc,
        StatisticsRange range,
        CancellationToken cancellationToken)
    {
        var bucketSeconds = await BucketSecondsAsync(context, providerId, fromUtc, toUtc, range, cancellationToken);
        var connection = context.Database.GetDbConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                datetime((unixepoch(CompletedAtUtc) / @bucketSeconds) * @bucketSeconds, 'unixepoch') AS BucketStartUtc,
                AVG(DownloadMbps),
                AVG(UploadMbps),
                AVG(LatencyMs),
                AVG(JitterMs)
            FROM SpeedTestResults
            WHERE Status = 'completed'
              AND (@provider IS NULL OR ProviderId = @provider)
              AND (@fromUtc IS NULL OR CompletedAtUtc >= @fromUtc)
              AND CompletedAtUtc <= @toUtc
            GROUP BY unixepoch(CompletedAtUtc) / @bucketSeconds
            ORDER BY unixepoch(CompletedAtUtc) / @bucketSeconds
            LIMIT 241;
            """;
        AddParameter(command, "@bucketSeconds", bucketSeconds);
        AddParameter(command, "@provider", providerId?.Value);
        AddParameter(command, "@fromUtc", fromUtc?.UtcDateTime);
        AddParameter(command, "@toUtc", toUtc.UtcDateTime);

        var points = new List<StatisticsChartPoint>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var bucket = DateTime.SpecifyKind(reader.GetDateTime(0), DateTimeKind.Utc);
            points.Add(new StatisticsChartPoint(
                new DateTimeOffset(bucket),
                ReadDecimal(reader, 1),
                ReadDecimal(reader, 2),
                ReadDecimal(reader, 3),
                ReadDecimal(reader, 4)));
        }

        return points;
    }

    private static async Task<long> BucketSecondsAsync(
        DashboardDbContext context,
        ProviderId? providerId,
        DateTimeOffset? fromUtc,
        DateTimeOffset toUtc,
        StatisticsRange range,
        CancellationToken cancellationToken)
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

        var completed = Filter(context.SpeedTestResults.AsNoTracking(), providerId, fromUtc, toUtc)
            .Where(result => result.Status == CompletedStatus);
        var earliest = await completed.MinAsync(result => (DateTime?)result.CompletedAtUtc, cancellationToken);
        if (earliest is null)
        {
            return 24 * 60 * 60;
        }

        var days = Math.Max(1, (toUtc.UtcDateTime - earliest.Value).TotalDays);
        if (days <= 240)
        {
            return 24 * 60 * 60;
        }

        var weeksPerBucket = Math.Max(1, (int)Math.Ceiling(days / (240d * 7d)));
        return weeksPerBucket * 7L * 24L * 60L * 60L;
    }

    private static void AddParameter(System.Data.Common.DbCommand command, string name, object? value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value ?? DBNull.Value;
        command.Parameters.Add(parameter);
    }

    private static decimal? ReadDecimal(System.Data.Common.DbDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : ToDecimal(reader.GetDouble(ordinal));

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

    private static decimal? ToDecimal(double? value) => value is null ? null : Round((decimal)value.Value);

    private static decimal Round(decimal value) => Math.Round(value, 3, MidpointRounding.AwayFromZero);
}
