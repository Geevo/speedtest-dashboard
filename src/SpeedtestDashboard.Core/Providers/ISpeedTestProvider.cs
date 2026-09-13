using SpeedtestDashboard.Core.Tests;

namespace SpeedtestDashboard.Core.Providers;

public interface ISpeedTestProvider
{
    ProviderDescriptor Descriptor { get; }

    Task<ProviderHealth> CheckHealthAsync(CancellationToken cancellationToken);

    ProviderRequestValidationResult ValidateRequest(SpeedTestRequest request);

    Task<SpeedTestResult> RunAsync(
        SpeedTestExecution execution,
        CancellationToken cancellationToken);
}

public interface ISpeedTestServerProvider
{
    Task<IReadOnlyList<SpeedTestServer>> GetServersAsync(
        ServerQuery query,
        CancellationToken cancellationToken);
}

public sealed record ProviderRequestValidationResult(bool IsValid, string? Code = null, string? Message = null)
{
    public static readonly ProviderRequestValidationResult Valid = new(true);

    public static ProviderRequestValidationResult Invalid(string code, string message) => new(false, code, message);
}
