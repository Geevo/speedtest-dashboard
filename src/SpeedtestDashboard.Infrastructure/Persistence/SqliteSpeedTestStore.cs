using System.Text;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using SpeedtestDashboard.Core.History;
using SpeedtestDashboard.Core.Network;
using SpeedtestDashboard.Core.Providers;
using SpeedtestDashboard.Core.Tests;
using SpeedtestDashboard.Infrastructure.Persistence.Entities;

namespace SpeedtestDashboard.Infrastructure.Persistence;

public sealed class SqliteSpeedTestStore(
    SqliteConnectionFactory connectionFactory,
    TimeProvider timeProvider,
    ILogger<SqliteSpeedTestStore> logger) : ISpeedTestPersistenceWriter, ISpeedTestHistoryStore
{
    private const string JobColumns = "Id, ProviderId, Status, Version, Stage, CreatedAtUtc, StartedAtUtc, CompletedAtUtc, RequestedServerId, FailureCode, FailureMessage";
    private const string ResultColumns = "Id, JobId, ProviderId, Status, QueuedAtUtc, StartedAtUtc, CompletedAtUtc, RequestedServerId, ServerId, ServerName, ServerLocation, ServerCountry, DownloadMbps, UploadMbps, LatencyMs, JitterMs, PacketLossPercent, ResultUrl, FailureCode, FailureMessage, ProviderMetadataJson, NetworkState, NetworkCheckedAtUtc, NetworkIsStale, NetworkWarning, IPv4Address, IPv4Asn, IPv4AsName, IPv4Isp, IPv4CountryCode, IPv4CountryName, IPv4Region, IPv4City, IPv4AddressSource, IPv4MetadataSource, IPv6Address, IPv6Asn, IPv6AsName, IPv6Isp, IPv6CountryCode, IPv6CountryName, IPv6Region, IPv6City, IPv6AddressSource, IPv6MetadataSource";

    public void PersistCreated(SpeedTestJob job)
    {
        try
        {
            using var connection = connectionFactory.CreateConnection();
            connection.Open();
            using var command = connectionFactory.CreateCommand(connection, """
                INSERT INTO SpeedTestJobs
                    (Id, ProviderId, Status, Version, Stage, CreatedAtUtc, StartedAtUtc, CompletedAtUtc, RequestedServerId, FailureCode, FailureMessage)
                VALUES
                    (@id, @providerId, @status, @version, @stage, @createdAtUtc, @startedAtUtc, @completedAtUtc, @requestedServerId, @failureCode, @failureMessage);
                """);
            BindJob(command, MapJob(job));
            command.ExecuteNonQuery();
        }
        catch (Exception exception)
        {
            throw Wrap("The speed-test job could not be persisted.", exception);
        }
    }

    public void PersistTransition(SpeedTestJob job)
    {
        try
        {
            using var connection = connectionFactory.CreateConnection();
            connection.Open();
            using var transaction = job.IsTerminal ? connection.BeginTransaction() : null;
            using var command = connectionFactory.CreateCommand(connection, """
                UPDATE SpeedTestJobs SET
                    Status = @status, Version = @version, Stage = @stage, StartedAtUtc = @startedAtUtc,
                    CompletedAtUtc = @completedAtUtc, FailureCode = @failureCode, FailureMessage = @failureMessage
                WHERE Id = @id;
                """, transaction);
            var entity = MapJob(job);
            BindJob(command, entity);
            if (command.ExecuteNonQuery() != 1)
            {
                throw new InvalidOperationException("The persisted speed-test job was not found.");
            }

            if (job.IsTerminal)
            {
                InsertResult(connection, transaction!, MapResult(job, entity));
            }

            transaction?.Commit();
        }
        catch (Exception exception)
        {
            throw Wrap("The speed-test state could not be durably committed.", exception);
        }
    }

    public void DeleteQueued(Guid jobId)
    {
        try
        {
            using var connection = connectionFactory.CreateConnection();
            connection.Open();
            using var command = connectionFactory.CreateCommand(connection,
                "DELETE FROM SpeedTestJobs WHERE Id = @id AND Status = @status;");
            command.Parameters.AddWithValue("@id", jobId);
            command.Parameters.AddWithValue("@status", StatusValue(SpeedTestJobStatus.Queued));
            command.ExecuteNonQuery();
        }
        catch (Exception exception)
        {
            throw Wrap("The rejected speed-test job could not be removed from storage.", exception);
        }
    }

    public async Task<HistoryPage> ListAsync(HistoryQuery query, CancellationToken cancellationToken)
    {
        if (query.Cursor is not null && !HistoryCursor.TryDecode(query.Cursor, out _))
        {
            throw new ArgumentException("The history cursor is invalid.", nameof(query));
        }

        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        var where = new List<string>();
        await using var command = connectionFactory.CreateCommand(connection, string.Empty);

        if (query.ProviderId is not null)
        {
            var providerId = query.ProviderId.Value.Value;
            where.Add("ProviderId = @providerId");
            command.Parameters.AddWithValue("@providerId", providerId);
        }

        if (query.Status is not null)
        {
            var status = StatusValue(query.Status.Value);
            where.Add("Status = @status");
            command.Parameters.AddWithValue("@status", status);
        }

        if (query.FromUtc is not null)
        {
            var fromUtc = query.FromUtc.Value.UtcDateTime;
            where.Add("CompletedAtUtc >= @fromUtc");
            command.Parameters.AddWithValue("@fromUtc", fromUtc);
        }

        if (query.ToUtc is not null)
        {
            var toUtc = query.ToUtc.Value.UtcDateTime;
            where.Add("CompletedAtUtc <= @toUtc");
            command.Parameters.AddWithValue("@toUtc", toUtc);
        }

        if (query.Cursor is not null && HistoryCursor.TryDecode(query.Cursor, out var cursor))
        {
            where.Add("(CompletedAtUtc < @cursorCompletedAtUtc OR (CompletedAtUtc = @cursorCompletedAtUtc AND Id < @cursorId))");
            command.Parameters.AddWithValue("@cursorCompletedAtUtc", cursor.CompletedAtUtc);
            command.Parameters.AddWithValue("@cursorId", cursor.Id);
        }

        command.CommandText = $"SELECT {ResultColumns} FROM SpeedTestResults{(where.Count == 0 ? string.Empty : " WHERE " + string.Join(" AND ", where))} ORDER BY CompletedAtUtc DESC, Id DESC LIMIT @limit;";
        command.Parameters.AddWithValue("@limit", query.Limit + 1);
        var entities = new List<SpeedTestResultEntity>(query.Limit + 1);
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                entities.Add(ReadResult(reader));
            }
        }

        var hasMore = entities.Count > query.Limit;
        if (hasMore)
        {
            entities.RemoveAt(entities.Count - 1);
        }

        var items = entities.Select(MapHistory).ToArray();
        var nextCursor = hasMore && entities.Count > 0
            ? new HistoryCursor(AsUtc(entities[^1].CompletedAtUtc), entities[^1].Id).Encode()
            : null;
        return new HistoryPage(items, nextCursor);
    }

    public async Task<SpeedTestHistoryRecord?> GetAsync(long id, CancellationToken cancellationToken)
    {
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        var entity = await GetResultAsync(connection, "Id = @id", id, cancellationToken);
        return entity is null ? null : MapHistory(entity);
    }

    public async Task<SpeedTestJob?> GetTerminalJobAsync(Guid jobId, CancellationToken cancellationToken)
    {
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        var result = await GetResultAsync(connection, "JobId = @id", jobId, cancellationToken);
        if (result is null)
        {
            return null;
        }

        await using var command = connectionFactory.CreateCommand(connection, $"SELECT {JobColumns} FROM SpeedTestJobs WHERE Id = @id;");
        command.Parameters.AddWithValue("@id", jobId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? MapJob(ReadJob(reader), result) : null;
    }

    public async Task<HistoryDeleteResult> DeleteAsync(long id, CancellationToken cancellationToken)
    {
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using var find = connectionFactory.CreateCommand(connection, "SELECT JobId FROM SpeedTestResults WHERE Id = @id;", (SqliteTransaction)transaction);
        find.Parameters.AddWithValue("@id", id);
        var value = await find.ExecuteScalarAsync(cancellationToken);
        Guid? result = value is null ? null : Guid.Parse((string)value);
        if (result is null)
        {
            return new HistoryDeleteResult(false, null);
        }

        var jobId = result.Value;
        await using var delete = connectionFactory.CreateCommand(connection, "DELETE FROM SpeedTestJobs WHERE Id = @id;", (SqliteTransaction)transaction);
        delete.Parameters.AddWithValue("@id", jobId);
        await delete.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new HistoryDeleteResult(true, jobId);
    }

    public async Task<HistoryDeleteAllResult> DeleteAllAsync(CancellationToken cancellationToken)
    {
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var jobIds = new List<Guid>();
        await using (var select = connectionFactory.CreateCommand(connection,
            "SELECT JobId FROM SpeedTestResults;", (SqliteTransaction)transaction))
        await using (var reader = await select.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken)) jobIds.Add(reader.GetGuid(0));
        }

        if (jobIds.Count == 0)
        {
            return new HistoryDeleteAllResult([]);
        }

        await using var delete = connectionFactory.CreateCommand(connection,
            "DELETE FROM SpeedTestJobs WHERE EXISTS (SELECT 1 FROM SpeedTestResults WHERE SpeedTestResults.JobId = SpeedTestJobs.Id);",
            (SqliteTransaction)transaction);
        await delete.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new HistoryDeleteAllResult(jobIds.ToArray());
    }

    public async Task<int> ReconcileInterruptedJobsAsync(CancellationToken cancellationToken)
    {
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var interrupted = new List<SpeedTestJobEntity>();
        await using (var select = connectionFactory.CreateCommand(connection,
            $"SELECT {JobColumns} FROM SpeedTestJobs WHERE Status NOT IN ('completed', 'failed', 'cancelled');",
            (SqliteTransaction)transaction))
        await using (var reader = await select.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken)) interrupted.Add(ReadJob(reader));
        }
        if (interrupted.Count == 0)
        {
            return 0;
        }

        var completedAt = timeProvider.GetUtcNow().UtcDateTime;
        foreach (var job in interrupted)
        {
            job.Status = StatusValue(SpeedTestJobStatus.Failed);
            job.Stage = "Failed";
            job.Version = checked(job.Version + 1);
            job.CompletedAtUtc = completedAt;
            job.FailureCode = SpeedTestFailureCodes.ApplicationRestarted;
            job.FailureMessage = "The application restarted before this test completed.";
            await using var update = connectionFactory.CreateCommand(connection, """
                UPDATE SpeedTestJobs SET Status = @status, Stage = @stage, Version = @version,
                    CompletedAtUtc = @completedAtUtc, FailureCode = @failureCode, FailureMessage = @failureMessage
                WHERE Id = @id;
                """, (SqliteTransaction)transaction);
            BindJob(update, job);
            await update.ExecuteNonQueryAsync(cancellationToken);
            InsertResult(connection, (SqliteTransaction)transaction, MapInterruptedResult(job));
        }

        await transaction.CommitAsync(cancellationToken);
        logger.LogWarning("Reconciled {Count} interrupted speed-test jobs after application restart.", interrupted.Count);
        return interrupted.Count;
    }

    private static void BindJob(SqliteCommand command, SpeedTestJobEntity entity)
    {
        Add(command, "@id", entity.Id);
        Add(command, "@providerId", entity.ProviderId);
        Add(command, "@status", entity.Status);
        Add(command, "@version", entity.Version);
        Add(command, "@stage", entity.Stage);
        Add(command, "@createdAtUtc", entity.CreatedAtUtc);
        Add(command, "@startedAtUtc", entity.StartedAtUtc);
        Add(command, "@completedAtUtc", entity.CompletedAtUtc);
        Add(command, "@requestedServerId", entity.RequestedServerId);
        Add(command, "@failureCode", entity.FailureCode);
        Add(command, "@failureMessage", entity.FailureMessage);
    }

    private void InsertResult(SqliteConnection connection, SqliteTransaction transaction, SpeedTestResultEntity entity)
    {
        using var command = connectionFactory.CreateCommand(connection, """
            INSERT INTO SpeedTestResults (
                JobId, ProviderId, Status, QueuedAtUtc, StartedAtUtc, CompletedAtUtc, RequestedServerId,
                ServerId, ServerName, ServerLocation, ServerCountry, DownloadMbps, UploadMbps, LatencyMs,
                JitterMs, PacketLossPercent, ResultUrl, FailureCode, FailureMessage, ProviderMetadataJson,
                NetworkState, NetworkCheckedAtUtc, NetworkIsStale, NetworkWarning,
                IPv4Address, IPv4Asn, IPv4AsName, IPv4Isp, IPv4CountryCode, IPv4CountryName, IPv4Region,
                IPv4City, IPv4AddressSource, IPv4MetadataSource,
                IPv6Address, IPv6Asn, IPv6AsName, IPv6Isp, IPv6CountryCode, IPv6CountryName, IPv6Region,
                IPv6City, IPv6AddressSource, IPv6MetadataSource)
            VALUES (
                @JobId, @ProviderId, @Status, @QueuedAtUtc, @StartedAtUtc, @CompletedAtUtc, @RequestedServerId,
                @ServerId, @ServerName, @ServerLocation, @ServerCountry, @DownloadMbps, @UploadMbps, @LatencyMs,
                @JitterMs, @PacketLossPercent, @ResultUrl, @FailureCode, @FailureMessage, @ProviderMetadataJson,
                @NetworkState, @NetworkCheckedAtUtc, @NetworkIsStale, @NetworkWarning,
                @IPv4Address, @IPv4Asn, @IPv4AsName, @IPv4Isp, @IPv4CountryCode, @IPv4CountryName, @IPv4Region,
                @IPv4City, @IPv4AddressSource, @IPv4MetadataSource,
                @IPv6Address, @IPv6Asn, @IPv6AsName, @IPv6Isp, @IPv6CountryCode, @IPv6CountryName, @IPv6Region,
                @IPv6City, @IPv6AddressSource, @IPv6MetadataSource);
            """, transaction);
        Add(command, "@JobId", entity.JobId); Add(command, "@ProviderId", entity.ProviderId);
        Add(command, "@Status", entity.Status); Add(command, "@QueuedAtUtc", entity.QueuedAtUtc);
        Add(command, "@StartedAtUtc", entity.StartedAtUtc); Add(command, "@CompletedAtUtc", entity.CompletedAtUtc);
        Add(command, "@RequestedServerId", entity.RequestedServerId); Add(command, "@ServerId", entity.ServerId);
        Add(command, "@ServerName", entity.ServerName); Add(command, "@ServerLocation", entity.ServerLocation);
        Add(command, "@ServerCountry", entity.ServerCountry); Add(command, "@DownloadMbps", entity.DownloadMbps);
        Add(command, "@UploadMbps", entity.UploadMbps); Add(command, "@LatencyMs", entity.LatencyMs);
        Add(command, "@JitterMs", entity.JitterMs); Add(command, "@PacketLossPercent", entity.PacketLossPercent);
        Add(command, "@ResultUrl", entity.ResultUrl); Add(command, "@FailureCode", entity.FailureCode);
        Add(command, "@FailureMessage", entity.FailureMessage); Add(command, "@ProviderMetadataJson", entity.ProviderMetadataJson);
        Add(command, "@NetworkState", entity.NetworkState); Add(command, "@NetworkCheckedAtUtc", entity.NetworkCheckedAtUtc);
        Add(command, "@NetworkIsStale", entity.NetworkIsStale); Add(command, "@NetworkWarning", entity.NetworkWarning);
        Add(command, "@IPv4Address", entity.IPv4Address); Add(command, "@IPv4Asn", entity.IPv4Asn);
        Add(command, "@IPv4AsName", entity.IPv4AsName); Add(command, "@IPv4Isp", entity.IPv4Isp);
        Add(command, "@IPv4CountryCode", entity.IPv4CountryCode); Add(command, "@IPv4CountryName", entity.IPv4CountryName);
        Add(command, "@IPv4Region", entity.IPv4Region); Add(command, "@IPv4City", entity.IPv4City);
        Add(command, "@IPv4AddressSource", entity.IPv4AddressSource); Add(command, "@IPv4MetadataSource", entity.IPv4MetadataSource);
        Add(command, "@IPv6Address", entity.IPv6Address); Add(command, "@IPv6Asn", entity.IPv6Asn);
        Add(command, "@IPv6AsName", entity.IPv6AsName); Add(command, "@IPv6Isp", entity.IPv6Isp);
        Add(command, "@IPv6CountryCode", entity.IPv6CountryCode); Add(command, "@IPv6CountryName", entity.IPv6CountryName);
        Add(command, "@IPv6Region", entity.IPv6Region); Add(command, "@IPv6City", entity.IPv6City);
        Add(command, "@IPv6AddressSource", entity.IPv6AddressSource); Add(command, "@IPv6MetadataSource", entity.IPv6MetadataSource);
        command.ExecuteNonQuery();
    }

    private async Task<SpeedTestResultEntity?> GetResultAsync(
        SqliteConnection connection, string predicate, object value, CancellationToken cancellationToken)
    {
        await using var command = connectionFactory.CreateCommand(connection,
            $"SELECT {ResultColumns} FROM SpeedTestResults WHERE {predicate};");
        command.Parameters.AddWithValue("@id", value);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadResult(reader) : null;
    }

    private static SpeedTestJobEntity ReadJob(SqliteDataReader reader) => new()
    {
        Id = reader.GetGuid(0),
        ProviderId = reader.GetString(1),
        Status = reader.GetString(2),
        Version = reader.GetInt64(3),
        Stage = reader.GetString(4),
        CreatedAtUtc = reader.GetDateTime(5),
        StartedAtUtc = NullableDateTime(reader, 6),
        CompletedAtUtc = NullableDateTime(reader, 7),
        RequestedServerId = NullableString(reader, 8),
        FailureCode = NullableString(reader, 9),
        FailureMessage = NullableString(reader, 10)
    };

    private static SpeedTestResultEntity ReadResult(SqliteDataReader reader) => new()
    {
        Id = reader.GetInt64(0),
        JobId = reader.GetGuid(1),
        ProviderId = reader.GetString(2),
        Status = reader.GetString(3),
        QueuedAtUtc = reader.GetDateTime(4),
        StartedAtUtc = NullableDateTime(reader, 5),
        CompletedAtUtc = reader.GetDateTime(6),
        RequestedServerId = NullableString(reader, 7),
        ServerId = NullableString(reader, 8),
        ServerName = NullableString(reader, 9),
        ServerLocation = NullableString(reader, 10),
        ServerCountry = NullableString(reader, 11),
        DownloadMbps = NullableDouble(reader, 12),
        UploadMbps = NullableDouble(reader, 13),
        LatencyMs = NullableDouble(reader, 14),
        JitterMs = NullableDouble(reader, 15),
        PacketLossPercent = NullableDouble(reader, 16),
        ResultUrl = NullableString(reader, 17),
        FailureCode = NullableString(reader, 18),
        FailureMessage = NullableString(reader, 19),
        ProviderMetadataJson = NullableString(reader, 20),
        NetworkState = NullableString(reader, 21),
        NetworkCheckedAtUtc = NullableDateTime(reader, 22),
        NetworkIsStale = reader.GetBoolean(23),
        NetworkWarning = NullableString(reader, 24),
        IPv4Address = NullableString(reader, 25),
        IPv4Asn = NullableString(reader, 26),
        IPv4AsName = NullableString(reader, 27),
        IPv4Isp = NullableString(reader, 28),
        IPv4CountryCode = NullableString(reader, 29),
        IPv4CountryName = NullableString(reader, 30),
        IPv4Region = NullableString(reader, 31),
        IPv4City = NullableString(reader, 32),
        IPv4AddressSource = NullableString(reader, 33),
        IPv4MetadataSource = NullableString(reader, 34),
        IPv6Address = NullableString(reader, 35),
        IPv6Asn = NullableString(reader, 36),
        IPv6AsName = NullableString(reader, 37),
        IPv6Isp = NullableString(reader, 38),
        IPv6CountryCode = NullableString(reader, 39),
        IPv6CountryName = NullableString(reader, 40),
        IPv6Region = NullableString(reader, 41),
        IPv6City = NullableString(reader, 42),
        IPv6AddressSource = NullableString(reader, 43),
        IPv6MetadataSource = NullableString(reader, 44),
        Job = null!
    };

    private static void Add(SqliteCommand command, string name, object? value) =>
        command.Parameters.AddWithValue(name, value ?? DBNull.Value);

    private static string? NullableString(SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);

    private static DateTime? NullableDateTime(SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetDateTime(ordinal);

    private static double? NullableDouble(SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetDouble(ordinal);

    private static SpeedTestJobEntity MapJob(SpeedTestJob job)
    {
        var entity = new SpeedTestJobEntity
        {
            Id = job.Id,
            ProviderId = job.Request.ProviderId.Value,
            Status = StatusValue(job.Status),
            Version = job.Version,
            Stage = job.Stage,
            CreatedAtUtc = job.CreatedAtUtc.UtcDateTime,
            RequestedServerId = job.Request.ServerId
        };
        ApplyJob(entity, job);
        return entity;
    }

    private static void ApplyJob(SpeedTestJobEntity entity, SpeedTestJob job)
    {
        entity.Status = StatusValue(job.Status);
        entity.Version = job.Version;
        entity.Stage = job.Stage;
        entity.StartedAtUtc = job.StartedAtUtc?.UtcDateTime;
        entity.CompletedAtUtc = job.CompletedAtUtc?.UtcDateTime;
        entity.FailureCode = job.Failure?.Code;
        entity.FailureMessage = job.Failure?.Message;
    }

    private static SpeedTestResultEntity MapResult(SpeedTestJob job, SpeedTestJobEntity entity)
    {
        var metadata = job.Result?.ProviderMetadataJson;
        if (metadata is not null && Encoding.UTF8.GetByteCount(metadata) > StorageOptions.MaximumProviderMetadataBytes)
        {
            throw new InvalidOperationException("Provider metadata exceeds the configured persistence limit.");
        }

        var result = new SpeedTestResultEntity
        {
            JobId = job.Id,
            Job = entity,
            ProviderId = job.Request.ProviderId.Value,
            Status = StatusValue(job.Status),
            QueuedAtUtc = job.CreatedAtUtc.UtcDateTime,
            StartedAtUtc = job.StartedAtUtc?.UtcDateTime,
            CompletedAtUtc = job.CompletedAtUtc?.UtcDateTime
                ?? throw new InvalidOperationException("A terminal speed-test job requires a completion timestamp."),
            RequestedServerId = job.Request.ServerId,
            ServerId = job.Result?.ServerId,
            ServerName = job.Result?.ServerName,
            ServerLocation = job.Result?.ServerLocation,
            DownloadMbps = ToDouble(job.Result?.DownloadMbps),
            UploadMbps = ToDouble(job.Result?.UploadMbps),
            LatencyMs = ToDouble(job.Result?.LatencyMilliseconds),
            JitterMs = ToDouble(job.Result?.JitterMilliseconds),
            PacketLossPercent = ToDouble(job.Result?.PacketLossPercent),
            ResultUrl = job.Result?.ResultUrl,
            FailureCode = job.Failure?.Code,
            FailureMessage = job.Failure?.Message,
            ProviderMetadataJson = metadata
        };
        ApplyNetwork(result, job.EgressIdentity ?? job.Result?.EgressIdentity);
        return result;
    }

    private static SpeedTestResultEntity MapInterruptedResult(SpeedTestJobEntity job) => new()
    {
        JobId = job.Id,
        Job = job,
        ProviderId = job.ProviderId,
        Status = job.Status,
        QueuedAtUtc = job.CreatedAtUtc,
        StartedAtUtc = job.StartedAtUtc,
        CompletedAtUtc = job.CompletedAtUtc!.Value,
        RequestedServerId = job.RequestedServerId,
        FailureCode = job.FailureCode,
        FailureMessage = job.FailureMessage
    };

    private static void ApplyNetwork(SpeedTestResultEntity result, NetworkIdentity? identity)
    {
        if (identity is null)
        {
            return;
        }

        result.NetworkState = identity.State.ToString().ToLowerInvariant();
        result.NetworkCheckedAtUtc = identity.CheckedAtUtc.UtcDateTime;
        result.NetworkIsStale = identity.IsStale;
        result.NetworkWarning = identity.Warning?.ToString();
        ApplyAddress(result, identity.IPv4, isIpv4: true);
        ApplyAddress(result, identity.IPv6, isIpv4: false);
    }

    private static void ApplyAddress(SpeedTestResultEntity result, NetworkAddressIdentity? address, bool isIpv4)
    {
        if (address is null)
        {
            return;
        }

        if (isIpv4)
        {
            result.IPv4Address = address.Address;
            result.IPv4Asn = address.Asn;
            result.IPv4AsName = address.AsName;
            result.IPv4Isp = address.Isp;
            result.IPv4CountryCode = address.CountryCode;
            result.IPv4CountryName = address.CountryName;
            result.IPv4Region = address.Region;
            result.IPv4City = address.City;
            result.IPv4AddressSource = address.AddressSource;
            result.IPv4MetadataSource = address.MetadataSource;
        }
        else
        {
            result.IPv6Address = address.Address;
            result.IPv6Asn = address.Asn;
            result.IPv6AsName = address.AsName;
            result.IPv6Isp = address.Isp;
            result.IPv6CountryCode = address.CountryCode;
            result.IPv6CountryName = address.CountryName;
            result.IPv6Region = address.Region;
            result.IPv6City = address.City;
            result.IPv6AddressSource = address.AddressSource;
            result.IPv6MetadataSource = address.MetadataSource;
        }
    }

    private static SpeedTestHistoryRecord MapHistory(SpeedTestResultEntity entity)
    {
        var providerId = ParseProvider(entity.ProviderId);
        var status = ParseStatus(entity.Status);
        return new SpeedTestHistoryRecord(
            entity.Id,
            entity.JobId,
            providerId,
            status,
            AsUtcOffset(entity.QueuedAtUtc),
            AsUtcOffset(entity.StartedAtUtc),
            AsUtcOffset(entity.CompletedAtUtc),
            entity.RequestedServerId,
            entity.ServerId,
            entity.ServerName,
            entity.ServerLocation,
            entity.ServerCountry,
            ToDecimal(entity.DownloadMbps),
            ToDecimal(entity.UploadMbps),
            ToDecimal(entity.LatencyMs),
            ToDecimal(entity.JitterMs),
            ToDecimal(entity.PacketLossPercent),
            entity.ResultUrl,
            entity.FailureCode is null ? null : new SpeedTestFailure(entity.FailureCode, entity.FailureMessage ?? "The test did not complete."),
            MapNetwork(entity),
            entity.ProviderMetadataJson);
    }

    private static SpeedTestJob MapJob(SpeedTestJobEntity job, SpeedTestResultEntity result)
    {
        var providerId = ParseProvider(job.ProviderId);
        var history = MapHistory(result);
        var speedResult = history.Status == SpeedTestJobStatus.Completed
            ? new SpeedTestResult(
                providerId,
                history.ServerId,
                history.ServerName,
                history.ServerLocation,
                history.DownloadMbps,
                history.UploadMbps,
                history.LatencyMilliseconds,
                history.JitterMilliseconds,
                history.PacketLossPercent,
                history.ResultUrl,
                history.JobId,
                history.EgressIdentity,
                history.ProviderMetadataJson)
            : null;
        return new SpeedTestJob(
            job.Id,
            new SpeedTestRequest(providerId, job.RequestedServerId),
            ParseStatus(job.Status),
            job.Stage,
            job.Version,
            AsUtcOffset(job.CreatedAtUtc),
            AsUtcOffset(job.StartedAtUtc),
            AsUtcOffset(job.CompletedAtUtc),
            history.EgressIdentity,
            speedResult,
            history.Failure);
    }

    private static NetworkIdentity? MapNetwork(SpeedTestResultEntity entity)
    {
        if (entity.NetworkState is null || entity.NetworkCheckedAtUtc is null)
        {
            return null;
        }

        return new NetworkIdentity(
            MapAddress(entity, isIpv4: true),
            MapAddress(entity, isIpv4: false),
            AsUtcOffset(entity.NetworkCheckedAtUtc.Value),
            Enum.Parse<NetworkIdentityState>(entity.NetworkState, ignoreCase: true),
            entity.NetworkIsStale,
            Enum.TryParse<NetworkIdentityWarning>(entity.NetworkWarning, out var warning) ? warning : null);
    }

    private static NetworkAddressIdentity? MapAddress(SpeedTestResultEntity entity, bool isIpv4)
    {
        var address = isIpv4 ? entity.IPv4Address : entity.IPv6Address;
        if (address is null)
        {
            return null;
        }

        return new NetworkAddressIdentity(
            address,
            isIpv4 ? NetworkAddressFamily.IPv4 : NetworkAddressFamily.IPv6,
            isIpv4 ? entity.IPv4Asn : entity.IPv6Asn,
            isIpv4 ? entity.IPv4AsName : entity.IPv6AsName,
            isIpv4 ? entity.IPv4Isp : entity.IPv6Isp,
            isIpv4 ? entity.IPv4CountryCode : entity.IPv6CountryCode,
            isIpv4 ? entity.IPv4CountryName : entity.IPv6CountryName,
            isIpv4 ? entity.IPv4Region : entity.IPv6Region,
            isIpv4 ? entity.IPv4City : entity.IPv6City,
            (isIpv4 ? entity.IPv4AddressSource : entity.IPv6AddressSource) ?? "unknown",
            isIpv4 ? entity.IPv4MetadataSource : entity.IPv6MetadataSource);
    }

    private static string StatusValue(SpeedTestJobStatus status) => status == SpeedTestJobStatus.ProcessingResult
        ? "processingResult"
        : status.ToString().ToLowerInvariant();

    private static SpeedTestJobStatus ParseStatus(string status) => status == "processingResult"
        ? SpeedTestJobStatus.ProcessingResult
        : Enum.Parse<SpeedTestJobStatus>(status, ignoreCase: true);

    private static ProviderId ParseProvider(string value) => ProviderId.TryParse(value, out var providerId)
        ? providerId
        : throw new InvalidOperationException("Persisted provider ID is invalid.");

    private static double? ToDouble(decimal? value) => value is null ? null : checked((double)value.Value);
    private static decimal? ToDecimal(double? value) => value is null ? null : checked((decimal)value.Value);
    private static DateTime AsUtc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);
    private static DateTimeOffset AsUtcOffset(DateTime value) => new(AsUtc(value));
    private static DateTimeOffset? AsUtcOffset(DateTime? value) => value is null ? null : AsUtcOffset(value.Value);

    private SpeedTestPersistenceException Wrap(string message, Exception exception)
    {
        logger.LogError(exception, "{PersistenceFailure}", message);
        return exception as SpeedTestPersistenceException ?? new SpeedTestPersistenceException(message, exception);
    }
}
