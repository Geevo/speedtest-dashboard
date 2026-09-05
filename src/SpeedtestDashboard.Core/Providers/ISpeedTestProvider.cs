using SpeedtestDashboard.Core.Tests;

namespace SpeedtestDashboard.Core.Providers;

public interface ISpeedTestProvider
{
    ProviderId Id { get; }

    string DisplayName { get; }

    ProviderCapabilities Capabilities { get; }

    Task<ProviderHealth> CheckHealthAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<SpeedTestServer>> GetServersAsync(
        ServerQuery query,
        CancellationToken cancellationToken);

    Task<SpeedTestResult> RunAsync(
        SpeedTestExecution execution,
        CancellationToken cancellationToken);
}

public interface ISpeedTestRequestValidator
{
    ProviderRequestValidationResult ValidateRequest(SpeedTestRequest request);
}

public sealed record ProviderRequestValidationResult(bool IsValid, string? Code = null, string? Message = null)
{
    public static readonly ProviderRequestValidationResult Valid = new(true);

    public static ProviderRequestValidationResult Invalid(string code, string message) => new(false, code, message);
}
