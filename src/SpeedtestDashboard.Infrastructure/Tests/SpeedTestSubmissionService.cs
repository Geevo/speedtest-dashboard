using Microsoft.Extensions.Options;
using SpeedtestDashboard.Core.History;
using SpeedtestDashboard.Core.Providers;
using SpeedtestDashboard.Core.Tests;

namespace SpeedtestDashboard.Infrastructure.Tests;

public sealed class SpeedTestSubmissionService(
    ISpeedTestProviderRegistry registry,
    ISpeedTestJobStore jobStore,
    ISpeedTestQueue queue,
    IOptions<SpeedTestOptions> options) : ISpeedTestSubmissionService
{
    public async Task<SpeedTestSubmissionResult> SubmitAsync(SpeedTestRequest request, CancellationToken cancellationToken)
    {
        var validation = SpeedTestRequestValidation.Validate(request, registry);
        if (!validation.IsValid)
        {
            var outcome = validation.Code switch
            {
                SpeedTestFailureCodes.ProviderNotFound => SpeedTestSubmissionOutcome.ProviderNotFound,
                SpeedTestFailureCodes.CapabilityNotSupported => SpeedTestSubmissionOutcome.CapabilityNotSupported,
                _ => SpeedTestSubmissionOutcome.InvalidRequest
            };
            return SpeedTestSubmissionResult.Failed(outcome, validation.Code!, validation.Message!);
        }

        registry.TryGet(request.ProviderId, out var provider);

        ProviderHealth health;
        try
        {
            health = await provider!.CheckHealthAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            health = new ProviderHealth(
                provider!.Descriptor.Id,
                ProviderHealthState.Unavailable,
                Version: null,
                DateTimeOffset.UtcNow,
                Message: null);
        }

        if (health.State == ProviderHealthState.Unavailable)
        {
            return SpeedTestSubmissionResult.Failed(
                SpeedTestSubmissionOutcome.ProviderUnavailable,
                SpeedTestFailureCodes.ProviderUnavailable,
                "The requested speed-test provider is unavailable.");
        }

        SpeedTestJob job;
        try
        {
            job = jobStore.Create(request);
        }
        catch (SpeedTestPersistenceException)
        {
            return SpeedTestSubmissionResult.Failed(
                SpeedTestSubmissionOutcome.PersistenceFailed,
                SpeedTestFailureCodes.PersistenceFailed,
                "The speed-test job could not be saved to durable storage.");
        }

        if (!queue.TryEnqueue(job.Id))
        {
            try
            {
                jobStore.TryRemoveQueued(job.Id);
            }
            catch (SpeedTestPersistenceException)
            {
                return SpeedTestSubmissionResult.Failed(
                    SpeedTestSubmissionOutcome.PersistenceFailed,
                    SpeedTestFailureCodes.PersistenceFailed,
                    "The rejected speed-test job could not be removed from durable storage.");
            }

            return SpeedTestSubmissionResult.Failed(
                SpeedTestSubmissionOutcome.QueueFull,
                SpeedTestFailureCodes.QueueFull,
                "The speed-test queue is full. Try again shortly.",
                options.Value.QueueFullRetryAfterSeconds);
        }

        return SpeedTestSubmissionResult.Created(job);
    }
}
