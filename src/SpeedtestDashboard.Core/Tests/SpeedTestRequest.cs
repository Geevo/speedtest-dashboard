using SpeedtestDashboard.Core.Providers;

namespace SpeedtestDashboard.Core.Tests;

public enum SpeedTestDirection
{
    Upload,
    Download
}

public sealed record SpeedTestRequest(
    ProviderId ProviderId,
    string? ServerId,
    Guid? IperfServerId,
    SpeedTestDirection? Direction);

public sealed record SpeedTestExecution(
    Guid JobId,
    SpeedTestRequest Request,
    Network.NetworkIdentity EgressIdentity);

