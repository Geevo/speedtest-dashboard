using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SpeedtestDashboard.Core.Providers;
using SpeedtestDashboard.Core.Tests;
using SpeedtestDashboard.Infrastructure.Processes;
using SpeedtestDashboard.Infrastructure.Providers;
using SpeedtestDashboard.Infrastructure.Schedules;
using SpeedtestDashboard.Infrastructure.Tests;

namespace SpeedtestDashboard.Infrastructure;

public static class SpeedTestServiceCollectionExtensions
{
    public static IServiceCollection AddSpeedTestOrchestration(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<SpeedTestOptions>()
            .Bind(configuration.GetSection(SpeedTestOptions.SectionName))
            .Validate(options => options.QueueCapacity is >= 1 and <= 32, "Queue capacity must be between 1 and 32.")
            .Validate(options => options.QueueFullRetryAfterSeconds is >= 1 and <= 300, "Queue retry delay must be between 1 and 300 seconds.")
            .Validate(options => options.SseHeartbeatSeconds is >= 1 and <= 120, "SSE heartbeat must be between 1 and 120 seconds.")
            .ValidateOnStart();

        services.AddOptions<ProcessOptions>()
            .Bind(configuration.GetSection(ProcessOptions.SectionName))
            .Validate(options => options.DefaultTimeoutSeconds is >= 1 and <= 600, "Default process timeout must be between 1 and 600 seconds.")
            .Validate(options => options.MaxTimeoutSeconds is >= 1 and <= 3600, "Maximum process timeout must be between 1 and 3600 seconds.")
            .Validate(options => options.DefaultTimeoutSeconds <= options.MaxTimeoutSeconds, "Default process timeout cannot exceed the maximum.")
            .Validate(options => options.AbsoluteOutputLimitBytes is >= 1024 and <= 64 * 1024 * 1024, "Absolute output limit is outside the permitted range.")
            .Validate(options => options.DefaultStdoutLimitBytes is >= 1 && options.DefaultStdoutLimitBytes <= options.AbsoluteOutputLimitBytes, "Default stdout limit is invalid.")
            .Validate(options => options.DefaultStderrLimitBytes is >= 1 && options.DefaultStderrLimitBytes <= options.AbsoluteOutputLimitBytes, "Default stderr limit is invalid.")
            .ValidateOnStart();

        services.AddBuiltInSpeedTestProviders(configuration);

        services.AddOptions<ScheduleWorkerOptions>()
            .Bind(configuration.GetSection(ScheduleWorkerOptions.SectionName))
            .Validate(options => options.PollIntervalSeconds is >= 5 and <= 300, "Scheduler poll interval must be between 5 and 300 seconds.")
            .ValidateOnStart();

        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<ISpeedTestProviderRegistry, SpeedTestProviderRegistry>();
        services.AddSingleton<IProcessRunner, ProcessRunner>();
        services.AddSingleton<ISpeedTestJobStore, InMemorySpeedTestJobStore>();
        services.AddSingleton<ISpeedTestQueue, SpeedTestQueue>();
        services.AddSingleton<ISpeedTestCancellationRegistry, SpeedTestCancellationRegistry>();
        services.AddSingleton<ISpeedTestSubmissionService, SpeedTestSubmissionService>();
        services.AddHostedService<SpeedTestWorker>();
        services.AddHostedService<ScheduleWorker>();
        return services;
    }
}
