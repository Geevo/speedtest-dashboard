using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SpeedtestDashboard.Core.Providers;

namespace SpeedtestDashboard.Infrastructure.Providers.LibreSpeed;

public static class LibreSpeedServiceCollectionExtensions
{
    public static IServiceCollection AddLibreSpeedProvider(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var processMaximumTimeout = configuration.GetValue<int?>("Processes:MaxTimeoutSeconds") ?? 600;
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
            .Validate(options => options.ServerListTimeoutSeconds is >= 5 and <= 120,
                "LibreSpeed server-list timeout must be between 5 and 120 seconds.")
            .Validate(options => options.ServerCacheSeconds is >= 30 and <= 3600,
                "LibreSpeed server cache duration must be between 30 and 3600 seconds.")
            .Validate(options => options.MaximumServers is >= 1 and <= 500,
                "LibreSpeed maximum server count must be between 1 and 500.")
            .ValidateOnStart();

        services.AddSingleton<LibreSpeedCommandFactory>();
        services.AddSingleton<LibreSpeedServerCatalogParser>();
        services.AddHttpClient<LibreSpeedServerCatalogClient>(client => client.Timeout = Timeout.InfiniteTimeSpan);
        services.AddSingleton<LibreSpeedResultParser>();
        services.AddSingleton<LibreSpeedSpeedTestProvider>();
        services.AddSingleton<ISpeedTestProvider>(provider => provider.GetRequiredService<LibreSpeedSpeedTestProvider>());
        return services;
    }
}
