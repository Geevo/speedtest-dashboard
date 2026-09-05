using SpeedtestDashboard.Core.Providers;

namespace SpeedtestDashboard.Core.Tests;

public sealed record SpeedTestRequestValidationResult(bool IsValid, string? Code, string? Message)
{
    public static readonly SpeedTestRequestValidationResult Valid = new(true, null, null);

    public static SpeedTestRequestValidationResult Invalid(string code, string message) => new(false, code, message);
}

public static class SpeedTestRequestValidation
{
    public static SpeedTestRequestValidationResult Validate(SpeedTestRequest request, ISpeedTestProviderRegistry registry)
    {
        if (!registry.TryGet(request.ProviderId, out var provider))
        {
            return SpeedTestRequestValidationResult.Invalid(
                SpeedTestFailureCodes.ProviderNotFound,
                "The requested speed-test provider is not registered.");
        }

        if (request.ServerId is not null && !provider.Capabilities.HasFlag(ProviderCapabilities.ServerSelection))
        {
            return SpeedTestRequestValidationResult.Invalid(
                SpeedTestFailureCodes.CapabilityNotSupported,
                "This provider does not support explicit server selection.");
        }

        if (provider is ISpeedTestRequestValidator validator)
        {
            var validation = validator.ValidateRequest(request);
            if (!validation.IsValid)
            {
                return SpeedTestRequestValidationResult.Invalid(
                    validation.Code ?? SpeedTestFailureCodes.InvalidRequest,
                    validation.Message ?? "The provider-specific request is invalid.");
            }
        }

        return SpeedTestRequestValidationResult.Valid;
    }
}
