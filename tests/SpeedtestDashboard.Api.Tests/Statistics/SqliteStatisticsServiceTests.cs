using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SpeedtestDashboard.Api.Tests.Orchestration;
using SpeedtestDashboard.Core.Providers;
using SpeedtestDashboard.Core.Statistics;
using SpeedtestDashboard.Infrastructure.Persistence;
using SpeedtestDashboard.Infrastructure.Persistence.Entities;

namespace SpeedtestDashboard.Api.Tests.Statistics;

public sealed class SqliteStatisticsServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 5, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task EmptyHistoryReturnsCountsAndNullableMetrics()
    {
        using var database = await TestDatabase.CreateAsync();

        var result = await database.Service.GetAsync(new(StatisticsRange.Last7Days, null), default);

        Assert.Equal(0, result.Tests.Total);
        Assert.Null(result.Tests.SuccessRate);
        Assert.Null(result.Download);
        Assert.Null(result.Upload);
        Assert.Null(result.Latency);
        Assert.Null(result.Jitter);
        Assert.Null(result.PacketLoss);
        Assert.Empty(result.Chart);
    }

    [Fact]
    public async Task SingleResultProducesACompleteSummaryWithoutAComparisonTrend()
    {
        using var database = await TestDatabase.CreateAsync();
        database.Add("ookla", "completed", Now.AddMinutes(-5), 250, 75, 8, .4, 0);
        await database.SaveAsync();

        var result = await database.Service.GetAsync(new(StatisticsRange.Last24Hours, null), default);

        AssertMetric(result.Download, 1, latest: 250, average: 250, median: 250, minimum: 250, maximum: 250);
        Assert.Equal(8m, result.Latency?.P95);
        Assert.Null(result.Download?.TrendPercent);
    }

    [Fact]
    public async Task CompletedMeasurementsCalculateSummaryStatisticsAndNearestRankP95()
    {
        using var database = await TestDatabase.CreateAsync();
        for (var index = 1; index <= 5; index++)
        {
            database.Add("ookla", "completed", Now.AddHours(-6 + index),
                download: index * 10, upload: index * 5, latency: index, jitter: index / 10d, packetLoss: index - 1);
        }
        await database.SaveAsync();

        var result = await database.Service.GetAsync(new(StatisticsRange.Last24Hours, null), default);

        Assert.Equal(new TestCountStatistics(5, 5, 0, 0, 100m), result.Tests);
        AssertMetric(result.Download, 5, latest: 50, average: 30, median: 30, minimum: 10, maximum: 50);
        AssertMetric(result.Upload, 5, latest: 25, average: 15, median: 15, minimum: 5, maximum: 25);
        AssertMetric(result.Latency, 5, latest: 5, average: 3, median: 3, minimum: 1, maximum: 5, p95: 5);
        AssertMetric(result.Jitter, 5, latest: .5m, average: .3m, median: .3m, minimum: .1m, maximum: .5m, p95: .5m);
        AssertMetric(result.PacketLoss, 5, latest: 4, average: 2, median: 2, minimum: 0, maximum: 4);
    }

    [Fact]
    public async Task NullAndFailedMeasurementsNeverEnterMetricsButTerminalStatesAreCounted()
    {
        using var database = await TestDatabase.CreateAsync();
        database.Add("ookla", "completed", Now.AddHours(-3), 100, 50, 10, 1, 0);
        database.Add("librespeed", "completed", Now.AddHours(-2), 120, 60, 12, 2, null);
        database.Add("ookla", "failed", Now.AddHours(-1), 9999, 9999, 9999, 9999, 99);
        database.Add("ookla", "cancelled", Now.AddMinutes(-30));
        await database.SaveAsync();

        var result = await database.Service.GetAsync(new(StatisticsRange.Last7Days, null), default);

        Assert.Equal(4, result.Tests.Total);
        Assert.Equal(2, result.Tests.Completed);
        Assert.Equal(1, result.Tests.Failed);
        Assert.Equal(1, result.Tests.Cancelled);
        Assert.Equal(50m, result.Tests.SuccessRate);
        Assert.Equal(2, result.Download?.Count);
        Assert.Equal(110m, result.Download?.Average);
        Assert.Equal(1, result.PacketLoss?.Count);
        Assert.Equal(0m, result.PacketLoss?.Median);
        Assert.DoesNotContain(result.Chart, point => point.DownloadMbps == 9999m);
    }

    [Fact]
    public async Task ProviderComparisonsAreDerivedFromStoredProviderIds()
    {
        using var database = await TestDatabase.CreateAsync();
        database.Add("custom-provider", "completed", Now.AddMinutes(-5), 100, 50, 10, 1, 0);
        await database.SaveAsync();

        var result = await database.Service.GetAsync(new(StatisticsRange.Last24Hours, null), default);

        var comparison = Assert.Single(result.Providers);
        Assert.Equal("custom-provider", comparison.Provider);
        Assert.Equal(1, comparison.Tests.Completed);
    }

    [Fact]
    public async Task ProviderFilterAndPreviousPeriodMedianTrendUseEqualAdjacentWindows()
    {
        using var database = await TestDatabase.CreateAsync();
        database.Add("ookla", "completed", Now.AddDays(-10), 100, 50, 10, 1, 0);
        database.Add("ookla", "completed", Now.AddDays(-9), 100, 50, 10, 1, 0);
        database.Add("ookla", "completed", Now.AddDays(-3), 120, 60, 9, .8, 0);
        database.Add("ookla", "completed", Now.AddDays(-2), 140, 70, 8, .6, 0);
        database.Add("librespeed", "completed", Now.AddDays(-1), 900, 400, 2, .1, null);
        await database.SaveAsync();

        var result = await database.Service.GetAsync(new(StatisticsRange.Last7Days, ProviderId.Parse("ookla")), default);

        Assert.Equal("ookla", result.Provider);
        Assert.Equal(2, result.Tests.Total);
        Assert.Equal(130m, result.Download?.Median);
        Assert.Equal(30m, result.Download?.TrendPercent);
        Assert.Equal(-15m, result.Latency?.TrendPercent);
        Assert.Null((await database.Service.GetAsync(new(StatisticsRange.AllTime, ProviderId.Parse("ookla")), default)).Download?.TrendPercent);
    }

    [Theory]
    [InlineData(StatisticsRange.Last24Hours, 1)]
    [InlineData(StatisticsRange.Last7Days, 2)]
    [InlineData(StatisticsRange.Last30Days, 3)]
    [InlineData(StatisticsRange.Last90Days, 4)]
    [InlineData(StatisticsRange.AllTime, 5)]
    public async Task EverySupportedRangeUsesTheExpectedWindow(StatisticsRange range, int expected)
    {
        using var database = await TestDatabase.CreateAsync();
        database.Add("ookla", "completed", Now.AddHours(-12), 10);
        database.Add("ookla", "completed", Now.AddDays(-2), 20);
        database.Add("ookla", "completed", Now.AddDays(-8), 30);
        database.Add("ookla", "completed", Now.AddDays(-31), 40);
        database.Add("ookla", "completed", Now.AddDays(-91), 50);
        await database.SaveAsync();

        var result = await database.Service.GetAsync(new(range, null), default);

        Assert.Equal(expected, result.Tests.Total);
    }

    [Fact]
    public async Task LargeHistoryReturnsBoundedServerSideBuckets()
    {
        using var database = await TestDatabase.CreateAsync();
        for (var index = 0; index < 5000; index++)
        {
            database.Add(index % 2 == 0 ? "ookla" : "librespeed", "completed",
                Now.AddMinutes(-index * 8), 100 + index % 20, 50, 10, 1, index % 2 == 0 ? 0 : null);
        }
        await database.SaveAsync();

        var result = await database.Service.GetAsync(new(StatisticsRange.AllTime, null), default);

        Assert.Equal(5000, result.Tests.Total);
        Assert.InRange(result.Chart.Count, 1, 241);
        Assert.True(result.Chart.Count < result.Tests.Completed);
    }

    private static void AssertMetric(
        MetricStatistics? metric,
        int count,
        decimal latest,
        decimal average,
        decimal median,
        decimal minimum,
        decimal maximum,
        decimal? p95 = null)
    {
        Assert.NotNull(metric);
        Assert.Equal(count, metric.Count);
        Assert.Equal(latest, metric.Latest);
        Assert.Equal(average, metric.Average);
        Assert.Equal(median, metric.Median);
        Assert.Equal(minimum, metric.Minimum);
        Assert.Equal(maximum, metric.Maximum);
        Assert.Equal(p95, metric.P95);
    }

    private sealed class TestDatabase : IDisposable
    {
        private readonly string _directory;
        private readonly SqliteConnectionFactory _factory;
        private readonly List<SpeedTestResultEntity> _pending = [];
        private int _sequence;

        private TestDatabase(string directory, SqliteConnectionFactory factory, SqliteStatisticsService service)
        {
            _directory = directory;
            _factory = factory;
            Service = service;
        }

        public SqliteStatisticsService Service { get; }

        public static async Task<TestDatabase> CreateAsync()
        {
            var directory = Path.Combine(Path.GetTempPath(), "speedtest-statistics-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            var storage = Options.Create(new StorageOptions { DatabasePath = Path.Combine(directory, "statistics.db") });
            var factory = new SqliteConnectionFactory(storage);
            var clock = new ManualTimeProvider(Now);
            var store = new SqliteSpeedTestStore(factory, clock, NullLogger<SqliteSpeedTestStore>.Instance);
            await new DashboardDatabaseInitializer(factory, store, storage, clock, NullLogger<DashboardDatabaseInitializer>.Instance).InitializeAsync();
            return new TestDatabase(directory, factory, new SqliteStatisticsService(factory, clock));
        }

        public void Add(
            string provider,
            string status,
            DateTimeOffset completedAt,
            double? download = null,
            double? upload = null,
            double? latency = null,
            double? jitter = null,
            double? packetLoss = null)
        {
            var id = Guid.NewGuid();
            var completed = completedAt.UtcDateTime;
            var job = new SpeedTestJobEntity
            {
                Id = id,
                ProviderId = provider,
                Status = status,
                Stage = status,
                Version = 1,
                CreatedAtUtc = completed.AddMinutes(-1),
                StartedAtUtc = completed.AddSeconds(-30),
                CompletedAtUtc = completed
            };
            _pending.Add(new SpeedTestResultEntity
            {
                Id = ++_sequence,
                JobId = id,
                Job = job,
                ProviderId = provider,
                Status = status,
                QueuedAtUtc = completed.AddMinutes(-1),
                StartedAtUtc = completed.AddSeconds(-30),
                CompletedAtUtc = completed,
                DownloadMbps = download,
                UploadMbps = upload,
                LatencyMs = latency,
                JitterMs = jitter,
                PacketLossPercent = packetLoss
            });
        }

        public async Task SaveAsync()
        {
            await using var connection = await _factory.OpenConnectionAsync();
            await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync();
            foreach (var result in _pending)
            {
                await using (var job = _factory.CreateCommand(connection, """
                    INSERT INTO SpeedTestJobs (Id, ProviderId, Status, Version, Stage, CreatedAtUtc, StartedAtUtc, CompletedAtUtc)
                    VALUES (@id, @provider, @status, 1, @status, @created, @started, @completed);
                    """, transaction))
                {
                    job.Parameters.AddWithValue("@id", result.JobId); job.Parameters.AddWithValue("@provider", result.ProviderId);
                    job.Parameters.AddWithValue("@status", result.Status); job.Parameters.AddWithValue("@created", result.QueuedAtUtc);
                    job.Parameters.AddWithValue("@started", result.StartedAtUtc!); job.Parameters.AddWithValue("@completed", result.CompletedAtUtc);
                    await job.ExecuteNonQueryAsync();
                }
                await using var row = _factory.CreateCommand(connection, """
                    INSERT INTO SpeedTestResults (Id, JobId, ProviderId, Status, QueuedAtUtc, StartedAtUtc, CompletedAtUtc,
                        DownloadMbps, UploadMbps, LatencyMs, JitterMs, PacketLossPercent, NetworkIsStale)
                    VALUES (@sequence, @jobId, @provider, @status, @queued, @started, @completed,
                        @download, @upload, @latency, @jitter, @loss, 0);
                    """, transaction);
                Add(row, "@sequence", result.Id); Add(row, "@jobId", result.JobId); Add(row, "@provider", result.ProviderId);
                Add(row, "@status", result.Status); Add(row, "@queued", result.QueuedAtUtc); Add(row, "@started", result.StartedAtUtc);
                Add(row, "@completed", result.CompletedAtUtc); Add(row, "@download", result.DownloadMbps);
                Add(row, "@upload", result.UploadMbps); Add(row, "@latency", result.LatencyMs);
                Add(row, "@jitter", result.JitterMs); Add(row, "@loss", result.PacketLossPercent);
                await row.ExecuteNonQueryAsync();
            }
            await transaction.CommitAsync();
            _pending.Clear();
        }

        private static void Add(SqliteCommand command, string name, object? value) =>
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);

        public void Dispose()
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
        }
    }

}
