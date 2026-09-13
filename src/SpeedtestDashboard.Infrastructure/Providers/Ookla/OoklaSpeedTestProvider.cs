using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SpeedtestDashboard.Core.Providers;
using SpeedtestDashboard.Core.Tests;
using SpeedtestDashboard.Infrastructure.Processes;

namespace SpeedtestDashboard.Infrastructure.Providers.Ookla;

public sealed partial class OoklaSpeedTestProvider(
    IProcessRunner processRunner,
    OoklaCommandFactory commandFactory,
    OoklaServerListParser serverListParser,
    OoklaResultParser resultParser,
    IOptions<OoklaOptions> options,
    TimeProvider timeProvider,
    ILogger<OoklaSpeedTestProvider> logger) : ISpeedTestProvider, ISpeedTestServerProvider
{
    private readonly SemaphoreSlim _healthGate = new(1, 1);
    private readonly SemaphoreSlim _serverGate = new(1, 1);
    private ProviderHealth? _cachedHealth;
    private DateTimeOffset _healthExpiresAtUtc;
    private IReadOnlyList<SpeedTestServer>? _cachedServers;
    private DateTimeOffset _serversExpireAtUtc;

    public ProviderDescriptor Descriptor => OoklaProviderDefinition.Descriptor;

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

    public async Task<IReadOnlyList<SpeedTestServer>> GetServersAsync(
        ServerQuery query,
        CancellationToken cancellationToken)
    {
        EnsureOperational();
        var servers = await GetCachedServersAsync(cancellationToken);
        IEnumerable<SpeedTestServer> filtered = servers;
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.Trim();
            filtered = filtered.Where(server =>
                server.Id.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                server.Name.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                server.Location?.Contains(search, StringComparison.OrdinalIgnoreCase) is true ||
                server.Sponsor?.Contains(search, StringComparison.OrdinalIgnoreCase) is true);
        }

        return filtered.Take(Math.Min(query.Limit, options.Value.MaximumServers)).ToArray();
    }

    public ProviderRequestValidationResult ValidateRequest(SpeedTestRequest request)
    {
        if (request.ProviderId != Descriptor.Id)
        {
            return ProviderRequestValidationResult.Invalid(
                SpeedTestFailureCodes.InvalidRequest,
                "The request provider does not match Ookla.");
        }

        if (request.ServerId is not null && !OoklaServerId.IsValid(request.ServerId))
        {
            return ProviderRequestValidationResult.Invalid(
                SpeedTestFailureCodes.InvalidRequest,
                "Ookla server IDs must contain 1 to 10 decimal digits and cannot start with zero.");
        }

        return ProviderRequestValidationResult.Valid;
    }

    public async Task<SpeedTestResult> RunAsync(
        SpeedTestExecution execution,
        CancellationToken cancellationToken)
    {
        EnsureOperational();
        var validation = ValidateRequest(execution.Request);
        if (!validation.IsValid)
        {
            throw new ProviderExecutionException(
                validation.Code ?? SpeedTestFailureCodes.InvalidRequest,
                validation.Message ?? "The Ookla request is invalid.");
        }

        var processResult = await processRunner.RunAsync(
            commandFactory.CreateTestCommand(execution.Request.ServerId),
            cancellationToken);
        EnsureSuccessfulProcess(processResult, cancellationToken, "test");
        try
        {
            return resultParser.Parse(processResult.StandardOutput);
        }
        catch (OoklaOutputException exception)
        {
            logger.LogWarning(
                "Ookla result parsing failed for job {JobId}: {ParserMessage}",
                execution.JobId,
                BoundForLog(exception.Message));
            throw new ProviderExecutionException(
                exception.Message.Contains("missing", StringComparison.OrdinalIgnoreCase)
                    ? OoklaFailureCodes.ResultIncomplete
                    : OoklaFailureCodes.InvalidOutput,
                "Speedtest CLI returned an invalid result.");
        }
    }

    private async Task<ProviderHealth> ProbeHealthAsync(CancellationToken cancellationToken)
    {
        var checkedAt = timeProvider.GetUtcNow();
        if (!options.Value.Enabled)
        {
            return Health(ProviderHealthState.Unavailable, null, checkedAt, "Ookla provider is disabled.");
        }

        var result = await processRunner.RunAsync(commandFactory.CreateVersionCommand(), cancellationToken);
        if (result.TerminationReason == ProcessTerminationReason.Cancelled)
        {
            cancellationToken.ThrowIfCancellationRequested();
        }

        if (result.TerminationReason == ProcessTerminationReason.FailedToStart)
        {
            return Health(ProviderHealthState.Unavailable, null, checkedAt, "Speedtest CLI is not installed.");
        }

        if (result.TerminationReason == ProcessTerminationReason.TimedOut)
        {
            return Health(ProviderHealthState.Unavailable, null, checkedAt, "Speedtest CLI version check timed out.");
        }

        if (result.TerminationReason != ProcessTerminationReason.Exited || result.ExitCode != 0)
        {
            return Health(ProviderHealthState.Unavailable, null, checkedAt, "Speedtest CLI version check failed.");
        }

        var version = ParseVersion(result.StandardOutput);
        if (version is null)
        {
            return Health(ProviderHealthState.Degraded, null, checkedAt, "Speedtest CLI returned an unrecognized version.");
        }

        if (!options.Value.AcceptLicense || !options.Value.AcceptGdpr)
        {
            return Health(
                ProviderHealthState.Unavailable,
                version,
                checkedAt,
                "License and GDPR acceptance have not been configured.");
        }

        return version == OoklaOptions.PinnedVersion
            ? Health(ProviderHealthState.Available, version, checkedAt, "Ready")
            : Health(ProviderHealthState.Degraded, version, checkedAt, "The installed Speedtest CLI version differs from the tested version.");
    }

    private async Task<IReadOnlyList<SpeedTestServer>> GetCachedServersAsync(CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        if (_cachedServers is not null && now < _serversExpireAtUtc)
        {
            return _cachedServers;
        }

        await _serverGate.WaitAsync(cancellationToken);
        try
        {
            now = timeProvider.GetUtcNow();
            if (_cachedServers is not null && now < _serversExpireAtUtc)
            {
                return _cachedServers;
            }

            var result = await processRunner.RunAsync(commandFactory.CreateServerListCommand(), cancellationToken);
            EnsureSuccessfulProcess(result, cancellationToken, "server discovery");
            try
            {
                _cachedServers = serverListParser.Parse(result.StandardOutput, options.Value.MaximumServers);
                _serversExpireAtUtc = now.AddSeconds(options.Value.ServerCacheSeconds);
                return _cachedServers;
            }
            catch (OoklaOutputException exception)
            {
                logger.LogWarning("Ookla server-list parsing failed: {ParserMessage}", BoundForLog(exception.Message));
                throw new ProviderExecutionException(
                    OoklaFailureCodes.InvalidOutput,
                    "Speedtest CLI returned an invalid server list.");
            }
        }
        finally
        {
            _serverGate.Release();
        }
    }

    private void EnsureOperational()
    {
        if (!options.Value.Enabled)
        {
            throw new ProviderExecutionException(OoklaFailureCodes.Disabled, "Ookla provider is disabled.");
        }

        if (!options.Value.AcceptLicense || !options.Value.AcceptGdpr)
        {
            throw new ProviderExecutionException(
                OoklaFailureCodes.LicenseNotAccepted,
                "Ookla license and GDPR acceptance have not been configured.");
        }
    }

    private static void EnsureSuccessfulProcess(
        ProcessResult result,
        CancellationToken cancellationToken,
        string operation)
    {
        switch (result.TerminationReason)
        {
            case ProcessTerminationReason.Cancelled:
                throw new OperationCanceledException(cancellationToken);
            case ProcessTerminationReason.TimedOut:
                throw new ProviderExecutionException(
                    OoklaFailureCodes.Timeout,
                    $"Ookla {operation} timed out.");
            case ProcessTerminationReason.OutputLimitExceeded:
                throw new ProviderExecutionException(
                    OoklaFailureCodes.InvalidOutput,
                    $"Ookla {operation} returned too much output.");
            case ProcessTerminationReason.FailedToStart:
                throw new ProviderExecutionException(
                    OoklaFailureCodes.NotInstalled,
                    "Speedtest CLI is not installed.");
        }

        if (result.ExitCode != 0)
        {
            var stderr = result.StandardError.ToLowerInvariant();
            if (stderr.Contains("license", StringComparison.Ordinal) || stderr.Contains("gdpr", StringComparison.Ordinal))
            {
                throw new ProviderExecutionException(
                    OoklaFailureCodes.LicenseNotAccepted,
                    "Ookla license acceptance was rejected by Speedtest CLI.");
            }

            if (stderr.Contains("server", StringComparison.Ordinal) &&
                (stderr.Contains("not found", StringComparison.Ordinal) || stderr.Contains("invalid", StringComparison.Ordinal)))
            {
                throw new ProviderExecutionException(
                    OoklaFailureCodes.ServerNotFound,
                    "The selected Ookla server was not found.");
            }

            if (stderr.Contains("network is unreachable", StringComparison.Ordinal) ||
                stderr.Contains("couldn't connect", StringComparison.Ordinal) ||
                stderr.Contains("no route to host", StringComparison.Ordinal) ||
                stderr.Contains("failed to resolve", StringComparison.Ordinal))
            {
                throw new ProviderExecutionException(
                    OoklaFailureCodes.NetworkUnavailable,
                    "Speedtest CLI could not reach the Ookla network.");
            }

            throw new ProviderExecutionException(
                OoklaFailureCodes.Failed,
                $"Ookla {operation} failed.");
        }
    }

    private ProviderHealth Health(
        ProviderHealthState state,
        string? version,
        DateTimeOffset checkedAt,
        string message) => new(Descriptor.Id, state, version, checkedAt, message);

    private static string? ParseVersion(string output)
    {
        var match = VersionRegex().Match(output.Trim());
        return match.Success ? match.Groups["version"].Value : null;
    }

    private static string BoundForLog(string value) => value.Length <= 512 ? value : value[..512];

    [GeneratedRegex(@"^Speedtest by Ookla (?<version>\d+\.\d+\.\d+\.\d+)(?:\s|$)", RegexOptions.CultureInvariant)]
    private static partial Regex VersionRegex();
}
