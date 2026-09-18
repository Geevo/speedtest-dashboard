using Microsoft.Data.Sqlite;
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
    public async Task InitialMigrationCreatesSchemaJournalAndWalMode()
    {
        using var database = await TestDatabase.CreateAsync();

        Assert.True(File.Exists(database.Path));
        await using var connection = await database.Factory.OpenConnectionAsync();
        var tables = new List<string>();
        await using (var tableCommand = database.Factory.CreateCommand(connection, "SELECT name FROM sqlite_master WHERE type='table';"))
        await using (var reader = await tableCommand.ExecuteReaderAsync())
            while (await reader.ReadAsync()) tables.Add(reader.GetString(0));
        Assert.Contains("SpeedTestJobs", tables);
        Assert.Contains("SpeedTestResults", tables);
        Assert.Contains("SchemaVersions", tables);

        await using (var journalCommand = database.Factory.CreateCommand(connection,
                         "SELECT ScriptName FROM SchemaVersions ORDER BY SchemaVersionID;"))
        await using (var reader = await journalCommand.ExecuteReaderAsync())
        {
            Assert.True(await reader.ReadAsync());
            Assert.EndsWith("0001_initial.sql", reader.GetString(0), StringComparison.Ordinal);
            Assert.False(await reader.ReadAsync());
        }

        await using var command = database.Factory.CreateCommand(connection, "PRAGMA journal_mode;");
        var mode = Convert.ToString(await command.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal("wal", mode, ignoreCase: true);
        Assert.False(Directory.Exists(database.BackupDirectory));
    }

    [Fact]
    public async Task InitialMigrationIsNotAppliedAgainOnRestart()
    {
        using var database = await TestDatabase.CreateAsync();

        await database.InitializeAsync();

        await using var connection = await database.Factory.OpenConnectionAsync();
        await using var command = database.Factory.CreateCommand(connection, "SELECT COUNT(*) FROM SchemaVersions;");
        Assert.Equal(1L, Convert.ToInt64(await command.ExecuteScalarAsync()));
    }

    [Fact]
    public async Task ApplicationTablesUseStrictSQLiteTyping()
    {
        using var database = await TestDatabase.CreateAsync();
        var expected = new HashSet<string>(StringComparer.Ordinal)
        {
            "SpeedTestJobs",
            "SpeedTestResults",
            "Users",
            "DashboardSettings",
            "ApiCredentials",
            "ApiIdempotencyRecords",
            "SpeedTestSchedules",
            "ScheduleRuns"
        };

        await using var connection = await database.Factory.OpenConnectionAsync();
        await using var command = database.Factory.CreateCommand(connection, "PRAGMA table_list;");
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            if (expected.Remove(reader.GetString(1)))
            {
                Assert.Equal(1L, reader.GetInt64(5));
            }
        }

        Assert.Empty(expected);
    }

    [Fact]
    public async Task SchemaRejectsInvalidDomainValuesAndOrphanedReferences()
    {
        using var database = await TestDatabase.CreateAsync();
        await database.ExecuteAsync("""
            INSERT INTO SpeedTestJobs (Id, ProviderId, Status, Version, Stage, CreatedAtUtc)
            VALUES ('00000000-0000-0000-0000-000000000010', 'fixture', 'queued', 1, 'Queued', '2026-09-04T10:00:00Z');
            """);
        await database.ExecuteAsync("""
            INSERT INTO Users (Id, UserName, NormalizedUserName, PasswordHash, SecurityStamp, CreatedAtUtc)
            VALUES ('00000000-0000-0000-0000-000000000020', 'first', 'FIRST', 'hash', 'stamp', '2026-09-04T10:00:00Z');
            """);

        var invalidStatements = new[]
        {
            """
            INSERT INTO SpeedTestJobs (Id, ProviderId, Status, Version, Stage, CreatedAtUtc)
            VALUES ('00000000-0000-0000-0000-000000000011', 'fixture', 'unknown', 1, 'Unknown', '2026-09-04T10:00:00Z');
            """,
            """
            INSERT INTO SpeedTestResults
                (JobId, ProviderId, Status, QueuedAtUtc, CompletedAtUtc, DownloadMbps, NetworkIsStale)
            VALUES
                ('00000000-0000-0000-0000-000000000010', 'fixture', 'completed',
                 '2026-09-04T10:00:00Z', '2026-09-04T10:01:00Z', -1, 0);
            """,
            """
            INSERT INTO SpeedTestResults
                (JobId, ProviderId, Status, QueuedAtUtc, CompletedAtUtc, ProviderMetadataJson, NetworkIsStale)
            VALUES
                ('00000000-0000-0000-0000-000000000010', 'fixture', 'completed',
                 '2026-09-04T10:00:00Z', '2026-09-04T10:01:00Z', 'not-json', 0);
            """,
            "UPDATE DashboardSettings SET AuthenticationEnabled = 2 WHERE Id = 1;",
            """
            INSERT INTO SpeedTestSchedules
                (Id, Name, ProviderId, RecurrenceKind, TimeZoneId, Enabled, CreatedAtUtc, UpdatedAtUtc)
            VALUES
                ('00000000-0000-0000-0000-000000000030', 'Invalid interval', 'fixture',
                 'interval', 'UTC', 1, '2026-09-04T10:00:00Z', '2026-09-04T10:00:00Z');
            """,
            """
            INSERT INTO ScheduleRuns (Id, ScheduleId, ScheduledForUtc, AttemptedAtUtc, Status)
            VALUES ('00000000-0000-0000-0000-000000000040',
                    '00000000-0000-0000-0000-000000000099',
                    '2026-09-04T10:00:00Z', '2026-09-04T10:00:00Z', 'pending');
            """,
            """
            INSERT INTO ApiIdempotencyRecords (Key, RequestHash, JobId, CreatedAtUtc)
            VALUES ('orphan', 'AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA',
                    '00000000-0000-0000-0000-000000000099', '2026-09-04T10:00:00Z');
            """,
            """
            INSERT INTO Users (Id, UserName, NormalizedUserName, PasswordHash, SecurityStamp, CreatedAtUtc)
            VALUES ('00000000-0000-0000-0000-000000000021', 'second', 'SECOND', 'hash', 'stamp', '2026-09-04T10:00:00Z');
            """
        };

        foreach (var sql in invalidStatements)
        {
            var exception = await Assert.ThrowsAsync<SqliteException>(() => database.ExecuteAsync(sql));
            Assert.Equal(19, exception.SqliteErrorCode);
        }
    }

    [Fact]
    public async Task ForeignKeyActionsFollowRecordOwnership()
    {
        using var database = await TestDatabase.CreateAsync();
        await database.ExecuteAsync("""
            INSERT INTO SpeedTestJobs
                (Id, ProviderId, Status, Version, Stage, CreatedAtUtc, CompletedAtUtc)
            VALUES
                ('00000000-0000-0000-0000-000000000050', 'fixture', 'completed', 2, 'Completed',
                 '2026-09-04T10:00:00Z', '2026-09-04T10:01:00Z');
            INSERT INTO SpeedTestSchedules
                (Id, Name, ProviderId, RecurrenceKind, IntervalMinutes, TimeZoneId, Enabled,
                 CreatedAtUtc, UpdatedAtUtc, LastJobId, LastRunStatus)
            VALUES
                ('00000000-0000-0000-0000-000000000051', 'Hourly', 'fixture', 'interval', 60, 'UTC', 1,
                 '2026-09-04T09:00:00Z', '2026-09-04T09:00:00Z',
                 '00000000-0000-0000-0000-000000000050', 'queued');
            INSERT INTO ScheduleRuns
                (Id, ScheduleId, ScheduledForUtc, AttemptedAtUtc, JobId, Status)
            VALUES
                ('00000000-0000-0000-0000-000000000052',
                 '00000000-0000-0000-0000-000000000051',
                 '2026-09-04T10:00:00Z', '2026-09-04T10:00:00Z',
                 '00000000-0000-0000-0000-000000000050', 'queued');
            INSERT INTO ApiIdempotencyRecords (Key, RequestHash, JobId, CreatedAtUtc)
            VALUES ('request-1', 'AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA',
                    '00000000-0000-0000-0000-000000000050', '2026-09-04T10:00:00Z');
            """);

        await database.ExecuteAsync(
            "DELETE FROM SpeedTestJobs WHERE Id = '00000000-0000-0000-0000-000000000050';");

        Assert.Equal(0L, Convert.ToInt64(await database.ScalarAsync("SELECT COUNT(*) FROM ApiIdempotencyRecords;")));
        Assert.Equal(DBNull.Value, await database.ScalarAsync("SELECT LastJobId FROM SpeedTestSchedules LIMIT 1;"));
        Assert.Equal(DBNull.Value, await database.ScalarAsync("SELECT JobId FROM ScheduleRuns LIMIT 1;"));

        await database.ExecuteAsync(
            "DELETE FROM SpeedTestSchedules WHERE Id = '00000000-0000-0000-0000-000000000051';");

        Assert.Equal(0L, Convert.ToInt64(await database.ScalarAsync("SELECT COUNT(*) FROM ScheduleRuns;")));
    }

    [Fact]
    public async Task PendingMigrationCreatesRestorableBackupBeforeUpgradeAttempt()
    {
        using var database = await TestDatabase.CreateAsync();
        var job = CreateCompleted(database, ProviderId.Parse("provider-a"));
        await database.ExecuteAsync("DELETE FROM SchemaVersions;");

        await Assert.ThrowsAsync<InvalidOperationException>(() => database.InitializeAsync());

        var backupPath = Assert.Single(Directory.GetFiles(database.BackupDirectory, "*.db"));
        await using var backup = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = backupPath,
            Mode = SqliteOpenMode.ReadOnly,
            ForeignKeys = true
        }.ToString());
        await backup.OpenAsync();

        await using (var jobCommand = backup.CreateCommand())
        {
            jobCommand.CommandText = "SELECT COUNT(*) FROM SpeedTestJobs WHERE Id = @id;";
            jobCommand.Parameters.AddWithValue("@id", job.Id);
            Assert.Equal(1L, Convert.ToInt64(await jobCommand.ExecuteScalarAsync()));
        }

        await using var integrityCommand = backup.CreateCommand();
        integrityCommand.CommandText = "PRAGMA quick_check;";
        Assert.Equal("ok", Convert.ToString(await integrityCommand.ExecuteScalarAsync()));
    }

    [Fact]
    public async Task MigrationBackupRetentionOnlyDeletesManagedBackups()
    {
        using var database = await TestDatabase.CreateAsync(migrationBackupRetentionCount: 2);
        await database.ExecuteAsync("DELETE FROM SchemaVersions;");
        Directory.CreateDirectory(database.BackupDirectory);
        var userBackup = System.IO.Path.Combine(database.BackupDirectory, "user-created.db");
        await File.WriteAllTextAsync(userBackup, "preserve me");

        for (var attempt = 0; attempt < 3; attempt++)
        {
            database.Clock.Advance(TimeSpan.FromSeconds(1));
            await Assert.ThrowsAsync<InvalidOperationException>(() => database.InitializeAsync());
        }

        Assert.Equal(2, Directory.GetFiles(database.BackupDirectory, "speedtest.pre-migration-*.db").Length);
        Assert.True(File.Exists(userBackup));
    }

    [Fact]
    public async Task StartupRejectsForeignKeyViolationsBeforeServingTraffic()
    {
        using var database = await TestDatabase.CreateAsync();
        SqliteConnection.ClearAllPools();
        await using (var connection = new SqliteConnection($"Data Source={database.Path};Foreign Keys=False"))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO SpeedTestResults (
                    JobId, ProviderId, Status, QueuedAtUtc, CompletedAtUtc, FailureCode, NetworkIsStale)
                VALUES (
                    '00000000-0000-0000-0000-000000000001', 'fixture', 'failed',
                    '2026-09-04T10:00:00Z', '2026-09-04T10:01:00Z', 'fixture_failure', 0);
                """;
            await command.ExecuteNonQueryAsync();
        }

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => database.InitializeAsync());

        Assert.Contains("foreign key check failed", exception.Message, StringComparison.OrdinalIgnoreCase);
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
    public async Task DeleteAllRemovesEveryHistoryRecordAndAssociatedTerminalJob()
    {
        using var database = await TestDatabase.CreateAsync();
        var first = CreateCompleted(database, ProviderId.Parse("provider-a"));
        var second = CreateCompleted(database, ProviderId.Parse("fixture"));

        var deleted = await database.Store.DeleteAllAsync(default);

        Assert.Equal(2, deleted.DeletedCount);
        Assert.Contains(first.Id, deleted.JobIds);
        Assert.Contains(second.Id, deleted.JobIds);
        Assert.Empty((await database.Store.ListAsync(new HistoryQuery(null, null, null, null, 10, null), default)).Items);
        Assert.Null(await database.Store.GetTerminalJobAsync(first.Id, default));
        Assert.Null(await database.Store.GetTerminalJobAsync(second.Id, default));
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
        await using (var connection = await database.Factory.OpenConnectionAsync())
        {
            foreach (var status in new[] { "queued", "starting", "running", "processingResult" })
            {
                await using var command = database.Factory.CreateCommand(connection, """
                    INSERT INTO SpeedTestJobs (Id, ProviderId, Status, Version, Stage, CreatedAtUtc)
                    VALUES (@id, 'ookla', @status, 2, @status, @created);
                    """);
                command.Parameters.AddWithValue("@id", Guid.NewGuid());
                command.Parameters.AddWithValue("@status", status);
                command.Parameters.AddWithValue("@created", database.Clock.GetUtcNow().UtcDateTime);
                await command.ExecuteNonQueryAsync();
            }
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
        Assert.Equal(1L, await database.ScalarAsync("SELECT COUNT(*) FROM SpeedTestResults WHERE JobId = @id;", job.Id));
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
        Assert.Equal("processingResult", await database.ScalarAsync("SELECT Status FROM SpeedTestJobs WHERE Id = @id;", job.Id));
        Assert.Equal(0L, await database.ScalarAsync("SELECT COUNT(*) FROM SpeedTestResults WHERE JobId = @id;", job.Id));
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
        public SqliteConnectionFactory Factory { get; }
        public SqliteSpeedTestStore Store { get; }
        public string BackupDirectory => System.IO.Path.Combine(_directory, "backups");

        private readonly StorageOptions _options;

        private TestDatabase(string directory, int migrationBackupRetentionCount)
        {
            _directory = directory;
            Path = System.IO.Path.Combine(directory, "speedtest.db");
            _options = new StorageOptions
            {
                DatabasePath = Path,
                CommandTimeoutSeconds = 10,
                MigrationBackupRetentionCount = migrationBackupRetentionCount
            };
            Factory = new SqliteConnectionFactory(Options.Create(_options));
            Store = new SqliteSpeedTestStore(Factory, Clock, NullLogger<SqliteSpeedTestStore>.Instance);
        }

        public static async Task<TestDatabase> CreateAsync(int migrationBackupRetentionCount = 3)
        {
            var directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "speedtest-dashboard-persistence-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            var database = new TestDatabase(directory, migrationBackupRetentionCount);
            await database.InitializeAsync();
            return database;
        }

        public Task InitializeAsync()
        {
            var initializer = new DashboardDatabaseInitializer(
                Factory,
                Store,
                Options.Create(_options),
                Clock,
                NullLogger<DashboardDatabaseInitializer>.Instance);
            return initializer.InitializeAsync();
        }

        public SqliteSpeedTestStore CreateReplacementStore() => new(Factory, Clock, NullLogger<SqliteSpeedTestStore>.Instance);

        public async Task<object?> ScalarAsync(string sql, Guid id)
        {
            await using var connection = await Factory.OpenConnectionAsync();
            await using var command = Factory.CreateCommand(connection, sql);
            command.Parameters.AddWithValue("@id", id);
            return await command.ExecuteScalarAsync();
        }

        public async Task<object?> ScalarAsync(string sql)
        {
            await using var connection = await Factory.OpenConnectionAsync();
            await using var command = Factory.CreateCommand(connection, sql);
            return await command.ExecuteScalarAsync();
        }

        public async Task ExecuteAsync(string sql)
        {
            await using var connection = await Factory.OpenConnectionAsync();
            await using var command = Factory.CreateCommand(connection, sql);
            await command.ExecuteNonQueryAsync();
        }

        public void Dispose()
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
        }
    }

}
