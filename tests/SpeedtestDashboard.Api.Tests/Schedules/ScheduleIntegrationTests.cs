using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using SpeedtestDashboard.Api.Endpoints;
using SpeedtestDashboard.Api.Tests.Orchestration;
using SpeedtestDashboard.Core.Schedules;
using SpeedtestDashboard.Infrastructure.Schedules;

namespace SpeedtestDashboard.Api.Tests.Schedules;

public sealed class ScheduleIntegrationTests
{
    [Fact]
    public async Task ScheduledOneOffTest_RunsThroughTheRealQueueAndAppearsInHistory()
    {
        var provider = new FakeSpeedTestProvider();
        using var factory = new ScheduleWebApplicationFactory(provider);
        using var client = factory.CreateClient();

        var runAtLocal = DateTime.UtcNow.AddSeconds(2);
        var createResponse = await client.PostAsJsonAsync("/api/schedules", new
        {
            name = "Integration one-off",
            providerId = "fixture",
            recurrenceKind = "oneOff",
            runAtLocal,
            timeZoneId = "Etc/UTC",
            enabled = true
        });
        var created = await createResponse.Content.ReadFromJsonAsync<ScheduleResponse>();
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);

        await Task.Delay(TimeSpan.FromSeconds(2.5));
        var worker = factory.Services.GetRequiredService<ScheduleWorker>();
        await worker.PollOnceAsync(CancellationToken.None);

        var afterPoll = await client.GetFromJsonAsync<ScheduleResponse>($"/api/schedules/{created!.Id}");
        Assert.NotNull(afterPoll?.LastJobId);
        Assert.Equal("queued", afterPoll!.LastRunStatus);
        Assert.True(afterPoll.Completed); // the one-off will not run again

        await TestWait.UntilAsync(() =>
            client.GetFromJsonAsync<SpeedTestJobResponse>($"/api/tests/{afterPoll.LastJobId}")
                .GetAwaiter().GetResult()?.Status == "completed");

        var history = await client.GetFromJsonAsync<JsonElement>("/api/history");
        Assert.Contains(
            history.GetProperty("items").EnumerateArray(),
            item => item.GetProperty("jobId").GetGuid() == afterPoll.LastJobId);
    }

    [Fact]
    public async Task Schedule_SurvivesApplicationReplacement()
    {
        var root = Path.Combine(Path.GetTempPath(), "speedtest-schedule-tests", Guid.NewGuid().ToString("N"));
        Guid scheduleId;
        using (var factory = new ScheduleWebApplicationFactory(new FakeSpeedTestProvider(), root))
        using (var client = factory.CreateClient())
        {
            var response = await client.PostAsJsonAsync("/api/schedules", new
            {
                name = "Persisted schedule",
                providerId = "fixture",
                recurrenceKind = "interval",
                intervalMinutes = 30,
                timeZoneId = "Etc/UTC",
                enabled = true
            });
            var created = await response.Content.ReadFromJsonAsync<ScheduleResponse>();
            scheduleId = created!.Id;
        }

        using (var factory = new ScheduleWebApplicationFactory(new FakeSpeedTestProvider(), root))
        using (var client = factory.CreateClient())
        {
            using var response = await client.GetAsync($"/api/schedules/{scheduleId}");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var schedule = await response.Content.ReadFromJsonAsync<ScheduleResponse>();
            Assert.Equal("Persisted schedule", schedule!.Name);
        }

        Directory.Delete(root, recursive: true);
    }

    [Fact]
    public async Task DueOccurrence_CanOnlyBeClaimedOnce()
    {
        using var factory = new ScheduleWebApplicationFactory(new FakeSpeedTestProvider());
        using var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/schedules", new
        {
            name = "Claim once",
            providerId = "fixture",
            recurrenceKind = "interval",
            intervalMinutes = 30,
            timeZoneId = "Etc/UTC",
            enabled = true
        });
        var created = await response.Content.ReadFromJsonAsync<ScheduleResponse>();
        var store = factory.Services.GetRequiredService<ISpeedTestScheduleStore>();
        var schedule = await store.GetAsync(created!.Id, CancellationToken.None);
        var scheduledFor = schedule!.NextRunAtUtc!.Value;
        var firstRun = new ScheduleRun(
            Guid.NewGuid(), schedule.Id, scheduledFor, DateTimeOffset.UtcNow,
            JobId: null, ScheduleRunStatus.Pending, FailureCode: null);
        var secondRun = firstRun with { Id = Guid.NewGuid() };

        var firstClaim = await store.TryClaimRunAsync(
            schedule.Id, scheduledFor, firstRun, scheduledFor.AddMinutes(30), CancellationToken.None);
        var secondClaim = await store.TryClaimRunAsync(
            schedule.Id, scheduledFor, secondRun, scheduledFor.AddMinutes(30), CancellationToken.None);

        Assert.True(firstClaim);
        Assert.False(secondClaim);
        Assert.Single(await store.ListRunsAsync(schedule.Id, 10, CancellationToken.None));
    }
}
