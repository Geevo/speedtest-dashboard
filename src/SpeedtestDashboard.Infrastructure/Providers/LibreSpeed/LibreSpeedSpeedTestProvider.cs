using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SpeedtestDashboard.Core.Providers;
using SpeedtestDashboard.Core.Tests;
using SpeedtestDashboard.Infrastructure.Processes;

namespace SpeedtestDashboard.Infrastructure.Providers.LibreSpeed;

public sealed partial class LibreSpeedSpeedTestProvider(
    IProcessRunner processRunner,
    LibreSpeedCommandFactory commandFactory,
    LibreSpeedServerCatalogClient serverCatalogClient,
    LibreSpeedResultParser resultParser,
    IOptions<LibreSpeedOptions> options,
    TimeProvider timeProvider,
    ILogger<LibreSpeedSpeedTestProvider> logger) : ISpeedTestProvider, ISpeedTestServerProvider
{
    private readonly SemaphoreSlim _healthGate = new(1, 1);
    private readonly SemaphoreSlim _serverGate = new(1, 1);
    private ProviderHealth? _cachedHealth;
    private DateTimeOffset _healthExpiresAtUtc;
    private IReadOnlyList<LibreSpeedServerDefinition>? _cachedServers;
    private DateTimeOffset _serversExpireAtUtc;

    public ProviderDescriptor Descriptor => LibreSpeedProviderDefinition.Descriptor;

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
        IReadOnlyList<LibreSpeedServerDefinition> catalog;
        try
        {
            catalog = await GetCachedServersAsync(cancellationToken);
        }
        catch (LibreSpeedOutputException)
        {
            throw new ProviderExecutionException(
                LibreSpeedFailureCodes.ServerListFailed,
                "The LibreSpeed public server catalogue could not be loaded.");
        }
        IEnumerable<LibreSpeedServerDefinition> filtered = catalog;
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.Trim();
            filtered = filtered.Where(server =>
                server.Server.Id.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                server.Server.Name.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                server.Server.Location?.Contains(search, StringComparison.OrdinalIgnoreCase) is true ||
                server.Server.Sponsor?.Contains(search, StringComparison.OrdinalIgnoreCase) is true ||
                server.Server.Host?.Contains(search, StringComparison.OrdinalIgnoreCase) is true);
        }

        return filtered
            .Take(Math.Min(query.Limit, options.Value.MaximumServers))
            .Select(server => server.Server)
            .ToArray();
    }

    public ProviderRequestValidationResult ValidateRequest(SpeedTestRequest request)
    {
        if (request.ProviderId != Descriptor.Id)
        {
            return ProviderRequestValidationResult.Invalid(
                SpeedTestFailureCodes.InvalidRequest,
                "The request provider does not match LibreSpeed.");
        }

        if (request.ServerId is not null && !LibreSpeedServerId.IsValid(request.ServerId))
        {
            return ProviderRequestValidationResult.Invalid(
                SpeedTestFailureCodes.InvalidRequest,
                "LibreSpeed server IDs must be positive 32-bit integers without leading zeroes.");
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
                validation.Message ?? "The LibreSpeed request is invalid.");
        }

        var processResult = await processRunner.RunAsync(
            commandFactory.CreateTestCommand(execution.Request.ServerId),
            cancellationToken);
        EnsureSuccessfulProcess(processResult, cancellationToken, "test");

        IReadOnlyList<LibreSpeedServerDefinition> catalog = [];
        try
        {
            catalog = await GetCachedServersAsync(cancellationToken);
        }
        catch (LibreSpeedOutputException exception)
        {
            logger.LogWarning(
                "LibreSpeed result server could not be resolved from the catalogue for job {JobId}: {Message}",
                execution.JobId,
                BoundForLog(exception.Message));
        }

        try
        {
            return resultParser.Parse(processResult.StandardOutput, execution.Request.ServerId, catalog);
        }
        catch (LibreSpeedOutputException exception)
        {
            logger.LogWarning(
                "LibreSpeed result parsing failed for job {JobId}: {ParserMessage}",
                execution.JobId,
                BoundForLog(exception.Message));
            throw new ProviderExecutionException(
                exception.Message.Contains("missing", StringComparison.OrdinalIgnoreCase) ||
                exception.Message.Contains("no test results", StringComparison.OrdinalIgnoreCase)
                    ? LibreSpeedFailureCodes.ResultIncomplete
                    : LibreSpeedFailureCodes.InvalidOutput,
                "LibreSpeed CLI returned an invalid result.");
        }
    }

    private async Task<ProviderHealth> ProbeHealthAsync(CancellationToken cancellationToken)
    {
        var checkedAt = timeProvider.GetUtcNow();
        if (!options.Value.Enabled)
        {
            return Health(ProviderHealthState.Unavailable, null, checkedAt, "LibreSpeed provider is disabled.");
        }

        var result = await processRunner.RunAsync(commandFactory.CreateVersionCommand(), cancellationToken);
        if (result.TerminationReason == ProcessTerminationReason.Cancelled)
        {
            cancellationToken.ThrowIfCancellationRequested();
        }

        if (result.TerminationReason == ProcessTerminationReason.FailedToStart)
        {
            return Health(ProviderHealthState.Unavailable, null, checkedAt, "LibreSpeed CLI is not installed.", installed: false);
        }

        if (result.TerminationReason == ProcessTerminationReason.TimedOut)
        {
            return Health(ProviderHealthState.Unavailable, null, checkedAt, "LibreSpeed CLI version check timed out.");
        }

        if (result.TerminationReason != ProcessTerminationReason.Exited || result.ExitCode != 0)
        {
            return Health(ProviderHealthState.Unavailable, null, checkedAt, "LibreSpeed CLI version check failed.");
        }

        var version = ParseVersion(result.StandardOutput);
        if (version is null)
        {
            return Health(ProviderHealthState.Degraded, null, checkedAt, "LibreSpeed CLI returned an unrecognized version.");
        }

        return version == LibreSpeedOptions.PinnedVersion
            ? Health(ProviderHealthState.Available, version, checkedAt, "Ready")
            : Health(ProviderHealthState.Degraded, version, checkedAt, "The installed LibreSpeed CLI version differs from the tested version.");
    }

    private async Task<IReadOnlyList<LibreSpeedServerDefinition>> GetCachedServersAsync(
        CancellationToken cancellationToken)
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

            try
            {
                _cachedServers = await serverCatalogClient.GetServersAsync(cancellationToken);
                _serversExpireAtUtc = now.AddSeconds(options.Value.ServerCacheSeconds);
                return _cachedServers;
            }
            catch (LibreSpeedOutputException exception)
            {
                logger.LogWarning("LibreSpeed server discovery failed: {Message}", BoundForLog(exception.Message));
                throw;
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
            throw new ProviderExecutionException(
                LibreSpeedFailureCodes.Disabled,
                "LibreSpeed provider is disabled.");
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
                    LibreSpeedFailureCodes.Timeout,
                    $"LibreSpeed {operation} timed out.");
            case ProcessTerminationReason.OutputLimitExceeded:
                throw new ProviderExecutionException(
                    LibreSpeedFailureCodes.InvalidOutput,
                    $"LibreSpeed {operation} returned too much output.");
            case ProcessTerminationReason.FailedToStart:
                throw new ProviderExecutionException(
                    LibreSpeedFailureCodes.NotInstalled,
                    "LibreSpeed CLI is not installed.");
        }

        if (result.ExitCode == 0)
        {
            return;
        }

        var stderr = result.StandardError.ToLowerInvariant();
        if (stderr.Contains("server", StringComparison.Ordinal) &&
            (stderr.Contains("not found", StringComparison.Ordinal) ||
             stderr.Contains("no server", StringComparison.Ordinal) ||
             stderr.Contains("invalid", StringComparison.Ordinal)))
        {
            throw new ProviderExecutionException(
                LibreSpeedFailureCodes.ServerNotFound,
                "The selected LibreSpeed server was not found.");
        }

        if (stderr.Contains("network is unreachable", StringComparison.Ordinal) ||
            stderr.Contains("no route to host", StringComparison.Ordinal) ||
            stderr.Contains("failed to resolve", StringComparison.Ordinal) ||
            stderr.Contains("connection refused", StringComparison.Ordinal) ||
            stderr.Contains("error when fetching server list", StringComparison.Ordinal))
        {
            throw new ProviderExecutionException(
                LibreSpeedFailureCodes.NetworkUnavailable,
                "LibreSpeed CLI could not reach the public server network.");
        }

        throw new ProviderExecutionException(
            LibreSpeedFailureCodes.Failed,
            $"LibreSpeed {operation} failed.");
    }

    private ProviderHealth Health(
        ProviderHealthState state,
        string? version,
        DateTimeOffset checkedAt,
        string message,
        bool installed = true) => new(Descriptor.Id, state, version, checkedAt, message, installed);

    internal static string? ParseVersion(string output)
    {
        foreach (var line in output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var match = VersionRegex().Match(line);
            if (match.Success)
            {
                return match.Groups["version"].Value;
            }
        }

        return null;
    }

    private static string BoundForLog(string value) => value.Length <= 512 ? value : value[..512];

    [GeneratedRegex(@"^librespeed-cli v?(?<version>\d+\.\d+\.\d+) \(built on [^)]+\)$", RegexOptions.CultureInvariant)]
    private static partial Regex VersionRegex();
}
