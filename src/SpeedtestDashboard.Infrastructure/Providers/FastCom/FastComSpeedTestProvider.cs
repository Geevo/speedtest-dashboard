using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SpeedtestDashboard.Core.Providers;
using SpeedtestDashboard.Core.Tests;
using SpeedtestDashboard.Infrastructure.Processes;

namespace SpeedtestDashboard.Infrastructure.Providers.FastCom;

public sealed partial class FastComSpeedTestProvider(
    IProcessRunner processRunner,
    FastComCommandFactory commandFactory,
    FastComResultParser resultParser,
    IOptions<FastComOptions> options,
    TimeProvider timeProvider,
    ILogger<FastComSpeedTestProvider> logger) : ISpeedTestProvider
{
    private readonly SemaphoreSlim _healthGate = new(1, 1);
    private ProviderHealth? _cachedHealth;
    private DateTimeOffset _healthExpiresAtUtc;

    public ProviderDescriptor Descriptor => FastComProviderDefinition.Descriptor;

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
                "The request provider does not match FAST.com.");
        }

        return request.ServerId is null
            ? ProviderRequestValidationResult.Valid
            : ProviderRequestValidationResult.Invalid(
                SpeedTestFailureCodes.CapabilityNotSupported,
                "FAST.com does not support explicit server selection.");
    }

    public async Task<SpeedTestResult> RunAsync(SpeedTestExecution execution, CancellationToken cancellationToken)
    {
        EnsureOperational();
        var validation = ValidateRequest(execution.Request);
        if (!validation.IsValid)
        {
            throw new ProviderExecutionException(
                validation.Code ?? SpeedTestFailureCodes.InvalidRequest,
                validation.Message ?? "The FAST.com request is invalid.");
        }

        var processResult = await processRunner.RunAsync(commandFactory.CreateTestCommand(), cancellationToken);
        EnsureSuccessfulProcess(processResult, cancellationToken);
        try
        {
            return resultParser.Parse(processResult.StandardOutput);
        }
        catch (FastComOutputException exception)
        {
            logger.LogWarning(
                "FAST.com result parsing failed for job {JobId}: {ParserMessage}",
                execution.JobId,
                BoundForLog(exception.Message));
            throw new ProviderExecutionException(
                FailureCode(exception),
                exception.ProviderReportedFailure
                    ? "FAST.com could not complete the speed test."
                    : "FAST.com CLI returned an invalid result.");
        }
    }

    private async Task<ProviderHealth> ProbeHealthAsync(CancellationToken cancellationToken)
    {
        var checkedAt = timeProvider.GetUtcNow();
        if (!options.Value.Enabled)
        {
            return Health(ProviderHealthState.Unavailable, null, checkedAt, "FAST.com provider is disabled.");
        }

        var result = await processRunner.RunAsync(commandFactory.CreateVersionCommand(), cancellationToken);
        if (result.TerminationReason == ProcessTerminationReason.Cancelled)
        {
            cancellationToken.ThrowIfCancellationRequested();
        }

        if (result.TerminationReason == ProcessTerminationReason.FailedToStart)
        {
            return Health(ProviderHealthState.Unavailable, null, checkedAt, "FAST.com CLI is not installed.", installed: false);
        }

        if (result.TerminationReason == ProcessTerminationReason.TimedOut)
        {
            return Health(ProviderHealthState.Unavailable, null, checkedAt, "FAST.com CLI version check timed out.");
        }

        if (result.TerminationReason != ProcessTerminationReason.Exited || result.ExitCode != 0)
        {
            return Health(ProviderHealthState.Unavailable, null, checkedAt, "FAST.com CLI version check failed.");
        }

        var version = ParseVersion($"{result.StandardOutput}\n{result.StandardError}");
        if (version is null)
        {
            return Health(ProviderHealthState.Degraded, null, checkedAt, "FAST.com CLI returned an unrecognized version.");
        }

        return version == FastComOptions.PinnedVersion
            ? Health(ProviderHealthState.Available, version, checkedAt, "Ready")
            : Health(ProviderHealthState.Degraded, version, checkedAt, "The installed FAST.com CLI version differs from the tested version.");
    }

    private void EnsureOperational()
    {
        if (!options.Value.Enabled)
        {
            throw new ProviderExecutionException(
                FastComFailureCodes.Disabled,
                "FAST.com provider is disabled.");
        }
    }

    private static void EnsureSuccessfulProcess(ProcessResult result, CancellationToken cancellationToken)
    {
        switch (result.TerminationReason)
        {
            case ProcessTerminationReason.Cancelled:
                throw new OperationCanceledException(cancellationToken);
            case ProcessTerminationReason.TimedOut:
                throw new ProviderExecutionException(
                    FastComFailureCodes.Timeout,
                    "FAST.com test timed out.");
            case ProcessTerminationReason.OutputLimitExceeded:
                throw new ProviderExecutionException(
                    FastComFailureCodes.InvalidOutput,
                    "FAST.com test returned too much output.");
            case ProcessTerminationReason.FailedToStart:
                throw new ProviderExecutionException(
                    FastComFailureCodes.NotInstalled,
                    "FAST.com CLI is not installed.");
        }

        if (result.ExitCode == 0)
        {
            return;
        }

        var output = $"{result.StandardError}\n{result.StandardOutput}".ToLowerInvariant();
        if (output.Contains("network", StringComparison.Ordinal) ||
            output.Contains("connection", StringComparison.Ordinal) ||
            output.Contains("resolve", StringComparison.Ordinal))
        {
            throw new ProviderExecutionException(
                FastComFailureCodes.NetworkUnavailable,
                "FAST.com CLI could not reach the FAST.com network.");
        }

        throw new ProviderExecutionException(
            FastComFailureCodes.Failed,
            "FAST.com test failed.");
    }

    private static string FailureCode(FastComOutputException exception)
    {
        if (exception.ProviderReportedFailure)
        {
            return exception.Message.Contains("contact fast.com", StringComparison.OrdinalIgnoreCase)
                ? FastComFailureCodes.NetworkUnavailable
                : FastComFailureCodes.Failed;
        }

        return exception.Message.Contains("missing", StringComparison.OrdinalIgnoreCase)
            ? FastComFailureCodes.ResultIncomplete
            : FastComFailureCodes.InvalidOutput;
    }

    private ProviderHealth Health(
        ProviderHealthState state,
        string? version,
        DateTimeOffset checkedAt,
        string message,
        bool installed = true) => new(Descriptor.Id, state, version, checkedAt, message, installed);

    internal static string? ParseVersion(string output)
    {
        var match = VersionRegex().Match(output);
        return match.Success ? match.Groups["version"].Value : null;
    }

    private static string BoundForLog(string value) => value.Length <= 512 ? value : value[..512];

    [GeneratedRegex(@"v(?<version>\d+\.\d+\.\d+) - Estimate connection speed using fast\.com", RegexOptions.CultureInvariant)]
    private static partial Regex VersionRegex();
}
