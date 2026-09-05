using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Options;
using SpeedtestDashboard.Core.History;
using SpeedtestDashboard.Core.Providers;
using SpeedtestDashboard.Core.Tests;

namespace SpeedtestDashboard.Api.Endpoints;

public static class TestEndpoints
{
    public static IEndpointRouteBuilder MapTestEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/tests", CreateTestAsync)
            .WithName("CreateSpeedTest")
            .WithTags("Tests")
            .Produces<CreateTestResponse>(StatusCodes.Status202Accepted)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

        endpoints.MapGet("/api/tests/{jobId:guid}", GetTestAsync)
            .WithName("GetSpeedTest")
            .WithTags("Tests")
            .Produces<SpeedTestJobResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        endpoints.MapPost("/api/tests/{jobId:guid}/cancel", CancelTest)
            .WithName("CancelSpeedTest")
            .WithTags("Tests")
            .Produces<SpeedTestJobResponse>(StatusCodes.Status202Accepted)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        endpoints.MapGet("/api/tests/{jobId:guid}/events", GetTestEvents)
            .WithName("GetSpeedTestEvents")
            .WithTags("Tests")
            .Produces(StatusCodes.Status200OK, contentType: "text/event-stream")
            .ProducesProblem(StatusCodes.Status404NotFound);

        return endpoints;
    }

    private static async Task<IResult> CreateTestAsync(
        CreateTestRequest request,
        ISpeedTestProviderRegistry registry,
        ISpeedTestJobStore jobStore,
        ISpeedTestQueue queue,
        IOptions<SpeedTestOptions> options,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        if (!TryMapRequest(request, out var speedTestRequest, out var validationMessage))
        {
            return ProblemResponses.BadRequest(SpeedTestFailureCodes.InvalidRequest, validationMessage);
        }

        if (!registry.TryGet(speedTestRequest.ProviderId, out var provider))
        {
            return ProblemResponses.NotFound(
                SpeedTestFailureCodes.ProviderNotFound,
                "The requested speed-test provider is not registered.");
        }

        if (speedTestRequest.ServerId is not null &&
            !provider.Capabilities.HasFlag(ProviderCapabilities.ServerSelection))
        {
            return ProblemResponses.Conflict(
                SpeedTestFailureCodes.CapabilityNotSupported,
                "This provider does not support explicit server selection.");
        }


        if (provider is ISpeedTestRequestValidator validator)
        {
            var validation = validator.ValidateRequest(speedTestRequest);
            if (!validation.IsValid)
            {
                return ProblemResponses.BadRequest(
                    validation.Code ?? SpeedTestFailureCodes.InvalidRequest,
                    validation.Message ?? "The provider-specific request is invalid.");
            }
        }

        ProviderHealth health;
        try
        {
            health = await provider.CheckHealthAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            health = new ProviderHealth(
                provider.Id,
                ProviderHealthState.Unavailable,
                Version: null,
                DateTimeOffset.UtcNow,
                Message: null);
        }

        if (health.State == ProviderHealthState.Unavailable)
        {
            return ProblemResponses.Conflict(
                SpeedTestFailureCodes.ProviderUnavailable,
                "The requested speed-test provider is unavailable.");
        }

        SpeedTestJob job;
        try
        {
            job = jobStore.Create(speedTestRequest);
        }
        catch (SpeedTestPersistenceException)
        {
            return ProblemResponses.Internal(
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
                return ProblemResponses.Internal(
                    SpeedTestFailureCodes.PersistenceFailed,
                    "The rejected speed-test job could not be removed from durable storage.");
            }
            return ProblemResponses.TooManyRequests(
                SpeedTestFailureCodes.QueueFull,
                "The speed-test queue is full. Try again shortly.",
                options.Value.QueueFullRetryAfterSeconds,
                context);
        }

        var response = CreateTestResponse.From(job);
        return Results.Accepted(response.ResourceUrl, response);
    }

    private static async Task<IResult> GetTestAsync(
        Guid jobId,
        ISpeedTestJobStore jobStore,
        ISpeedTestHistoryStore history,
        CancellationToken cancellationToken)
    {
        if (jobStore.TryGet(jobId, out var activeOrRecentJob))
        {
            return Results.Ok(SpeedTestJobResponse.From(activeOrRecentJob));
        }

        var persistedJob = await history.GetTerminalJobAsync(jobId, cancellationToken);
        return persistedJob is null
            ? ProblemResponses.NotFound("job_not_found", "The requested speed-test job was not found.")
            : Results.Ok(SpeedTestJobResponse.From(persistedJob));
    }

    private static IResult CancelTest(
        Guid jobId,
        ISpeedTestJobStore jobStore,
        ISpeedTestCancellationRegistry cancellationRegistry)
    {
        if (!jobStore.TryGet(jobId, out var job))
        {
            return ProblemResponses.NotFound("job_not_found", "The requested speed-test job was not found.");
        }

        if (job.IsTerminal)
        {
            return ProblemResponses.Conflict("job_terminal", "A terminal speed-test job cannot be cancelled.");
        }

        var result = jobStore.Transition(
            jobId,
            SpeedTestJobStatus.Cancelled,
            "Cancelled",
            out var cancelledJob);
        if (result != JobMutationResult.Success || cancelledJob is null)
        {
            if (result == JobMutationResult.PersistenceFailed)
            {
                cancellationRegistry.TryCancel(jobId);
                return ProblemResponses.Internal(
                    SpeedTestFailureCodes.PersistenceFailed,
                    "Cancellation could not be saved to durable history.");
            }

            return result == JobMutationResult.NotFound
                ? ProblemResponses.NotFound("job_not_found", "The requested speed-test job was not found.")
                : ProblemResponses.Conflict("job_terminal", "The speed-test job completed before cancellation could be applied.");
        }

        cancellationRegistry.TryCancel(jobId);
        return Results.Accepted($"/api/tests/{jobId}", SpeedTestJobResponse.From(cancelledJob));
    }

    private static IResult GetTestEvents(
        Guid jobId,
        ISpeedTestJobStore jobStore,
        IOptions<SpeedTestOptions> options,
        CancellationToken cancellationToken)
    {
        if (!jobStore.TryGet(jobId, out _))
        {
            return ProblemResponses.NotFound("job_not_found", "The requested speed-test job was not found.");
        }

        var heartbeatInterval = TimeSpan.FromSeconds(options.Value.SseHeartbeatSeconds);
        return Results.ServerSentEvents(GetEvents(jobId, jobStore, heartbeatInterval, cancellationToken));
    }

    private static async IAsyncEnumerable<SseItem<SpeedTestEventResponse>> GetEvents(
        Guid jobId,
        ISpeedTestJobStore jobStore,
        TimeSpan heartbeatInterval,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var update in jobStore.SubscribeAsync(jobId, heartbeatInterval, cancellationToken))
        {
            var eventType = update.Type.ToString().ToLowerInvariant();
            yield return new SseItem<SpeedTestEventResponse>(
                new SpeedTestEventResponse(
                    update.Version,
                    update.EmittedAtUtc,
                    update.Job is null ? null : SpeedTestJobResponse.From(update.Job)),
                eventType)
            {
                EventId = update.Version.ToString(System.Globalization.CultureInfo.InvariantCulture)
            };
        }
    }

    private static bool TryMapRequest(
        CreateTestRequest request,
        out SpeedTestRequest mapped,
        out string validationMessage)
    {
        mapped = null!;
        validationMessage = string.Empty;
        if (!ProviderId.TryParse(request.ProviderId, out var providerId))
        {
            validationMessage = "Provider ID must be a lowercase identifier between 1 and 32 characters.";
            return false;
        }

        var serverId = string.IsNullOrWhiteSpace(request.ServerId) ? null : request.ServerId.Trim();
        if (serverId?.Length > 128)
        {
            validationMessage = "Server ID must not exceed 128 characters.";
            return false;
        }

        mapped = new SpeedTestRequest(providerId, serverId);
        return true;
    }
}

public sealed record CreateTestRequest(
    string? ProviderId,
    string? ServerId);

public sealed record CreateTestResponse(
    Guid Id,
    string ProviderId,
    string Status,
    string Stage,
    long Version,
    DateTimeOffset CreatedAtUtc,
    string ResourceUrl,
    string EventsUrl)
{
    public static CreateTestResponse From(SpeedTestJob job) => new(
        job.Id,
        job.Request.ProviderId.Value,
        JobStatus(job.Status),
        job.Stage,
        job.Version,
        job.CreatedAtUtc,
        $"/api/tests/{job.Id}",
        $"/api/tests/{job.Id}/events");

    private static string JobStatus(SpeedTestJobStatus status) => status == SpeedTestJobStatus.ProcessingResult
        ? "processingResult"
        : status.ToString().ToLowerInvariant();
}

public sealed record SpeedTestJobResponse(
    Guid Id,
    string ProviderId,
    string Status,
    string Stage,
    long Version,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? StartedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    NetworkIdentityResponse? EgressIdentity,
    SpeedTestResultResponse? Result,
    SpeedTestFailureResponse? Failure)
{
    public static SpeedTestJobResponse From(SpeedTestJob job) => new(
        job.Id,
        job.Request.ProviderId.Value,
        job.Status == SpeedTestJobStatus.ProcessingResult
            ? "processingResult"
            : job.Status.ToString().ToLowerInvariant(),
        job.Stage,
        job.Version,
        job.CreatedAtUtc,
        job.StartedAtUtc,
        job.CompletedAtUtc,
        job.EgressIdentity is null ? null : NetworkIdentityResponse.From(job.EgressIdentity),
        job.Result is null ? null : SpeedTestResultResponse.From(job.Result),
        job.Failure is null ? null : new SpeedTestFailureResponse(job.Failure.Code, job.Failure.Message));
}

public sealed record SpeedTestResultResponse(
    string ProviderId,
    string? ServerId,
    string? ServerName,
    string? ServerLocation,
    decimal? DownloadMbps,
    decimal? UploadMbps,
    decimal? LatencyMilliseconds,
    decimal? JitterMilliseconds,
    decimal? PacketLossPercent,
    string? ResultUrl)
{
    public static SpeedTestResultResponse From(SpeedTestResult result) => new(
        result.ProviderId.Value,
        result.ServerId,
        result.ServerName,
        result.ServerLocation,
        result.DownloadMbps,
        result.UploadMbps,
        result.LatencyMilliseconds,
        result.JitterMilliseconds,
        result.PacketLossPercent,
        result.ResultUrl);
}

public sealed record SpeedTestFailureResponse(string Code, string Message);

public sealed record SpeedTestEventResponse(
    long Version,
    DateTimeOffset EmittedAtUtc,
    SpeedTestJobResponse? Job);
