using System.Text.Json.Serialization;

namespace SpeedtestDashboard.Infrastructure.Serialization;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(FastComMetadata))]
[JsonSerializable(typeof(LibreSpeedMetadata))]
[JsonSerializable(typeof(MLabMetadata))]
[JsonSerializable(typeof(OoklaMetadata))]
internal sealed partial class InfrastructureJsonSerializerContext : JsonSerializerContext;

internal sealed record FastComMetadata(string LatencyMethod, string ServerSelection);

internal sealed record LibreSpeedMetadata(
    DateTimeOffset? Timestamp,
    ulong? BytesSent,
    ulong? BytesReceived,
    string? LibreSpeedReportedClientIp,
    string? LibreSpeedReportedClientOrganization,
    string ServerUrl,
    bool HttpPing);

internal sealed record MLabMetadata(
    string MeasurementProtocol,
    string ServerSelection,
    string? ServerIp,
    string? ClientIp,
    string? DownloadMeasurementId,
    string? UploadMeasurementId,
    string? LatencyMethod,
    decimal? DownloadRetransmissionPercent,
    string DataPublication);

internal sealed record OoklaLatencyMetadata(decimal? Iqm, decimal? Low, decimal? High, decimal? Jitter);

internal sealed record OoklaMetadata(
    DateTimeOffset? Timestamp,
    string? Isp,
    decimal? PingLow,
    decimal? PingHigh,
    decimal? DownloadBytes,
    decimal? DownloadElapsedMilliseconds,
    OoklaLatencyMetadata? DownloadLatency,
    decimal? UploadBytes,
    decimal? UploadElapsedMilliseconds,
    OoklaLatencyMetadata? UploadLatency,
    string? InternalIp,
    string? OoklaReportedExternalIp,
    string? ServerHost,
    decimal? ServerPort,
    string? ServerIp,
    string? ResultId,
    bool? ResultPersisted);
