using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SpeedtestDashboard.Core.History;
using SpeedtestDashboard.Core.Schedules;
using SpeedtestDashboard.Core.Statistics;

namespace SpeedtestDashboard.Infrastructure.Persistence;

public static class PersistenceServiceCollectionExtensions
{
    public static IServiceCollection AddDashboardPersistence(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<StorageOptions>()
            .Bind(configuration.GetSection(StorageOptions.SectionName))
            .Validate(
                options => Path.IsPathFullyQualified(options.DatabasePath) &&
                           options.DatabasePath.IndexOfAny(['\r', '\n', '\0']) < 0 &&
                           !string.IsNullOrWhiteSpace(Path.GetFileName(options.DatabasePath)),
                "Storage database path must be an absolute file path.")
            .Validate(options => options.CommandTimeoutSeconds is >= 1 and <= 60,
                "Storage command timeout must be between 1 and 60 seconds.")
            .ValidateOnStart();

        services.AddSingleton<SqliteConnectionFactory>();
        services.AddSingleton<SqliteSpeedTestStore>();
        services.AddSingleton<ISpeedTestPersistenceWriter>(provider => provider.GetRequiredService<SqliteSpeedTestStore>());
        services.AddSingleton<ISpeedTestHistoryStore>(provider => provider.GetRequiredService<SqliteSpeedTestStore>());
        services.AddSingleton<ISpeedTestStatisticsService, SqliteStatisticsService>();
        services.AddSingleton<ISpeedTestScheduleStore, SqliteScheduleStore>();
        services.AddSingleton<DatabaseMaintenanceService>();
        services.AddSingleton<DashboardDatabaseInitializer>();
        return services;
    }

    internal static string BuildConnectionString(StorageOptions options)
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = options.DatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            ForeignKeys = true,
            DefaultTimeout = options.CommandTimeoutSeconds
        };
        return builder.ToString();
    }
}
