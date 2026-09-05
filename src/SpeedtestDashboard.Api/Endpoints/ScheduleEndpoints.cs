using SpeedtestDashboard.Api.ApiKeys;
using SpeedtestDashboard.Api.Authentication;
using SpeedtestDashboard.Core.Providers;
using SpeedtestDashboard.Core.Schedules;
using SpeedtestDashboard.Core.Tests;

namespace SpeedtestDashboard.Api.Endpoints;

public static class ScheduleEndpoints
{
    public static IEndpointRouteBuilder MapScheduleEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/schedules", ListAsync)
            .WithName("GetSchedules")
            .WithTags("Schedules")
            .Produces<IReadOnlyList<ScheduleResponse>>();

        endpoints.MapGet("/api/schedules/{id:guid}", GetAsync)
            .WithName("GetSchedule")
            .WithTags("Schedules")
            .Produces<ScheduleResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        endpoints.MapPost("/api/schedules", CreateAsync)
            .RequireCsrf()
            .WithName("CreateSchedule")
            .WithTags("Schedules")
            .Produces<ScheduleResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        endpoints.MapPut("/api/schedules/{id:guid}", UpdateAsync)
            .RequireCsrf()
            .WithName("UpdateSchedule")
            .WithTags("Schedules")
            .Produces<ScheduleResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound);

        endpoints.MapDelete("/api/schedules/{id:guid}", DeleteAsync)
            .RequireCsrf()
            .WithName("DeleteSchedule")
            .WithTags("Schedules")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound);

        endpoints.MapPost("/api/schedules/{id:guid}/enable", EnableAsync)
            .RequireCsrf()
            .WithName("EnableSchedule")
            .WithTags("Schedules")
            .Produces<ScheduleResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        endpoints.MapPost("/api/schedules/{id:guid}/disable", DisableAsync)
            .RequireCsrf()
            .WithName("DisableSchedule")
            .WithTags("Schedules")
            .Produces<ScheduleResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        endpoints.MapGet("/api/v1/schedules", ListAsync)
            .RequireAuthorization(ApiKeyAuthenticationDefaults.PolicyName)
            .RequireRateLimiting(ApiKeyAuthenticationDefaults.ReadRateLimiterPolicy)
            .WithName("GetSchedulesV1")
            .WithTags("API v1")
            .Produces<IReadOnlyList<ScheduleResponse>>()
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        endpoints.MapGet("/api/v1/schedules/{id:guid}", GetAsync)
            .RequireAuthorization(ApiKeyAuthenticationDefaults.PolicyName)
            .RequireRateLimiting(ApiKeyAuthenticationDefaults.ReadRateLimiterPolicy)
            .WithName("GetScheduleV1")
            .WithTags("API v1")
            .Produces<ScheduleResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return endpoints;
    }

    private static async Task<IResult> ListAsync(ISpeedTestScheduleStore store, CancellationToken cancellationToken)
    {
        var schedules = await store.ListAsync(cancellationToken);
        return Results.Ok(schedules.Select(ScheduleResponse.From).ToArray());
    }

    private static async Task<IResult> GetAsync(Guid id, ISpeedTestScheduleStore store, CancellationToken cancellationToken)
    {
        var schedule = await store.GetAsync(id, cancellationToken);
        return schedule is null
            ? ProblemResponses.NotFound("schedule_not_found", "The requested schedule was not found.")
            : Results.Ok(ScheduleResponse.From(schedule));
    }

    private static async Task<IResult> CreateAsync(
        ScheduleRequest request,
        ISpeedTestScheduleStore store,
        ISpeedTestProviderRegistry registry,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        if (!TryBuildDraft(request, registry, now, out var draft, out var error))
        {
            return ProblemResponses.BadRequest("invalid_schedule", error);
        }

        var timeZone = TimeZoneInfo.FindSystemTimeZoneById(draft.TimeZoneId);
        DateTimeOffset? nextRun = draft.Enabled ? ScheduleRecurrenceCalculator.ComputeInitialNextRun(draft, now, timeZone) : null;
        var schedule = await store.CreateAsync(draft, nextRun, cancellationToken);
        return Results.Created($"/api/schedules/{schedule.Id}", ScheduleResponse.From(schedule));
    }

    private static async Task<IResult> UpdateAsync(
        Guid id,
        ScheduleRequest request,
        ISpeedTestScheduleStore store,
        ISpeedTestProviderRegistry registry,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        if (!TryBuildDraft(request, registry, now, out var draft, out var error))
        {
            return ProblemResponses.BadRequest("invalid_schedule", error);
        }

        var timeZone = TimeZoneInfo.FindSystemTimeZoneById(draft.TimeZoneId);
        DateTimeOffset? nextRun = draft.Enabled ? ScheduleRecurrenceCalculator.ComputeInitialNextRun(draft, now, timeZone) : null;
        var schedule = await store.UpdateAsync(id, draft, nextRun, cancellationToken);
        return schedule is null
            ? ProblemResponses.NotFound("schedule_not_found", "The requested schedule was not found.")
            : Results.Ok(ScheduleResponse.From(schedule));
    }

    private static async Task<IResult> DeleteAsync(Guid id, ISpeedTestScheduleStore store, CancellationToken cancellationToken)
    {
        var deleted = await store.DeleteAsync(id, cancellationToken);
        return deleted
            ? Results.NoContent()
            : ProblemResponses.NotFound("schedule_not_found", "The requested schedule was not found.");
    }

    private static async Task<IResult> EnableAsync(
        Guid id,
        ISpeedTestScheduleStore store,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var existing = await store.GetAsync(id, cancellationToken);
        if (existing is null)
        {
            return ProblemResponses.NotFound("schedule_not_found", "The requested schedule was not found.");
        }

        var now = timeProvider.GetUtcNow();
        DateTimeOffset? nextRun;
        if (existing.RecurrenceKind == ScheduleRecurrenceKind.OneOff)
        {
            // Re-enabling never replays a one-off run whose moment has already passed.
            nextRun = existing.RunAtUtc is { } runAtUtc && runAtUtc > now ? runAtUtc : null;
        }
        else
        {
            var timeZone = TryResolveTimeZone(existing.TimeZoneId);
            nextRun = timeZone is null ? null : ScheduleRecurrenceCalculator.ComputeNextRunAfter(existing, now, timeZone);
        }

        var schedule = await store.SetEnabledAsync(id, enabled: true, nextRun, cancellationToken);
        return schedule is null
            ? ProblemResponses.NotFound("schedule_not_found", "The requested schedule was not found.")
            : Results.Ok(ScheduleResponse.From(schedule));
    }

    private static async Task<IResult> DisableAsync(Guid id, ISpeedTestScheduleStore store, CancellationToken cancellationToken)
    {
        var schedule = await store.SetEnabledAsync(id, enabled: false, nextRunAtUtc: null, cancellationToken);
        return schedule is null
            ? ProblemResponses.NotFound("schedule_not_found", "The requested schedule was not found.")
            : Results.Ok(ScheduleResponse.From(schedule));
    }

    private static bool TryBuildDraft(
        ScheduleRequest request,
        ISpeedTestProviderRegistry registry,
        DateTimeOffset now,
        out ScheduleDraft draft,
        out string error)
    {
        draft = null!;
        error = string.Empty;

        var name = request.Name?.Trim();
        if (string.IsNullOrWhiteSpace(name) || name.Length > 120)
        {
            error = "Name must be 1-120 characters.";
            return false;
        }

        if (!ProviderId.TryParse(request.ProviderId, out var providerId))
        {
            error = "Provider ID must be a lowercase identifier between 1 and 32 characters.";
            return false;
        }

        var serverId = string.IsNullOrWhiteSpace(request.ServerId) ? null : request.ServerId.Trim();
        if (serverId?.Length > 128)
        {
            error = "Server ID must not exceed 128 characters.";
            return false;
        }

        if (!TryParseRecurrenceKind(request.RecurrenceKind, out var kind))
        {
            error = "Recurrence kind must be oneOff, interval, daily, or weekly.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(request.TimeZoneId))
        {
            error = "A time zone is required.";
            return false;
        }

        var timeZone = TryResolveTimeZone(request.TimeZoneId);
        if (timeZone is null)
        {
            error = "The time zone is not recognized.";
            return false;
        }

        DateTimeOffset? runAtUtc = null;
        int? intervalMinutes = null;
        int? timeOfDayMinutes = null;
        DayOfWeek? dayOfWeek = null;

        switch (kind)
        {
            case ScheduleRecurrenceKind.OneOff:
                if (request.RunAtLocal is null)
                {
                    error = "A one-off schedule requires a run date and time.";
                    return false;
                }

                var local = DateTime.SpecifyKind(request.RunAtLocal.Value, DateTimeKind.Unspecified);
                if (timeZone.IsInvalidTime(local))
                {
                    error = "The run date and time falls in a daylight-saving gap in the selected time zone.";
                    return false;
                }

                runAtUtc = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local, timeZone), TimeSpan.Zero);
                if (runAtUtc <= now)
                {
                    error = "The one-off run date and time must be in the future.";
                    return false;
                }

                break;
            case ScheduleRecurrenceKind.Interval:
                if (request.IntervalMinutes is not (>= 1 and <= 10_080))
                {
                    error = "Interval minutes must be between 1 and 10080 (one week).";
                    return false;
                }

                intervalMinutes = request.IntervalMinutes;
                break;
            case ScheduleRecurrenceKind.Daily:
                if (request.TimeOfDayMinutes is not (>= 0 and <= 1439))
                {
                    error = "Time of day must be between 0 and 1439 minutes.";
                    return false;
                }

                timeOfDayMinutes = request.TimeOfDayMinutes;
                break;
            case ScheduleRecurrenceKind.Weekly:
                if (request.TimeOfDayMinutes is not (>= 0 and <= 1439))
                {
                    error = "Time of day must be between 0 and 1439 minutes.";
                    return false;
                }

                if (request.DayOfWeek is not (>= 0 and <= 6))
                {
                    error = "Day of week must be between 0 (Sunday) and 6 (Saturday).";
                    return false;
                }

                timeOfDayMinutes = request.TimeOfDayMinutes;
                dayOfWeek = (DayOfWeek)request.DayOfWeek!.Value;
                break;
        }

        var candidateRequest = new SpeedTestRequest(providerId, serverId);
        var validation = SpeedTestRequestValidation.Validate(candidateRequest, registry);
        if (!validation.IsValid)
        {
            error = validation.Message!;
            return false;
        }

        draft = new ScheduleDraft(
            name, providerId, serverId, kind, runAtUtc, intervalMinutes, timeOfDayMinutes, dayOfWeek,
            request.TimeZoneId!, request.Enabled);
        return true;
    }

    private static bool TryParseRecurrenceKind(string? value, out ScheduleRecurrenceKind kind)
    {
        switch (value)
        {
            case "oneOff":
                kind = ScheduleRecurrenceKind.OneOff;
                return true;
            case "interval":
                kind = ScheduleRecurrenceKind.Interval;
                return true;
            case "daily":
                kind = ScheduleRecurrenceKind.Daily;
                return true;
            case "weekly":
                kind = ScheduleRecurrenceKind.Weekly;
                return true;
            default:
                kind = default;
                return false;
        }
    }

    private static TimeZoneInfo? TryResolveTimeZone(string timeZoneId)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        }
        catch (TimeZoneNotFoundException)
        {
            return null;
        }
        catch (InvalidTimeZoneException)
        {
            return null;
        }
    }
}

public sealed record ScheduleRequest(
    string? Name,
    string? ProviderId,
    string? ServerId,
    string? RecurrenceKind,
    DateTime? RunAtLocal,
    int? IntervalMinutes,
    int? TimeOfDayMinutes,
    int? DayOfWeek,
    string? TimeZoneId,
    bool Enabled);

public sealed record ScheduleResponse(
    Guid Id,
    string Name,
    string ProviderId,
    string? ServerId,
    string RecurrenceKind,
    DateTimeOffset? RunAtUtc,
    int? IntervalMinutes,
    int? TimeOfDayMinutes,
    int? DayOfWeek,
    string TimeZoneId,
    bool Enabled,
    bool Completed,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    DateTimeOffset? LastRunAtUtc,
    DateTimeOffset? NextRunAtUtc,
    Guid? LastJobId,
    string? LastRunStatus)
{
    public static ScheduleResponse From(SpeedTestSchedule schedule) => new(
        schedule.Id,
        schedule.Name,
        schedule.ProviderId.Value,
        schedule.ServerId,
        RecurrenceKindValue(schedule.RecurrenceKind),
        schedule.RunAtUtc,
        schedule.IntervalMinutes,
        schedule.TimeOfDayMinutes,
        schedule.DayOfWeek is null ? null : (int)schedule.DayOfWeek.Value,
        schedule.TimeZoneId,
        schedule.Enabled,
        schedule.RecurrenceKind == ScheduleRecurrenceKind.OneOff &&
            schedule.NextRunAtUtc is null &&
            string.Equals(schedule.LastRunStatus, "queued", StringComparison.Ordinal),
        schedule.CreatedAtUtc,
        schedule.UpdatedAtUtc,
        schedule.LastRunAtUtc,
        schedule.NextRunAtUtc,
        schedule.LastJobId,
        schedule.LastRunStatus);

    private static string RecurrenceKindValue(ScheduleRecurrenceKind kind) => kind switch
    {
        ScheduleRecurrenceKind.OneOff => "oneOff",
        ScheduleRecurrenceKind.Interval => "interval",
        ScheduleRecurrenceKind.Daily => "daily",
        ScheduleRecurrenceKind.Weekly => "weekly",
        _ => "unknown"
    };
}
