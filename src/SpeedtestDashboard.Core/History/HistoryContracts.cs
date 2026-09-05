using SpeedtestDashboard.Core.Network;
using SpeedtestDashboard.Core.Providers;
using SpeedtestDashboard.Core.Tests;

namespace SpeedtestDashboard.Core.History;

public sealed record HistoryQuery(
    ProviderId? ProviderId,
    SpeedTestJobStatus? Status,
    DateTimeOffset? FromUtc,
    DateTimeOffset? ToUtc,
    int Limit,
    string? Cursor);

public sealed record SpeedTestHistoryRecord(
    long Id,
    Guid JobId,
    ProviderId ProviderId,
    SpeedTestJobStatus Status,
    DateTimeOffset QueuedAtUtc,
    DateTimeOffset? StartedAtUtc,
    DateTimeOffset CompletedAtUtc,
    string? RequestedServerId,
    string? ServerId,
    string? ServerName,
    string? ServerLocation,
    string? ServerCountry,
    decimal? DownloadMbps,
    decimal? UploadMbps,
    decimal? LatencyMilliseconds,
    decimal? JitterMilliseconds,
    decimal? PacketLossPercent,
    string? ResultUrl,
    SpeedTestFailure? Failure,
    NetworkIdentity? EgressIdentity,
    string? ProviderMetadataJson);

public sealed record HistoryPage(
    IReadOnlyList<SpeedTestHistoryRecord> Items,
    string? NextCursor);

public sealed record HistoryDeleteResult(bool Deleted, Guid? JobId);

public interface ISpeedTestHistoryStore
{
    Task<HistoryPage> ListAsync(HistoryQuery query, CancellationToken cancellationToken);

    Task<SpeedTestHistoryRecord?> GetAsync(long id, CancellationToken cancellationToken);

    Task<SpeedTestJob?> GetTerminalJobAsync(Guid jobId, CancellationToken cancellationToken);

    Task<HistoryDeleteResult> DeleteAsync(long id, CancellationToken cancellationToken);
}
