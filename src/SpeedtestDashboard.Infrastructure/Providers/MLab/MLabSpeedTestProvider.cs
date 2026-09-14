using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SpeedtestDashboard.Core.Providers;
using SpeedtestDashboard.Core.Tests;
using SpeedtestDashboard.Infrastructure.Processes;

namespace SpeedtestDashboard.Infrastructure.Providers.MLab;

public sealed class MLabSpeedTestProvider(
    IProcessRunner processRunner,
    MLabCommandFactory commandFactory,
    MLabResultParser resultParser,
    IOptions<MLabOptions> options,
    TimeProvider timeProvider,
    ILogger<MLabSpeedTestProvider> logger) : ISpeedTestProvider
{
    private readonly SemaphoreSlim _healthGate = new(1, 1);
    private ProviderHealth? _cachedHealth;
    private DateTimeOffset _healthExpiresAtUtc;

    public ProviderDescriptor Descriptor => MLabProviderDefinition.Descriptor;

    public async Task<ProviderHealth> CheckHealthAsync(CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        if (_cachedHealth is not null && now < _healthExpiresAtUtc)
        {
            return _cachedHealth;
        }

        await _healthGate.WaitAsync(cancellationToken);
        try
        {
            now = timeProvider.GetUtcNow();
            if (_cachedHealth is not null && now < _healthExpiresAtUtc)
            {
                return _cachedHealth;
            }

            var health = await ProbeHealthAsync(cancellationToken);
            _cachedHealth = health;
            _healthExpiresAtUtc = now.AddSeconds(options.Value.HealthCacheSeconds);
            return health;
        }
        finally
        {
            _healthGate.Release();
        }
    }

    public ProviderRequestValidationResult ValidateRequest(SpeedTestRequest request)
    {
        if (request.ProviderId != Descriptor.Id)
        {
            return ProviderRequestValidationResult.Invalid(
                SpeedTestFailureCodes.InvalidRequest,
                "The request provider does not match M-Lab.");
        }

        return request.ServerId is null
            ? ProviderRequestValidationResult.Valid
            : ProviderRequestValidationResult.Invalid(
                SpeedTestFailureCodes.CapabilityNotSupported,
                "M-Lab does not support explicit server selection.");
    }

    public async Task<SpeedTestResult> RunAsync(SpeedTestExecution execution, CancellationToken cancellationToken)
    {
        EnsureOperational();
        var validation = ValidateRequest(execution.Request);
        if (!validation.IsValid)
        {
            throw new ProviderExecutionException(
                validation.Code ?? SpeedTestFailureCodes.InvalidRequest,
                validation.Message ?? "The M-Lab request is invalid.");
        }

        var processResult = await processRunner.RunAsync(commandFactory.CreateTestCommand(), cancellationToken);
        EnsureSuccessfulProcess(processResult, cancellationToken);
        try
        {
            return resultParser.Parse(processResult.StandardOutput);
        }
        catch (MLabOutputException exception)
        {
            logger.LogWarning(
                "M-Lab result parsing failed for job {JobId}: {ParserMessage}",
                execution.JobId,
                BoundForLog(exception.Message));
            throw new ProviderExecutionException(
                exception.Message.Contains("missing", StringComparison.OrdinalIgnoreCase)
                    ? MLabFailureCodes.ResultIncomplete
                    : MLabFailureCodes.InvalidOutput,
                "M-Lab NDT7 client returned an invalid result.");
        }
    }

    private async Task<ProviderHealth> ProbeHealthAsync(CancellationToken cancellationToken)
    {
        var checkedAt = timeProvider.GetUtcNow();
        if (!options.Value.Enabled)
        {
            return Health(ProviderHealthState.Unavailable, null, checkedAt, "M-Lab provider is disabled.");
        }

        var result = await processRunner.RunAsync(commandFactory.CreateHealthCommand(), cancellationToken);
        if (result.TerminationReason == ProcessTerminationReason.Cancelled)
        {
            cancellationToken.ThrowIfCancellationRequested();
        }

        if (result.TerminationReason == ProcessTerminationReason.FailedToStart)
        {
            return Health(ProviderHealthState.Unavailable, null, checkedAt, "M-Lab NDT7 client is not installed.", installed: false);
        }

        if (result.TerminationReason == ProcessTerminationReason.TimedOut)
        {
            return Health(ProviderHealthState.Unavailable, null, checkedAt, "M-Lab NDT7 client health check timed out.");
        }

        if (result.TerminationReason != ProcessTerminationReason.Exited || result.ExitCode != 0)
        {
            return Health(ProviderHealthState.Unavailable, null, checkedAt, "M-Lab NDT7 client health check failed.");
        }

        var output = $"{result.StandardOutput}\n{result.StandardError}";
        if (!output.Contains("Usage of", StringComparison.Ordinal) ||
            !output.Contains("-format", StringComparison.Ordinal) ||
            !output.Contains("-client-name", StringComparison.Ordinal))
        {
            return Health(ProviderHealthState.Degraded, null, checkedAt, "M-Lab NDT7 client returned unrecognized help output.");
        }

        return Health(ProviderHealthState.Available, MLabOptions.PinnedVersion, checkedAt, "Ready");
    }

    private void EnsureOperational()
    {
        if (!options.Value.Enabled)
        {
            throw new ProviderExecutionException(MLabFailureCodes.Disabled, "M-Lab provider is disabled.");
        }
    }

    private static void EnsureSuccessfulProcess(ProcessResult result, CancellationToken cancellationToken)
    {
        switch (result.TerminationReason)
        {
            case ProcessTerminationReason.Cancelled:
                throw new OperationCanceledException(cancellationToken);
            case ProcessTerminationReason.TimedOut:
                throw new ProviderExecutionException(MLabFailureCodes.Timeout, "M-Lab test timed out.");
            case ProcessTerminationReason.OutputLimitExceeded:
                throw new ProviderExecutionException(MLabFailureCodes.InvalidOutput, "M-Lab test returned too much output.");
            case ProcessTerminationReason.FailedToStart:
                throw new ProviderExecutionException(MLabFailureCodes.NotInstalled, "M-Lab NDT7 client is not installed.");
        }

        if (result.ExitCode == 0)
        {
            return;
        }

        var output = $"{result.StandardError}\n{result.StandardOutput}".ToLowerInvariant();
        if (output.Contains("network", StringComparison.Ordinal) ||
            output.Contains("connection", StringComparison.Ordinal) ||
            output.Contains("connect", StringComparison.Ordinal) ||
            output.Contains("dial", StringComparison.Ordinal) ||
            output.Contains("resolve", StringComparison.Ordinal) ||
            output.Contains("lookup", StringComparison.Ordinal) ||
            output.Contains("timeout", StringComparison.Ordinal))
        {
            throw new ProviderExecutionException(
                MLabFailureCodes.NetworkUnavailable,
                "M-Lab NDT7 client could not reach the Measurement Lab network.");
        }

        throw new ProviderExecutionException(MLabFailureCodes.Failed, "M-Lab test failed.");
    }

    private ProviderHealth Health(
        ProviderHealthState state,
        string? version,
        DateTimeOffset checkedAt,
        string message,
        bool installed = true) => new(Descriptor.Id, state, version, checkedAt, message, installed);

    private static string BoundForLog(string value) => value.Length <= 512 ? value : value[..512];
}
