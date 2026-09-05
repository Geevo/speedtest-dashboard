using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SpeedtestDashboard.Core.Network;
using SpeedtestDashboard.Core.Providers;
using SpeedtestDashboard.Core.Tests;

namespace SpeedtestDashboard.Infrastructure.Tests;

public sealed class SpeedTestWorker(
    ISpeedTestQueue queue,
    ISpeedTestJobStore jobStore,
    ISpeedTestProviderRegistry providerRegistry,
    ISpeedTestCancellationRegistry cancellationRegistry,
    INetworkIdentityService networkIdentityService,
    ILogger<SpeedTestWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var jobId in queue.ReadAllAsync(stoppingToken))
            {
                try
                {
                    await ExecuteJobAsync(jobId, stoppingToken);
                }
                catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
                {
                    logger.LogError(exception, "Unexpected orchestration failure for speed-test job {JobId}.", jobId);
                    TryFail(jobId, SpeedTestFailureCodes.InternalError, "The speed test failed unexpectedly.");
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Cancellation is expected when the host stops.
        }
    }

    private async Task ExecuteJobAsync(Guid jobId, CancellationToken stoppingToken)
    {
        if (!jobStore.TryGet(jobId, out var queuedJob) || queuedJob.Status != SpeedTestJobStatus.Queued)
        {
            return;
        }

        if (jobStore.Transition(
            jobId,
            SpeedTestJobStatus.Starting,
            "Starting",
            out var startingJob) != JobMutationResult.Success || startingJob is null)
        {
            return;
        }

        using var cancellation = cancellationRegistry.Register(jobId, stoppingToken);
        try
        {
            if (!providerRegistry.TryGet(startingJob.Request.ProviderId, out var provider))
            {
                TryFail(jobId, SpeedTestFailureCodes.ProviderUnavailable, "The selected provider is no longer available.");
                return;
            }

            var identity = await networkIdentityService.GetAsync(forceRefresh: false, cancellation.Token);
            if (jobStore.Transition(
                jobId,
                SpeedTestJobStatus.Running,
                "Running",
                out var runningJob,
                egressIdentity: identity) != JobMutationResult.Success || runningJob is null)
            {
                return;
            }

            var result = await provider.RunAsync(
                new SpeedTestExecution(jobId, runningJob.Request, identity),
                cancellation.Token);

            if (result.ProviderId != runningJob.Request.ProviderId)
            {
                TryFail(jobId, SpeedTestFailureCodes.InvalidProviderResult, "The provider returned an invalid result.");
                return;
            }

            if (jobStore.Transition(
                jobId,
                SpeedTestJobStatus.ProcessingResult,
                "Processing result",
                out _) != JobMutationResult.Success)
            {
                return;
            }

            var normalizedResult = result with { JobId = jobId, EgressIdentity = identity };
            jobStore.Transition(
                jobId,
                SpeedTestJobStatus.Completed,
                "Completed",
                out _,
                result: normalizedResult);
        }
        catch (OperationCanceledException) when (cancellation.Token.IsCancellationRequested)
        {
            jobStore.Transition(
                jobId,
                SpeedTestJobStatus.Cancelled,
                "Cancelled",
                out _);
        }
        catch (ProviderExecutionException exception)
        {
            TryFail(jobId, NormalizeFailureCode(exception.Code), NormalizeMessage(exception.SafeMessage));
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Provider {ProviderId} failed for speed-test job {JobId}.",
                startingJob.Request.ProviderId.Value,
                jobId);
            TryFail(jobId, SpeedTestFailureCodes.InternalError, "The speed test failed unexpectedly.");
        }
    }

    private void TryFail(Guid jobId, string code, string message)
    {
        jobStore.Transition(
            jobId,
            SpeedTestJobStatus.Failed,
            "Failed",
            out _,
            failure: new SpeedTestFailure(code, message));
    }

    private static string NormalizeFailureCode(string code)
    {
        if (string.IsNullOrWhiteSpace(code) || code.Length > 64 ||
            code.Any(character => character is not (>= 'a' and <= 'z') and not '_'))
        {
            return SpeedTestFailureCodes.ProviderFailed;
        }

        return code;
    }

    private static string NormalizeMessage(string message)
    {
        var normalized = string.IsNullOrWhiteSpace(message)
            ? "The provider could not complete the speed test."
            : new string(message.Where(character => !char.IsControl(character)).ToArray()).Trim();
        return normalized.Length <= 240 ? normalized : normalized[..240];
    }
}
