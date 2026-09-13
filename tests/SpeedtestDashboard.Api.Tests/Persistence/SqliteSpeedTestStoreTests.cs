using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SpeedtestDashboard.Api.Tests.Orchestration;
using SpeedtestDashboard.Core.History;
using SpeedtestDashboard.Core.Network;
using SpeedtestDashboard.Core.Providers;
using SpeedtestDashboard.Core.Tests;
using SpeedtestDashboard.Infrastructure.Persistence;
using SpeedtestDashboard.Infrastructure.Persistence.Entities;
using SpeedtestDashboard.Infrastructure.Tests;

namespace SpeedtestDashboard.Api.Tests.Persistence;

public sealed class SqliteSpeedTestStoreTests
{
    [Theory]
    [InlineData("delete")]
    [InlineData("truncate")]
    [InlineData(null)]
    public void NonWalJournalModesSelectTheDocumentedFallbackPolicy(string? mode)
    {
        Assert.False(DashboardDatabaseInitializer.IsWalEnabled(mode));
        Assert.True(DashboardDatabaseInitializer.IsWalEnabled("WAL"));
    }

    [Fact]
    public async Task InitialMigrationCreatesFileSchemaAndWalMode()
    {
        using var database = await TestDatabase.CreateAsync();

        Assert.True(File.Exists(database.Path));
        await using var context = database.Factory.CreateDbContext();
        Assert.Empty(await context.Database.GetPendingMigrationsAsync());
        var tables = await context.Database.SqlQueryRaw<string>(
            "SELECT name AS Value FROM sqlite_master WHERE type='table'").ToListAsync();
        Assert.Contains("SpeedTestJobs", tables);
        Assert.Contains("SpeedTestResults", tables);
        Assert.Contains("__EFMigrationsHistory", tables);

        var connection = context.Database.GetDbConnection();
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA journal_mode;";
        var mode = Convert.ToString(await command.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal("wal", mode, ignoreCase: true);
    }

    [Theory]
    [InlineData(SpeedTestJobStatus.Completed)]
    [InlineData(SpeedTestJobStatus.Failed)]
    [InlineData(SpeedTestJobStatus.Cancelled)]
    public async Task EveryTerminalStateCreatesExactlyOneHistoryRecord(SpeedTestJobStatus terminalStatus)
    {
        using var database = await TestDatabase.CreateAsync();
        var memory = new InMemorySpeedTestJobStore(database.Clock, database.Store);
        var job = AdvanceToRunning(memory, database.Identity);

        if (terminalStatus == SpeedTestJobStatus.Completed)
        {
            Assert.Equal(JobMutationResult.Success, memory.Transition(job.Id, SpeedTestJobStatus.ProcessingResult, "Processing result", out _));
            Assert.Equal(JobMutationResult.Success, memory.Transition(job.Id, terminalStatus, "Completed", out _, result: Result(job.Id, database.Identity)));
        }
        else if (terminalStatus == SpeedTestJobStatus.Failed)
        {
            Assert.Equal(JobMutationResult.Success, memory.Transition(job.Id, terminalStatus, "Failed", out _, failure: new("fixture_failed", "A sanitized failure.")));
        }
        else
        {
            Assert.Equal(JobMutationResult.Success, memory.Transition(job.Id, terminalStatus, "Cancelled", out _));
        }

        var page = await database.Store.ListAsync(new HistoryQuery(null, null, null, null, 50, null), default);
        var record = Assert.Single(page.Items);
        Assert.Equal(terminalStatus, record.Status);
        Assert.Equal(job.Id, record.JobId);
        Assert.Equal(terminalStatus == SpeedTestJobStatus.Completed ? 934.625m : null, record.DownloadMbps);
        Assert.Equal(terminalStatus == SpeedTestJobStatus.Failed ? "fixture_failed" : terminalStatus == SpeedTestJobStatus.Cancelled ? "cancelled" : null, record.Failure?.Code);
    }

    [Fact]
    public async Task NormalizedMetricsMetadataUrlAndDualStackIdentityRoundTrip()
    {
        using var database = await TestDatabase.CreateAsync();
        var memory = new InMemorySpeedTestJobStore(database.Clock, database.Store);
        var job = AdvanceToRunning(memory, database.Identity);
        memory.Transition(job.Id, SpeedTestJobStatus.ProcessingResult, "Processing result", out _);
        memory.Transition(job.Id, SpeedTestJobStatus.Completed, "Completed", out _, result: Result(job.Id, database.Identity));

        var record = Assert.Single((await database.Store.ListAsync(new HistoryQuery(null, null, null, null, 10, null), default)).Items);
        Assert.Equal(934.625m, record.DownloadMbps);
        Assert.Equal(104.125m, record.UploadMbps);
        Assert.Equal(11.4m, record.LatencyMilliseconds);
        Assert.Equal(0.7m, record.JitterMilliseconds);
        Assert.Equal(0m, record.PacketLossPercent);
        Assert.Equal("https://www.speedtest.net/result/c/fixture", record.ResultUrl);
        Assert.Equal("{\"isp\":\"Fixture ISP\",\"downloadLatency\":{\"iqm\":18.2}}", record.ProviderMetadataJson);
        Assert.Equal("192.0.2.10", record.EgressIdentity?.IPv4?.Address);
        Assert.Equal("2001:db8::10", record.EgressIdentity?.IPv6?.Address);
        Assert.Equal("AS64500", record.EgressIdentity?.IPv4?.Asn);
        Assert.Equal("AS64501", record.EgressIdentity?.IPv6?.Asn);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task AddressFamiliesRemainIndependent(bool includeIpv4, bool includeIpv6)
    {
        using var database = await TestDatabase.CreateAsync();
        var identity = Identity(includeIpv4, includeIpv6);
        var memory = new InMemorySpeedTestJobStore(database.Clock, database.Store);
        var job = AdvanceToRunning(memory, identity);
        memory.Transition(job.Id, SpeedTestJobStatus.ProcessingResult, "Processing result", out _);
        memory.Transition(job.Id, SpeedTestJobStatus.Completed, "Completed", out _, result: Result(job.Id, identity));

        var record = Assert.Single((await database.Store.ListAsync(new HistoryQuery(null, null, null, null, 10, null), default)).Items);
        Assert.Equal(includeIpv4, record.EgressIdentity?.IPv4 is not null);
        Assert.Equal(includeIpv6, record.EgressIdentity?.IPv6 is not null);
    }

    [Fact]
    public async Task HistoryUsesDeterministicOrderingFiltersAndOpaqueCursor()
    {
        using var database = await TestDatabase.CreateAsync();
        for (var index = 0; index < 5; index++)
        {
            CreateCompleted(database, index % 2 == 0 ? ProviderId.Parse("provider-a") : ProviderId.Parse("fixture"));
            database.Clock.Advance(TimeSpan.FromMinutes(1));
        }

        var first = await database.Store.ListAsync(new HistoryQuery(null, null, null, null, 2, null), default);
        Assert.Equal(2, first.Items.Count);
        Assert.NotNull(first.NextCursor);
        Assert.True(first.Items[0].CompletedAtUtc > first.Items[1].CompletedAtUtc);
        var second = await database.Store.ListAsync(new HistoryQuery(null, null, null, null, 2, first.NextCursor), default);
        Assert.Equal(2, second.Items.Count);
        Assert.DoesNotContain(second.Items, item => first.Items.Any(firstItem => firstItem.Id == item.Id));

        var ookla = await database.Store.ListAsync(new HistoryQuery(ProviderId.Parse("provider-a"), SpeedTestJobStatus.Completed, null, null, 20, null), default);
        Assert.Equal(3, ookla.Items.Count);
        var window = await database.Store.ListAsync(new HistoryQuery(null, null, first.Items[1].CompletedAtUtc, first.Items[0].CompletedAtUtc, 20, null), default);
        Assert.Equal(2, window.Items.Count);
        await Assert.ThrowsAsync<ArgumentException>(() => database.Store.ListAsync(new HistoryQuery(null, null, null, null, 10, "not-a-cursor"), default));
    }

    [Fact]
    public async Task DeleteRemovesHistoryAndAssociatedTerminalJob()
    {
        using var database = await TestDatabase.CreateAsync();
        var job = CreateCompleted(database, ProviderId.Parse("provider-a"));
        var record = Assert.Single((await database.Store.ListAsync(new HistoryQuery(null, null, null, null, 10, null), default)).Items);

        var deleted = await database.Store.DeleteAsync(record.Id, default);

        Assert.True(deleted.Deleted);
        Assert.Equal(job.Id, deleted.JobId);
        Assert.Null(await database.Store.GetAsync(record.Id, default));
        Assert.Null(await database.Store.GetTerminalJobAsync(job.Id, default));
    }

    [Fact]
    public async Task FileBackedDatabaseSurvivesStoreReplacement()
    {
        using var database = await TestDatabase.CreateAsync();
        var job = CreateCompleted(database, ProviderId.Parse("provider-a"));

        var replacement = database.CreateReplacementStore();
        var persisted = await replacement.GetTerminalJobAsync(job.Id, default);

        Assert.NotNull(persisted);
        Assert.Equal(SpeedTestJobStatus.Completed, persisted.Status);
        Assert.Equal(934.625m, persisted.Result?.DownloadMbps);
    }

    [Fact]
    public async Task StartupReconciliationFailsInterruptedJobsWithoutMutatingTerminalRows()
    {
        using var database = await TestDatabase.CreateAsync();
        await using (var context = database.Factory.CreateDbContext())
        {
            foreach (var status in new[] { "queued", "starting", "running", "processingResult" })
            {
                context.SpeedTestJobs.Add(new SpeedTestJobEntity
                {
                    Id = Guid.NewGuid(),
                    ProviderId = "ookla",
                    Status = status,
                    Stage = status,
                    Version = 2,
                    CreatedAtUtc = database.Clock.GetUtcNow().UtcDateTime
                });
            }
            await context.SaveChangesAsync();
        }
        var completed = CreateCompleted(database, ProviderId.Parse("provider-a"));
        var completedBefore = await database.Store.GetTerminalJobAsync(completed.Id, default);

        Assert.Equal(4, await database.Store.ReconcileInterruptedJobsAsync(default));

        var failed = await database.Store.ListAsync(new HistoryQuery(null, SpeedTestJobStatus.Failed, null, null, 20, null), default);
        Assert.Equal(4, failed.Items.Count);
        Assert.All(failed.Items, item => Assert.Equal(SpeedTestFailureCodes.ApplicationRestarted, item.Failure?.Code));
        var completedAfter = await database.Store.GetTerminalJobAsync(completed.Id, default);
        Assert.Equal(completedBefore, completedAfter);
    }

    [Fact]
    public async Task DuplicateTerminalResultIsRejectedAndCannotCreateSecondRow()
    {
        using var database = await TestDatabase.CreateAsync();
        var job = CreateCompleted(database, ProviderId.Parse("provider-a"));
        var terminal = await database.Store.GetTerminalJobAsync(job.Id, default);

        Assert.Throws<SpeedTestPersistenceException>(() => database.Store.PersistTransition(terminal!));
        await using var context = database.Factory.CreateDbContext();
        Assert.Equal(1, await context.SpeedTestResults.CountAsync(result => result.JobId == job.Id));
    }

    [Fact]
    public async Task OversizedMetadataLeavesJobAndResultTerminalWriteAtomic()
    {
        using var database = await TestDatabase.CreateAsync();
        var memory = new InMemorySpeedTestJobStore(database.Clock, database.Store);
        var job = AdvanceToRunning(memory, database.Identity);
        memory.Transition(job.Id, SpeedTestJobStatus.ProcessingResult, "Processing result", out _);
        var oversized = Result(job.Id, database.Identity) with { ProviderMetadataJson = new string('x', StorageOptions.MaximumProviderMetadataBytes + 1) };

        var outcome = memory.Transition(job.Id, SpeedTestJobStatus.Completed, "Completed", out var failed, result: oversized);

        Assert.Equal(JobMutationResult.PersistenceFailed, outcome);
        Assert.Equal(SpeedTestFailureCodes.PersistenceFailed, failed?.Failure?.Code);
        await using var context = database.Factory.CreateDbContext();
        var persistedJob = await context.SpeedTestJobs.SingleAsync(entity => entity.Id == job.Id);
        Assert.Equal("processingResult", persistedJob.Status);
        Assert.False(await context.SpeedTestResults.AnyAsync(entity => entity.JobId == job.Id));
    }

    private static SpeedTestJob CreateCompleted(TestDatabase database, ProviderId providerId)
    {
        var memory = new InMemorySpeedTestJobStore(database.Clock, database.Store);
        var job = memory.Create(new SpeedTestRequest(providerId, "12345"));
        memory.Transition(job.Id, SpeedTestJobStatus.Starting, "Starting", out _);
        memory.Transition(job.Id, SpeedTestJobStatus.Running, "Running", out _, egressIdentity: database.Identity);
        memory.Transition(job.Id, SpeedTestJobStatus.ProcessingResult, "Processing result", out _);
        memory.Transition(job.Id, SpeedTestJobStatus.Completed, "Completed", out var completed, result: Result(job.Id, database.Identity) with { ProviderId = providerId });
        return completed!;
    }

    private static SpeedTestJob AdvanceToRunning(InMemorySpeedTestJobStore store, NetworkIdentity identity)
    {
        var job = store.Create(new SpeedTestRequest(ProviderId.Parse("provider-a"), "12345"));
        store.Transition(job.Id, SpeedTestJobStatus.Starting, "Starting", out _);
        store.Transition(job.Id, SpeedTestJobStatus.Running, "Running", out var running, egressIdentity: identity);
        return running!;
    }

    private static SpeedTestResult Result(Guid jobId, NetworkIdentity identity) => new(
        ProviderId.Parse("provider-a"), "12345", "Fixture ISP", "London, United Kingdom",
        934.625m, 104.125m, 11.4m, 0.7m, 0m,
        "https://www.speedtest.net/result/c/fixture", jobId, identity,
        "{\"isp\":\"Fixture ISP\",\"downloadLatency\":{\"iqm\":18.2}}");

    private static NetworkIdentity Identity(bool ipv4 = true, bool ipv6 = true) => new(
        ipv4 ? new NetworkAddressIdentity("192.0.2.10", NetworkAddressFamily.IPv4, "AS64500", "IPv4 Fixture", null, "GB", "United Kingdom", null, "London", "fixture", "fixture") : null,
        ipv6 ? new NetworkAddressIdentity("2001:db8::10", NetworkAddressFamily.IPv6, "AS64501", "IPv6 Fixture", null, "GB", "United Kingdom", null, "London", "fixture", "fixture") : null,
        new DateTimeOffset(2026, 9, 4, 9, 0, 0, TimeSpan.Zero),
        ipv4 && ipv6 ? NetworkIdentityState.Complete : NetworkIdentityState.Partial);

    private sealed class TestDatabase : IDisposable
    {
        private readonly string _directory;
        public string Path { get; }
        public ManualTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 9, 4, 10, 0, 0, TimeSpan.Zero));
        public NetworkIdentity Identity { get; } = SqliteSpeedTestStoreTests.Identity();
        public IDbContextFactory<DashboardDbContext> Factory { get; }
        public SqliteSpeedTestStore Store { get; }

        private TestDatabase(string directory)
        {
            _directory = directory;
            Path = System.IO.Path.Combine(directory, "speedtest.db");
            var options = new DbContextOptionsBuilder<DashboardDbContext>()
                .UseSqlite($"Data Source={Path};Mode=ReadWriteCreate;Foreign Keys=True;Default Timeout=10")
                .Options;
            Factory = new TestContextFactory(options);
            Store = new SqliteSpeedTestStore(Factory, Clock, NullLogger<SqliteSpeedTestStore>.Instance);
        }

        public static async Task<TestDatabase> CreateAsync()
        {
            var directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "speedtest-dashboard-persistence-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            var database = new TestDatabase(directory);
            var initializer = new DashboardDatabaseInitializer(
                database.Factory,
                database.Store,
                Options.Create(new StorageOptions { DatabasePath = database.Path, CommandTimeoutSeconds = 10 }),
                NullLogger<DashboardDatabaseInitializer>.Instance);
            await initializer.InitializeAsync();
            return database;
        }

        public SqliteSpeedTestStore CreateReplacementStore() => new(Factory, Clock, NullLogger<SqliteSpeedTestStore>.Instance);

        public void Dispose()
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
        }
    }

    private sealed class TestContextFactory(DbContextOptions<DashboardDbContext> options) : IDbContextFactory<DashboardDbContext>
    {
        public DashboardDbContext CreateDbContext() => new(options);
    }
}
