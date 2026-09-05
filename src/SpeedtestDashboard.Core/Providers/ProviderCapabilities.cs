namespace SpeedtestDashboard.Core.Providers;

[Flags]
public enum ProviderCapabilities
{
    None = 0,
    ServerDiscovery = 1 << 0,
    ServerSelection = 1 << 1,
    Download = 1 << 2,
    Upload = 1 << 3,
    Latency = 1 << 4,
    Jitter = 1 << 5,
    PacketLoss = 1 << 6,
    ResultUrl = 1 << 7,
    IPv6 = 1 << 8
}

