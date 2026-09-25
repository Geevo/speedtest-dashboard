using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SpeedtestDashboard.Core.Providers;

namespace SpeedtestDashboard.Infrastructure.Providers.FastCom;

public static class FastComServiceCollectionExtensions
{
    public static IServiceCollection AddFastComProvider(this IServiceCollection services, IConfiguration configuration)
    {
        var processMaximumTimeout = configuration.GetValue<int?>("Processes:MaxTimeoutSeconds") ?? 600;
        services.AddOptions<FastComOptions>()
            .Bind(configuration.GetSection(FastComOptions.SectionName))
            .Validate(options => !string.IsNullOrWhiteSpace(options.ExecutablePath) &&
                                 options.ExecutablePath.IndexOfAny(['\r', '\n', '\0']) < 0,
                "FAST.com executable path is invalid.")
            .Validate(options => options.HealthTimeoutSeconds is >= 1 and <= 30,
                "FAST.com health timeout must be between 1 and 30 seconds.")
            .Validate(options => options.HealthCacheSeconds is >= 10 and <= 300,
                "FAST.com health cache duration must be between 10 and 300 seconds.")
            .Validate(options => options.TestTimeoutSeconds >= 30 && options.TestTimeoutSeconds <= processMaximumTimeout,
                "FAST.com test timeout must be between 30 seconds and the configured process maximum.")
            .Validate(options => options.DurationSeconds is >= 7 and <= 30,
                "FAST.com test duration must be between 7 and 30 seconds.")
            .Validate(options => options.TestTimeoutSeconds >= (2 * options.DurationSeconds) + 10,
                "FAST.com test timeout must allow both bandwidth phases and a ten-second margin.")
            .ValidateOnStart();

        services.AddSingleton<FastComCommandFactory>();
        services.AddSingleton<FastComResultParser>();
        services.AddSingleton<FastComSpeedTestProvider>();
        services.AddSingleton<ISpeedTestProvider>(provider => provider.GetRequiredService<FastComSpeedTestProvider>());
        return services;
    }
}
