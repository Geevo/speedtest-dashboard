namespace SpeedtestDashboard.Infrastructure.Persistence.Entities;

public sealed class SpeedTestResultEntity
{
    public long Id { get; set; }
    public Guid JobId { get; set; }
    public required string ProviderId { get; set; }
    public required string Status { get; set; }
    public DateTime QueuedAtUtc { get; set; }
    public DateTime? StartedAtUtc { get; set; }
    public DateTime CompletedAtUtc { get; set; }
    public string? RequestedServerId { get; set; }
    public Guid? RequestedIperfServerId { get; set; }
    public string? Direction { get; set; }
    public string? ServerId { get; set; }
    public string? ServerName { get; set; }
    public string? ServerLocation { get; set; }
    public string? ServerCountry { get; set; }
    public double? DownloadMbps { get; set; }
    public double? UploadMbps { get; set; }
    public double? LatencyMs { get; set; }
    public double? JitterMs { get; set; }
    public double? PacketLossPercent { get; set; }
    public string? ResultUrl { get; set; }
    public string? FailureCode { get; set; }
    public string? FailureMessage { get; set; }
    public string? ProviderMetadataJson { get; set; }

    public string? NetworkState { get; set; }
    public DateTime? NetworkCheckedAtUtc { get; set; }
    public bool NetworkIsStale { get; set; }
    public string? NetworkWarning { get; set; }

    public string? IPv4Address { get; set; }
    public string? IPv4Asn { get; set; }
    public string? IPv4AsName { get; set; }
    public string? IPv4Isp { get; set; }
    public string? IPv4CountryCode { get; set; }
    public string? IPv4CountryName { get; set; }
    public string? IPv4Region { get; set; }
    public string? IPv4City { get; set; }
    public string? IPv4AddressSource { get; set; }
    public string? IPv4MetadataSource { get; set; }

    public string? IPv6Address { get; set; }
    public string? IPv6Asn { get; set; }
    public string? IPv6AsName { get; set; }
    public string? IPv6Isp { get; set; }
    public string? IPv6CountryCode { get; set; }
    public string? IPv6CountryName { get; set; }
    public string? IPv6Region { get; set; }
    public string? IPv6City { get; set; }
    public string? IPv6AddressSource { get; set; }
    public string? IPv6MetadataSource { get; set; }

    public required SpeedTestJobEntity Job { get; set; }
}
