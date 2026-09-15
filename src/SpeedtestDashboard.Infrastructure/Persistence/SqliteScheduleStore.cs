using Microsoft.Data.Sqlite;
using SpeedtestDashboard.Core.Providers;
using SpeedtestDashboard.Core.Schedules;
using SpeedtestDashboard.Infrastructure.Persistence.Entities;

namespace SpeedtestDashboard.Infrastructure.Persistence;

public sealed class SqliteScheduleStore(
    SqliteConnectionFactory connectionFactory,
    TimeProvider timeProvider) : ISpeedTestScheduleStore
{
    private const int MaximumRunHistory = 200;
    private const string ScheduleColumns = "Id, Name, ProviderId, ServerId, RecurrenceKind, RunAtUtc, IntervalMinutes, TimeOfDayMinutes, DayOfWeek, TimeZoneId, Enabled, CreatedAtUtc, UpdatedAtUtc, LastRunAtUtc, NextRunAtUtc, LastJobId, LastRunStatus";

    public async Task<SpeedTestSchedule> CreateAsync(
        ScheduleDraft draft, DateTimeOffset? nextRunAtUtc, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var entity = new SpeedTestScheduleEntity { Id = Guid.NewGuid() };
        ApplyDraft(entity, draft);
        entity.CreatedAtUtc = now.UtcDateTime;
        entity.UpdatedAtUtc = now.UtcDateTime;
        entity.NextRunAtUtc = nextRunAtUtc?.UtcDateTime;

        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        await InsertAsync(connection, entity, cancellationToken);
        return ToDomain(entity);
    }

    public async Task<SpeedTestSchedule?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        var entity = await FindAsync(connection, id, cancellationToken);
        return entity is null ? null : ToDomain(entity);
    }

    public async Task<IReadOnlyList<SpeedTestSchedule>> ListAsync(CancellationToken cancellationToken)
    {
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        var entities = await QuerySchedulesAsync(connection,
            $"SELECT {ScheduleColumns} FROM SpeedTestSchedules ORDER BY Name;", null, cancellationToken);
        return entities.Select(ToDomain).ToArray();
    }

    public async Task<SpeedTestSchedule?> UpdateAsync(
        Guid id, ScheduleDraft draft, DateTimeOffset? nextRunAtUtc, CancellationToken cancellationToken)
    {
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        var entity = await FindAsync(connection, id, cancellationToken);
        if (entity is null)
        {
            return null;
        }

        ApplyDraft(entity, draft);
        entity.UpdatedAtUtc = timeProvider.GetUtcNow().UtcDateTime;
        entity.NextRunAtUtc = nextRunAtUtc?.UtcDateTime;
        await UpdateEntityAsync(connection, entity, cancellationToken);
        return ToDomain(entity);
    }

    public async Task<SpeedTestSchedule?> SetEnabledAsync(
        Guid id, bool enabled, DateTimeOffset? nextRunAtUtc, CancellationToken cancellationToken)
    {
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        var entity = await FindAsync(connection, id, cancellationToken);
        if (entity is null)
        {
            return null;
        }

        entity.Enabled = enabled;
        entity.UpdatedAtUtc = timeProvider.GetUtcNow().UtcDateTime;
        entity.NextRunAtUtc = nextRunAtUtc?.UtcDateTime;
        await UpdateEntityAsync(connection, entity, cancellationToken);
        return ToDomain(entity);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connectionFactory.CreateCommand(connection, "DELETE FROM SpeedTestSchedules WHERE Id = @id;");
        command.Parameters.AddWithValue("@id", id);
        var deleted = await command.ExecuteNonQueryAsync(cancellationToken);
        return deleted > 0;
    }

    public async Task<IReadOnlyList<SpeedTestSchedule>> GetDueAsync(DateTimeOffset asOfUtc, CancellationToken cancellationToken)
    {
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        var entities = await QuerySchedulesAsync(connection,
            $"SELECT {ScheduleColumns} FROM SpeedTestSchedules WHERE Enabled = 1 AND NextRunAtUtc IS NOT NULL AND NextRunAtUtc <= @value;",
            asOfUtc.UtcDateTime, cancellationToken);
        return entities.Select(ToDomain).ToArray();
    }

    public async Task<bool> TryClaimRunAsync(
        Guid scheduleId,
        DateTimeOffset expectedNextRunAtUtc,
        ScheduleRun run,
        DateTimeOffset? nextRunAtUtc,
        CancellationToken cancellationToken)
    {
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);

        var expected = expectedNextRunAtUtc.UtcDateTime;
        var attempted = run.AttemptedAtUtc.UtcDateTime;
        var next = nextRunAtUtc?.UtcDateTime;
        await using var claim = connectionFactory.CreateCommand(connection, """
            UPDATE SpeedTestSchedules SET LastRunAtUtc = @attempted, LastRunStatus = 'pending', NextRunAtUtc = @next
            WHERE Id = @id AND Enabled = 1 AND NextRunAtUtc = @expected;
            """, transaction);
        Add(claim, "@attempted", attempted); Add(claim, "@next", next);
        Add(claim, "@id", scheduleId); Add(claim, "@expected", expected);
        var claimed = await claim.ExecuteNonQueryAsync(cancellationToken);
        if (claimed == 0)
        {
            await transaction.RollbackAsync(cancellationToken);
            return false;
        }

        await using var insert = connectionFactory.CreateCommand(connection, """
            INSERT INTO ScheduleRuns (Id, ScheduleId, ScheduledForUtc, AttemptedAtUtc, JobId, Status, FailureCode)
            VALUES (@id, @scheduleId, @scheduled, @attempted, @jobId, @status, @failureCode);
            """, transaction);
        Add(insert, "@id", run.Id); Add(insert, "@scheduleId", scheduleId);
        Add(insert, "@scheduled", run.ScheduledForUtc.UtcDateTime); Add(insert, "@attempted", attempted);
        Add(insert, "@jobId", run.JobId); Add(insert, "@status", run.Status.ToString().ToLowerInvariant());
        Add(insert, "@failureCode", run.FailureCode);
        await insert.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    public async Task CompleteRunAsync(
        Guid scheduleId,
        Guid runId,
        Guid? jobId,
        ScheduleRunStatus status,
        string? failureCode,
        CancellationToken cancellationToken)
    {
        if (status == ScheduleRunStatus.Pending)
        {
            throw new ArgumentOutOfRangeException(nameof(status), "A claimed run must be completed with a terminal admission status.");
        }

        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        await using var updateRun = connectionFactory.CreateCommand(connection, """
            UPDATE ScheduleRuns SET JobId = @jobId, Status = @status, FailureCode = @failureCode
            WHERE Id = @runId AND ScheduleId = @scheduleId AND Status = 'pending';
            """, transaction);
        var statusValue = status.ToString().ToLowerInvariant();
        Add(updateRun, "@jobId", jobId); Add(updateRun, "@status", statusValue); Add(updateRun, "@failureCode", failureCode);
        Add(updateRun, "@runId", runId); Add(updateRun, "@scheduleId", scheduleId);
        if (await updateRun.ExecuteNonQueryAsync(cancellationToken) == 0)
        {
            await transaction.RollbackAsync(cancellationToken);
            return;
        }

        await using var updateSchedule = connectionFactory.CreateCommand(connection, """
            UPDATE SpeedTestSchedules SET LastJobId = COALESCE(@jobId, LastJobId), LastRunStatus = @status
            WHERE Id = @scheduleId AND LastRunAtUtc = (SELECT AttemptedAtUtc FROM ScheduleRuns WHERE Id = @runId);
            """, transaction);
        Add(updateSchedule, "@jobId", jobId); Add(updateSchedule, "@status", statusValue);
        Add(updateSchedule, "@scheduleId", scheduleId); Add(updateSchedule, "@runId", runId);
        await updateSchedule.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task RecoverPendingRunsAsync(CancellationToken cancellationToken)
    {
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        await using (var schedules = connectionFactory.CreateCommand(connection, """
            UPDATE SpeedTestSchedules SET LastRunStatus = 'skipped'
            WHERE LastRunStatus = 'pending' AND EXISTS (
                SELECT 1 FROM ScheduleRuns r WHERE r.ScheduleId = SpeedTestSchedules.Id
                  AND r.AttemptedAtUtc = SpeedTestSchedules.LastRunAtUtc AND r.Status = 'pending');
            """, transaction))
        {
            await schedules.ExecuteNonQueryAsync(cancellationToken);
        }
        await using (var runs = connectionFactory.CreateCommand(connection,
            "UPDATE ScheduleRuns SET Status = 'skipped', FailureCode = @failureCode WHERE Status = 'pending';", transaction))
        {
            runs.Parameters.AddWithValue("@failureCode", ScheduleFailureCodes.ApplicationOffline);
            await runs.ExecuteNonQueryAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ScheduleRun>> ListRunsAsync(Guid scheduleId, int limit, CancellationToken cancellationToken)
    {
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connectionFactory.CreateCommand(connection, """
            SELECT Id, ScheduleId, ScheduledForUtc, AttemptedAtUtc, JobId, Status, FailureCode
            FROM ScheduleRuns WHERE ScheduleId = @id ORDER BY AttemptedAtUtc DESC LIMIT @limit;
            """);
        command.Parameters.AddWithValue("@id", scheduleId);
        command.Parameters.AddWithValue("@limit", Math.Clamp(limit, 1, MaximumRunHistory));
        var entities = new List<ScheduleRunEntity>();
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken)) entities.Add(ReadRun(reader));
        }
        return entities.Select(ToDomain).ToArray();
    }

    private async Task InsertAsync(SqliteConnection connection, SpeedTestScheduleEntity entity, CancellationToken cancellationToken)
    {
        await using var command = connectionFactory.CreateCommand(connection, """
            INSERT INTO SpeedTestSchedules
                (Id, Name, ProviderId, ServerId, RecurrenceKind, RunAtUtc, IntervalMinutes, TimeOfDayMinutes,
                 DayOfWeek, TimeZoneId, Enabled, CreatedAtUtc, UpdatedAtUtc, LastRunAtUtc, NextRunAtUtc, LastJobId, LastRunStatus)
            VALUES
                (@Id, @Name, @ProviderId, @ServerId, @RecurrenceKind, @RunAtUtc, @IntervalMinutes, @TimeOfDayMinutes,
                 @DayOfWeek, @TimeZoneId, @Enabled, @CreatedAtUtc, @UpdatedAtUtc, @LastRunAtUtc, @NextRunAtUtc, @LastJobId, @LastRunStatus);
            """);
        BindSchedule(command, entity);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task UpdateEntityAsync(SqliteConnection connection, SpeedTestScheduleEntity entity, CancellationToken cancellationToken)
    {
        await using var command = connectionFactory.CreateCommand(connection, """
            UPDATE SpeedTestSchedules SET Name = @Name, ProviderId = @ProviderId, ServerId = @ServerId,
                RecurrenceKind = @RecurrenceKind, RunAtUtc = @RunAtUtc, IntervalMinutes = @IntervalMinutes,
                TimeOfDayMinutes = @TimeOfDayMinutes, DayOfWeek = @DayOfWeek, TimeZoneId = @TimeZoneId,
                Enabled = @Enabled, UpdatedAtUtc = @UpdatedAtUtc, NextRunAtUtc = @NextRunAtUtc
            WHERE Id = @Id;
            """);
        BindSchedule(command, entity);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<SpeedTestScheduleEntity?> FindAsync(
        SqliteConnection connection, Guid id, CancellationToken cancellationToken)
    {
        var values = await QuerySchedulesAsync(connection,
            $"SELECT {ScheduleColumns} FROM SpeedTestSchedules WHERE Id = @value;", id, cancellationToken);
        return values.Count == 0 ? null : values[0];
    }

    private async Task<List<SpeedTestScheduleEntity>> QuerySchedulesAsync(
        SqliteConnection connection, string sql, object? value, CancellationToken cancellationToken)
    {
        await using var command = connectionFactory.CreateCommand(connection, sql);
        if (value is not null) command.Parameters.AddWithValue("@value", value);
        var values = new List<SpeedTestScheduleEntity>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) values.Add(ReadSchedule(reader));
        return values;
    }

    private static void BindSchedule(SqliteCommand command, SpeedTestScheduleEntity entity)
    {
        Add(command, "@Id", entity.Id); Add(command, "@Name", entity.Name); Add(command, "@ProviderId", entity.ProviderId);
        Add(command, "@ServerId", entity.ServerId); Add(command, "@RecurrenceKind", entity.RecurrenceKind);
        Add(command, "@RunAtUtc", entity.RunAtUtc); Add(command, "@IntervalMinutes", entity.IntervalMinutes);
        Add(command, "@TimeOfDayMinutes", entity.TimeOfDayMinutes); Add(command, "@DayOfWeek", entity.DayOfWeek);
        Add(command, "@TimeZoneId", entity.TimeZoneId); Add(command, "@Enabled", entity.Enabled);
        Add(command, "@CreatedAtUtc", entity.CreatedAtUtc); Add(command, "@UpdatedAtUtc", entity.UpdatedAtUtc);
        Add(command, "@LastRunAtUtc", entity.LastRunAtUtc); Add(command, "@NextRunAtUtc", entity.NextRunAtUtc);
        Add(command, "@LastJobId", entity.LastJobId); Add(command, "@LastRunStatus", entity.LastRunStatus);
    }

    private static SpeedTestScheduleEntity ReadSchedule(SqliteDataReader reader) => new()
    {
        Id = reader.GetGuid(0),
        Name = reader.GetString(1),
        ProviderId = reader.GetString(2),
        ServerId = Text(reader, 3),
        RecurrenceKind = reader.GetString(4),
        RunAtUtc = Date(reader, 5),
        IntervalMinutes = Integer(reader, 6),
        TimeOfDayMinutes = Integer(reader, 7),
        DayOfWeek = Integer(reader, 8),
        TimeZoneId = reader.GetString(9),
        Enabled = reader.GetBoolean(10),
        CreatedAtUtc = reader.GetDateTime(11),
        UpdatedAtUtc = reader.GetDateTime(12),
        LastRunAtUtc = Date(reader, 13),
        NextRunAtUtc = Date(reader, 14),
        LastJobId = GuidValue(reader, 15),
        LastRunStatus = Text(reader, 16)
    };

    private static ScheduleRunEntity ReadRun(SqliteDataReader reader) => new()
    {
        Id = reader.GetGuid(0),
        ScheduleId = reader.GetGuid(1),
        ScheduledForUtc = reader.GetDateTime(2),
        AttemptedAtUtc = reader.GetDateTime(3),
        JobId = GuidValue(reader, 4),
        Status = reader.GetString(5),
        FailureCode = Text(reader, 6)
    };

    private static void Add(SqliteCommand command, string name, object? value) =>
        command.Parameters.AddWithValue(name, value ?? DBNull.Value);
    private static string? Text(SqliteDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    private static DateTime? Date(SqliteDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : reader.GetDateTime(ordinal);
    private static int? Integer(SqliteDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : reader.GetInt32(ordinal);
    private static Guid? GuidValue(SqliteDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : reader.GetGuid(ordinal);

    private static void ApplyDraft(SpeedTestScheduleEntity entity, ScheduleDraft draft)
    {
        entity.Name = draft.Name;
        entity.ProviderId = draft.ProviderId.Value;
        entity.ServerId = draft.ServerId;
        entity.RecurrenceKind = draft.RecurrenceKind.ToString().ToLowerInvariant();
        entity.RunAtUtc = draft.RunAtUtc?.UtcDateTime;
        entity.IntervalMinutes = draft.IntervalMinutes;
        entity.TimeOfDayMinutes = draft.TimeOfDayMinutes;
        entity.DayOfWeek = draft.DayOfWeek is null ? null : (int)draft.DayOfWeek.Value;
        entity.TimeZoneId = draft.TimeZoneId;
        entity.Enabled = draft.Enabled;
    }

    private static SpeedTestSchedule ToDomain(SpeedTestScheduleEntity entity) => new(
        entity.Id,
        entity.Name,
        ProviderId.Parse(entity.ProviderId),
        entity.ServerId,
        Enum.Parse<ScheduleRecurrenceKind>(entity.RecurrenceKind, ignoreCase: true),
        AsUtcOffset(entity.RunAtUtc),
        entity.IntervalMinutes,
        entity.TimeOfDayMinutes,
        entity.DayOfWeek is null ? null : (DayOfWeek)entity.DayOfWeek.Value,
        entity.TimeZoneId,
        entity.Enabled,
        AsUtcOffset(entity.CreatedAtUtc)!.Value,
        AsUtcOffset(entity.UpdatedAtUtc)!.Value,
        AsUtcOffset(entity.LastRunAtUtc),
        AsUtcOffset(entity.NextRunAtUtc),
        entity.LastJobId,
        entity.LastRunStatus);

    private static ScheduleRun ToDomain(ScheduleRunEntity entity) => new(
        entity.Id,
        entity.ScheduleId,
        AsUtcOffset(entity.ScheduledForUtc)!.Value,
        AsUtcOffset(entity.AttemptedAtUtc)!.Value,
        entity.JobId,
        Enum.Parse<ScheduleRunStatus>(entity.Status, ignoreCase: true),
        entity.FailureCode);

    private static DateTimeOffset? AsUtcOffset(DateTime? value) =>
        value is null ? null : new DateTimeOffset(DateTime.SpecifyKind(value.Value, DateTimeKind.Utc));
}
