using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using SpeedtestDashboard.Api.ApiKeys;
using SpeedtestDashboard.Api.Authentication;
using SpeedtestDashboard.Core.History;
using SpeedtestDashboard.Core.Providers;
using SpeedtestDashboard.Core.Tests;
using SpeedtestDashboard.Infrastructure.ApiKeys;

namespace SpeedtestDashboard.Api.Endpoints;

public static class TestEndpoints
{
    private const int MaximumIdempotencyKeyLength = 128;

    public static IEndpointRouteBuilder MapTestEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/tests", CreateTestAsync)
            .RequireCsrf()
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
            .RequireCsrf()
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

        endpoints.MapPost("/api/v1/tests", CreateTestV1Async)
            .RequireAuthorization(ApiKeyAuthenticationDefaults.PolicyName)
            .RequireRateLimiting(ApiKeyAuthenticationDefaults.WriteRateLimiterPolicy)
            .WithName("CreateSpeedTestV1")
            .WithTags("API v1")
            .Produces<CreateTestResponse>(StatusCodes.Status202Accepted)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

        endpoints.MapGet("/api/v1/tests/{jobId:guid}", GetTestAsync)
            .RequireAuthorization(ApiKeyAuthenticationDefaults.PolicyName)
            .RequireRateLimiting(ApiKeyAuthenticationDefaults.ReadRateLimiterPolicy)
            .WithName("GetSpeedTestV1")
            .WithTags("API v1")
            .Produces<SpeedTestJobResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound);

        endpoints.MapPost("/api/v1/tests/{jobId:guid}/cancel", CancelTest)
            .RequireAuthorization(ApiKeyAuthenticationDefaults.PolicyName)
            .RequireRateLimiting(ApiKeyAuthenticationDefaults.WriteRateLimiterPolicy)
            .WithName("CancelSpeedTestV1")
            .WithTags("API v1")
            .Produces<SpeedTestJobResponse>(StatusCodes.Status202Accepted)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        endpoints.MapGet("/api/v1/tests/{jobId:guid}/events", GetTestEvents)
            .RequireAuthorization(ApiKeyAuthenticationDefaults.PolicyName)
            .RequireRateLimiting(ApiKeyAuthenticationDefaults.ReadRateLimiterPolicy)
            .WithName("GetSpeedTestEventsV1")
            .WithTags("API v1")
            .Produces(StatusCodes.Status200OK, contentType: "text/event-stream")
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return endpoints;
    }

    private static async Task<IResult> CreateTestAsync(
        CreateTestRequest request,
        ISpeedTestSubmissionService submissionService,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        if (!TryMapRequest(request, out var speedTestRequest, out var validationMessage))
        {
            return ProblemResponses.BadRequest(SpeedTestFailureCodes.InvalidRequest, validationMessage);
        }

        var result = await submissionService.SubmitAsync(speedTestRequest, cancellationToken);
        if (result.Outcome != SpeedTestSubmissionOutcome.Created)
        {
            return MapSubmissionFailure(result, context);
        }

        var response = CreateTestResponse.From(result.Job!);
        return Results.Accepted(response.ResourceUrl, response);
    }

    private static async Task<IResult> CreateTestV1Async(
        CreateTestRequest request,
        HttpRequest httpRequest,
        ISpeedTestSubmissionService submissionService,
        ISpeedTestJobStore jobStore,
        ISpeedTestHistoryStore history,
        IApiIdempotencyStore idempotencyStore,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        const string basePath = "/api/v1/tests";

        if (!TryMapRequest(request, out var speedTestRequest, out var validationMessage))
        {
            return ProblemResponses.BadRequest(SpeedTestFailureCodes.InvalidRequest, validationMessage);
        }

        var idempotencyKey = httpRequest.Headers.TryGetValue("Idempotency-Key", out var headerValues)
            ? headerValues.ToString()
            : null;
        if (idempotencyKey is { Length: > MaximumIdempotencyKeyLength })
        {
            return ProblemResponses.BadRequest(
                "invalid_idempotency_key",
                $"Idempotency-Key must be at most {MaximumIdempotencyKeyLength} characters.");
        }

        using var idempotencyLease = string.IsNullOrEmpty(idempotencyKey)
            ? null
            : await idempotencyStore.AcquireAsync(idempotencyKey, cancellationToken);

        string? requestHash = null;
        if (!string.IsNullOrEmpty(idempotencyKey))
        {
            requestHash = ComputeRequestHash(request);
            var existing = await idempotencyStore.TryGetAsync(idempotencyKey, cancellationToken);
            if (existing is not null)
            {
                if (!string.Equals(existing.RequestHash, requestHash, StringComparison.Ordinal))
                {
                    return ProblemResponses.Conflict(
                        "idempotency_key_conflict",
                        "This idempotency key was already used with a different request.");
                }

                var existingJob = await FindJobAsync(existing.JobId, jobStore, history, cancellationToken);
                if (existingJob is not null)
                {
                    var cachedResponse = CreateTestResponse.From(existingJob, basePath);
                    return Results.Accepted(cachedResponse.ResourceUrl, cachedResponse);
                }
            }
        }

        var result = await submissionService.SubmitAsync(speedTestRequest, cancellationToken);
        if (result.Outcome != SpeedTestSubmissionOutcome.Created)
        {
            return MapSubmissionFailure(result, context);
        }

        if (!string.IsNullOrEmpty(idempotencyKey))
        {
            // Admission has already succeeded; a disconnected caller must not erase deduplication.
            await idempotencyStore.SaveAsync(idempotencyKey, requestHash!, result.Job!.Id, CancellationToken.None);
        }

        var response = CreateTestResponse.From(result.Job!, basePath);
        return Results.Accepted(response.ResourceUrl, response);
    }

    private static IResult MapSubmissionFailure(SpeedTestSubmissionResult result, HttpContext context) => result.Outcome switch
    {
        SpeedTestSubmissionOutcome.ProviderNotFound => ProblemResponses.NotFound(result.FailureCode!, result.FailureMessage!),
        SpeedTestSubmissionOutcome.CapabilityNotSupported => ProblemResponses.Conflict(result.FailureCode!, result.FailureMessage!),
        SpeedTestSubmissionOutcome.ProviderUnavailable => ProblemResponses.Conflict(result.FailureCode!, result.FailureMessage!),
        SpeedTestSubmissionOutcome.InvalidRequest => ProblemResponses.BadRequest(result.FailureCode!, result.FailureMessage!),
        SpeedTestSubmissionOutcome.PersistenceFailed => ProblemResponses.Internal(result.FailureCode!, result.FailureMessage!),
        SpeedTestSubmissionOutcome.QueueFull => ProblemResponses.TooManyRequests(
            result.FailureCode!, result.FailureMessage!, result.RetryAfterSeconds!.Value, context),
        _ => ProblemResponses.Internal(SpeedTestFailureCodes.InternalError, "The speed test could not be created.")
    };

    private static async Task<IResult> GetTestAsync(
        Guid jobId,
        ISpeedTestJobStore jobStore,
        ISpeedTestHistoryStore history,
        CancellationToken cancellationToken)
    {
        var job = await FindJobAsync(jobId, jobStore, history, cancellationToken);
        return job is null
            ? ProblemResponses.NotFound("job_not_found", "The requested speed-test job was not found.")
            : Results.Ok(SpeedTestJobResponse.From(job));
    }

    private static IResult CancelTest(
        HttpContext context,
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
        var basePath = context.Request.Path.StartsWithSegments("/api/v1") ? "/api/v1/tests" : "/api/tests";
        return Results.Accepted($"{basePath}/{jobId}", SpeedTestJobResponse.From(cancelledJob));
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

    private static async Task<SpeedTestJob?> FindJobAsync(
        Guid jobId,
        ISpeedTestJobStore jobStore,
        ISpeedTestHistoryStore history,
        CancellationToken cancellationToken)
    {
        if (jobStore.TryGet(jobId, out var activeOrRecentJob))
        {
            return activeOrRecentJob;
        }

        return await history.GetTerminalJobAsync(jobId, cancellationToken);
    }

    private static string ComputeRequestHash(CreateTestRequest request)
    {
        var canonical = $"{request.ProviderId?.Trim().ToLowerInvariant()} {request.ServerId?.Trim() ?? string.Empty}";
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        return Convert.ToHexString(bytes);
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
    public static CreateTestResponse From(SpeedTestJob job, string basePath = "/api/tests") => new(
        job.Id,
        job.Request.ProviderId.Value,
        JobStatus(job.Status),
        job.Stage,
        job.Version,
        job.CreatedAtUtc,
        $"{basePath}/{job.Id}",
        $"{basePath}/{job.Id}/events");

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
