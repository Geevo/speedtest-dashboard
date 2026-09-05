using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SpeedtestDashboard.Core.Providers;
using SpeedtestDashboard.Core.Tests;
using SpeedtestDashboard.Infrastructure.Processes;
using SpeedtestDashboard.Infrastructure.Providers;
using SpeedtestDashboard.Infrastructure.Providers.LibreSpeed;
using SpeedtestDashboard.Infrastructure.Providers.Ookla;
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

        var processMaximumTimeout = configuration.GetValue<int?>("Processes:MaxTimeoutSeconds") ?? 600;
        services.AddOptions<OoklaOptions>()
            .Bind(configuration.GetSection(OoklaOptions.SectionName))
            .Validate(options => !string.IsNullOrWhiteSpace(options.ExecutablePath) &&
                                 options.ExecutablePath.IndexOfAny(['\r', '\n', '\0']) < 0,
                "Ookla executable path is invalid.")
            .Validate(options => options.HealthTimeoutSeconds is >= 1 and <= 30,
                "Ookla health timeout must be between 1 and 30 seconds.")
            .Validate(options => options.HealthCacheSeconds is >= 10 and <= 300,
                "Ookla health cache duration must be between 10 and 300 seconds.")
            .Validate(options => options.TestTimeoutSeconds >= 30 && options.TestTimeoutSeconds <= processMaximumTimeout,
                "Ookla test timeout must be between 30 seconds and the configured process maximum.")
            .Validate(options => options.ServerListTimeoutSeconds >= 5 && options.ServerListTimeoutSeconds <= processMaximumTimeout,
                "Ookla server-list timeout must be between 5 seconds and the configured process maximum.")
            .Validate(options => options.ServerCacheSeconds is >= 30 and <= 3600,
                "Ookla server cache duration must be between 30 and 3600 seconds.")
            .Validate(options => options.MaximumServers is >= 1 and <= 500,
                "Ookla maximum server count must be between 1 and 500.")
            .ValidateOnStart();

        services.AddOptions<LibreSpeedOptions>()
            .Bind(configuration.GetSection(LibreSpeedOptions.SectionName))
            .Validate(options => !string.IsNullOrWhiteSpace(options.ExecutablePath) &&
                                 options.ExecutablePath.IndexOfAny(['\r', '\n', '\0']) < 0,
                "LibreSpeed executable path is invalid.")
            .Validate(options => options.HealthTimeoutSeconds is >= 1 and <= 30,
                "LibreSpeed health timeout must be between 1 and 30 seconds.")
            .Validate(options => options.HealthCacheSeconds is >= 10 and <= 300,
                "LibreSpeed health cache duration must be between 10 and 300 seconds.")
            .Validate(options => options.TestTimeoutSeconds >= 30 && options.TestTimeoutSeconds <= processMaximumTimeout,
                "LibreSpeed test timeout must be between 30 seconds and the configured process maximum.")
            .Validate(options => options.ServerListTimeoutSeconds >= 5 && options.ServerListTimeoutSeconds <= 120,
                "LibreSpeed server-list timeout must be between 5 and 120 seconds.")
            .Validate(options => options.ServerCacheSeconds is >= 30 and <= 3600,
                "LibreSpeed server cache duration must be between 30 and 3600 seconds.")
            .Validate(options => options.MaximumServers is >= 1 and <= 500,
                "LibreSpeed maximum server count must be between 1 and 500.")
            .ValidateOnStart();

        services.AddOptions<ScheduleWorkerOptions>()
            .Bind(configuration.GetSection(ScheduleWorkerOptions.SectionName))
            .Validate(options => options.PollIntervalSeconds is >= 5 and <= 300, "Scheduler poll interval must be between 5 and 300 seconds.")
            .ValidateOnStart();

        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<ISpeedTestProviderRegistry, SpeedTestProviderRegistry>();
        services.AddSingleton<IProcessRunner, ProcessRunner>();
        services.AddSingleton<OoklaCommandFactory>();
        services.AddSingleton<OoklaServerListParser>();
        services.AddSingleton<OoklaResultParser>();
        services.AddSingleton<OoklaSpeedTestProvider>();
        services.AddSingleton<ISpeedTestProvider>(serviceProvider =>
            serviceProvider.GetRequiredService<OoklaSpeedTestProvider>());
        services.AddSingleton<LibreSpeedCommandFactory>();
        services.AddSingleton<LibreSpeedServerCatalogParser>();
        services.AddHttpClient<LibreSpeedServerCatalogClient>(client =>
        {
            client.Timeout = Timeout.InfiniteTimeSpan;
        });
        services.AddSingleton<LibreSpeedResultParser>();
        services.AddSingleton<LibreSpeedSpeedTestProvider>();
        services.AddSingleton<ISpeedTestProvider>(serviceProvider =>
            serviceProvider.GetRequiredService<LibreSpeedSpeedTestProvider>());
        services.AddSingleton<ISpeedTestJobStore, InMemorySpeedTestJobStore>();
        services.AddSingleton<ISpeedTestQueue, SpeedTestQueue>();
        services.AddSingleton<ISpeedTestCancellationRegistry, SpeedTestCancellationRegistry>();
        services.AddSingleton<ISpeedTestSubmissionService, SpeedTestSubmissionService>();
        services.AddHostedService<SpeedTestWorker>();
        services.AddHostedService<ScheduleWorker>();
        return services;
    }
}
