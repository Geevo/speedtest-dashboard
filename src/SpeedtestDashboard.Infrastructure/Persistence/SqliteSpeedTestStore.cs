using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SpeedtestDashboard.Core.History;
using SpeedtestDashboard.Core.Network;
using SpeedtestDashboard.Core.Providers;
using SpeedtestDashboard.Core.Tests;
using SpeedtestDashboard.Infrastructure.Persistence.Entities;

namespace SpeedtestDashboard.Infrastructure.Persistence;

public sealed class SqliteSpeedTestStore(
    IDbContextFactory<DashboardDbContext> contextFactory,
    TimeProvider timeProvider,
    ILogger<SqliteSpeedTestStore> logger) : ISpeedTestPersistenceWriter, ISpeedTestHistoryStore
{
    public void PersistCreated(SpeedTestJob job)
    {
        try
        {
            using var context = contextFactory.CreateDbContext();
            context.SpeedTestJobs.Add(MapJob(job));
            context.SaveChanges();
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
            using var context = contextFactory.CreateDbContext();
            using var transaction = job.IsTerminal ? context.Database.BeginTransaction() : null;
            var entity = context.SpeedTestJobs.SingleOrDefault(candidate => candidate.Id == job.Id)
                ?? throw new InvalidOperationException("The persisted speed-test job was not found.");

            ApplyJob(entity, job);
            if (job.IsTerminal)
            {
                if (context.SpeedTestResults.Any(result => result.JobId == job.Id))
                {
                    throw new InvalidOperationException("A terminal result already exists for this speed-test job.");
                }

                context.SpeedTestResults.Add(MapResult(job, entity));
            }

            context.SaveChanges();
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
            using var context = contextFactory.CreateDbContext();
            var job = context.SpeedTestJobs.SingleOrDefault(candidate => candidate.Id == jobId);
            if (job is not null && job.Status == StatusValue(SpeedTestJobStatus.Queued))
            {
                context.SpeedTestJobs.Remove(job);
                context.SaveChanges();
            }
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

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var records = context.SpeedTestResults.AsNoTracking();

        if (query.ProviderId is not null)
        {
            var providerId = query.ProviderId.Value.Value;
            records = records.Where(record => record.ProviderId == providerId);
        }

        if (query.Status is not null)
        {
            var status = StatusValue(query.Status.Value);
            records = records.Where(record => record.Status == status);
        }

        if (query.FromUtc is not null)
        {
            var fromUtc = query.FromUtc.Value.UtcDateTime;
            records = records.Where(record => record.CompletedAtUtc >= fromUtc);
        }

        if (query.ToUtc is not null)
        {
            var toUtc = query.ToUtc.Value.UtcDateTime;
            records = records.Where(record => record.CompletedAtUtc <= toUtc);
        }

        if (query.Cursor is not null && HistoryCursor.TryDecode(query.Cursor, out var cursor))
        {
            records = records.Where(record =>
                record.CompletedAtUtc < cursor.CompletedAtUtc ||
                (record.CompletedAtUtc == cursor.CompletedAtUtc && record.Id < cursor.Id));
        }

        var entities = await records
            .OrderByDescending(record => record.CompletedAtUtc)
            .ThenByDescending(record => record.Id)
            .Take(query.Limit + 1)
            .ToListAsync(cancellationToken);

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
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await context.SpeedTestResults.AsNoTracking()
            .SingleOrDefaultAsync(record => record.Id == id, cancellationToken);
        return entity is null ? null : MapHistory(entity);
    }

    public async Task<SpeedTestJob?> GetTerminalJobAsync(Guid jobId, CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await context.SpeedTestJobs.AsNoTracking()
            .Include(job => job.Result)
            .SingleOrDefaultAsync(job => job.Id == jobId, cancellationToken);
        return entity?.Result is null ? null : MapJob(entity, entity.Result);
    }

    public async Task<HistoryDeleteResult> DeleteAsync(long id, CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var result = await context.SpeedTestResults
            .SingleOrDefaultAsync(record => record.Id == id, cancellationToken);
        if (result is null)
        {
            return new HistoryDeleteResult(false, null);
        }

        var jobId = result.JobId;
        var job = await context.SpeedTestJobs.SingleAsync(candidate => candidate.Id == jobId, cancellationToken);
        context.SpeedTestJobs.Remove(job);
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new HistoryDeleteResult(true, jobId);
    }

    public async Task<HistoryDeleteAllResult> DeleteAllAsync(CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var terminalJobs = await context.SpeedTestJobs
            .Where(job => job.Result != null)
            .ToListAsync(cancellationToken);
        if (terminalJobs.Count == 0)
        {
            return new HistoryDeleteAllResult([]);
        }

        var jobIds = terminalJobs.Select(job => job.Id).ToArray();
        context.SpeedTestJobs.RemoveRange(terminalJobs);
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new HistoryDeleteAllResult(jobIds);
    }

    public async Task<int> ReconcileInterruptedJobsAsync(CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var terminalStatuses = new[]
        {
            StatusValue(SpeedTestJobStatus.Completed),
            StatusValue(SpeedTestJobStatus.Failed),
            StatusValue(SpeedTestJobStatus.Cancelled)
        };
        var interrupted = await context.SpeedTestJobs
            .Where(job => !terminalStatuses.Contains(job.Status))
            .ToListAsync(cancellationToken);
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
            context.SpeedTestResults.Add(MapInterruptedResult(job));
        }

        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        logger.LogWarning("Reconciled {Count} interrupted speed-test jobs after application restart.", interrupted.Count);
        return interrupted.Count;
    }

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
