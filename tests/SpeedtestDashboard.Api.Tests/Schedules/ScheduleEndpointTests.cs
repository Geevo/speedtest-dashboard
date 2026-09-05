using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using SpeedtestDashboard.Api.Endpoints;
using SpeedtestDashboard.Core.Providers;
using SpeedtestDashboard.Core.Schedules;

namespace SpeedtestDashboard.Api.Tests.Schedules;

public sealed class ScheduleEndpointTests : IClassFixture<DashboardWebApplicationFactory>
{
    private readonly DashboardWebApplicationFactory _factory;

    public ScheduleEndpointTests(DashboardWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task CreateOneOffSchedule_PersistsAndComputesRunAtUtc()
    {
        using var client = _factory.CreateClient();
        var runAtLocal = DateTime.UtcNow.AddDays(10);

        var response = await client.PostAsJsonAsync("/api/schedules", new
        {
            name = "One-off backup check",
            providerId = "librespeed",
            serverId = (string?)null,
            recurrenceKind = "oneOff",
            runAtLocal,
            timeZoneId = "Europe/London",
            enabled = true
        });
        var body = await response.Content.ReadFromJsonAsync<ScheduleResponse>();

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(body);
        Assert.Equal("oneOff", body!.RecurrenceKind);
        Assert.NotNull(body.NextRunAtUtc);
        Assert.False(body.Completed);
    }

    [Fact]
    public async Task CreateRecurringSchedule_ComputesAFutureNextRun()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/schedules", new
        {
            name = "Every 30 minutes",
            providerId = "librespeed",
            recurrenceKind = "interval",
            intervalMinutes = 30,
            timeZoneId = "Europe/London",
            enabled = true
        });
        var body = await response.Content.ReadFromJsonAsync<ScheduleResponse>();

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(body);
        Assert.Equal("interval", body!.RecurrenceKind);
        Assert.True(body.NextRunAtUtc > DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task EditSchedule_RecalculatesNextRun()
    {
        using var client = _factory.CreateClient();
        var created = await CreateIntervalScheduleAsync(client, intervalMinutes: 60);

        var updateResponse = await client.PutAsJsonAsync($"/api/schedules/{created.Id}", new
        {
            name = created.Name,
            providerId = created.ProviderId,
            recurrenceKind = "interval",
            intervalMinutes = 15,
            timeZoneId = created.TimeZoneId,
            enabled = true
        });
        var updated = await updateResponse.Content.ReadFromJsonAsync<ScheduleResponse>();

        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
        Assert.Equal(15, updated!.IntervalMinutes);
        Assert.NotEqual(created.NextRunAtUtc, updated.NextRunAtUtc);
    }

    [Fact]
    public async Task DisableThenEnableSchedule_ClearsAndRecalculatesNextRun()
    {
        using var client = _factory.CreateClient();
        var created = await CreateIntervalScheduleAsync(client, intervalMinutes: 30);

        using var disableResponse = await client.PostAsync($"/api/schedules/{created.Id}/disable", null);
        var disabled = await disableResponse.Content.ReadFromJsonAsync<ScheduleResponse>();

        using var enableResponse = await client.PostAsync($"/api/schedules/{created.Id}/enable", null);
        var enabled = await enableResponse.Content.ReadFromJsonAsync<ScheduleResponse>();

        Assert.False(disabled!.Enabled);
        Assert.Null(disabled.NextRunAtUtc);
        Assert.True(enabled!.Enabled);
        Assert.NotNull(enabled.NextRunAtUtc);
    }

    [Fact]
    public async Task DeleteSchedule_RemovesItAndReturns404OnSecondDelete()
    {
        using var client = _factory.CreateClient();
        var created = await CreateIntervalScheduleAsync(client, intervalMinutes: 30);

        using var firstDelete = await client.DeleteAsync($"/api/schedules/{created.Id}");
        using var secondDelete = await client.DeleteAsync($"/api/schedules/{created.Id}");
        using var getAfterDelete = await client.GetAsync($"/api/schedules/{created.Id}");

        Assert.Equal(HttpStatusCode.NoContent, firstDelete.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, secondDelete.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, getAfterDelete.StatusCode);
    }

    [Fact]
    public async Task CreateSchedule_WithUnknownProvider_ReturnsBadRequest()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/schedules", new
        {
            name = "Bad provider",
            providerId = "not-a-real-provider",
            recurrenceKind = "interval",
            intervalMinutes = 30,
            timeZoneId = "Europe/London",
            enabled = true
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateSchedule_WithAnInvalidServerId_ReturnsBadRequest()
    {
        using var client = _factory.CreateClient();

        // LibreSpeed server IDs must be positive integers without a leading zero; "0" fails
        // the same provider-side validation manual test creation uses.
        var response = await client.PostAsJsonAsync("/api/schedules", new
        {
            name = "Invalid server id",
            providerId = "librespeed",
            serverId = "0",
            recurrenceKind = "interval",
            intervalMinutes = 30,
            timeZoneId = "Europe/London",
            enabled = true
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateSchedule_WithPastOneOffTime_ReturnsBadRequest()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/schedules", new
        {
            name = "Already passed",
            providerId = "librespeed",
            recurrenceKind = "oneOff",
            runAtLocal = DateTime.UtcNow.AddDays(-1),
            timeZoneId = "Europe/London",
            enabled = true
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ListSchedules_ReturnsCreatedSchedules()
    {
        using var client = _factory.CreateClient();
        var created = await CreateIntervalScheduleAsync(client, intervalMinutes: 45);

        var list = await client.GetFromJsonAsync<JsonElement>("/api/schedules");

        Assert.Contains(
            list.EnumerateArray(),
            element => element.GetProperty("id").GetGuid() == created.Id);
    }

    [Fact]
    public void MissedOneOffSchedule_IsNotReportedAsCompleted()
    {
        var schedule = new SpeedTestSchedule(
            Guid.NewGuid(),
            "Missed check",
            ProviderId.Parse("librespeed"),
            ServerId: null,
            ScheduleRecurrenceKind.OneOff,
            RunAtUtc: DateTimeOffset.UtcNow.AddMinutes(-10),
            IntervalMinutes: null,
            TimeOfDayMinutes: null,
            DayOfWeek: null,
            TimeZoneId: "Etc/UTC",
            Enabled: true,
            CreatedAtUtc: DateTimeOffset.UtcNow.AddHours(-1),
            UpdatedAtUtc: DateTimeOffset.UtcNow.AddHours(-1),
            LastRunAtUtc: DateTimeOffset.UtcNow,
            NextRunAtUtc: null,
            LastJobId: null,
            LastRunStatus: "skipped");

        var response = ScheduleResponse.From(schedule);

        Assert.False(response.Completed);
    }

    private static async Task<ScheduleResponse> CreateIntervalScheduleAsync(HttpClient client, int intervalMinutes)
    {
        var response = await client.PostAsJsonAsync("/api/schedules", new
        {
            name = $"Every {intervalMinutes} minutes",
            providerId = "librespeed",
            recurrenceKind = "interval",
            intervalMinutes,
            timeZoneId = "Europe/London",
            enabled = true
        });
        var body = await response.Content.ReadFromJsonAsync<ScheduleResponse>();
        Assert.NotNull(body);
        return body!;
    }
}
