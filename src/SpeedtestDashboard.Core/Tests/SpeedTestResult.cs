using SpeedtestDashboard.Core.Network;
using SpeedtestDashboard.Core.Providers;

namespace SpeedtestDashboard.Core.Tests;

public sealed record SpeedTestResult(
    ProviderId ProviderId,
    string? ServerId,
    string? ServerName,
    string? ServerLocation,
    decimal? DownloadMbps,
    decimal? UploadMbps,
    decimal? LatencyMilliseconds,
    decimal? JitterMilliseconds,
    decimal? PacketLossPercent,
    string? ResultUrl,
    Guid? JobId = null,
    NetworkIdentity? EgressIdentity = null,
    string? ProviderMetadataJson = null);
