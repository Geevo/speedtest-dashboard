using SpeedtestDashboard.Core.Providers;

namespace SpeedtestDashboard.Core.Tests;

public sealed record SpeedTestRequest(
    ProviderId ProviderId,
    string? ServerId);

public sealed record SpeedTestExecution(
    Guid JobId,
    SpeedTestRequest Request,
    Network.NetworkIdentity EgressIdentity);
