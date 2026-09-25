using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SpeedtestDashboard.Core.Providers;

namespace SpeedtestDashboard.Infrastructure.Providers.Ookla;

public static class OoklaServiceCollectionExtensions
{
    public static IServiceCollection AddOoklaProvider(this IServiceCollection services, IConfiguration configuration)
    {
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

        services.AddSingleton<OoklaCommandFactory>();
        services.AddSingleton<OoklaServerListParser>();
        services.AddSingleton<OoklaResultParser>();
        services.AddSingleton<OoklaSpeedTestProvider>();
        services.AddSingleton<ISpeedTestProvider>(provider => provider.GetRequiredService<OoklaSpeedTestProvider>());
        return services;
    }
}
