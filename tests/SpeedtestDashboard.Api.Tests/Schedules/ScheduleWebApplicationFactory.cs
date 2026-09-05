using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using SpeedtestDashboard.Api.Tests.Orchestration;
using SpeedtestDashboard.Core.Providers;
using SpeedtestDashboard.Infrastructure.Schedules;

namespace SpeedtestDashboard.Api.Tests.Schedules;

/// <summary>
/// A dashboard host with a fixture provider and a real SQLite-backed schedule store, where
/// <see cref="ScheduleWorker"/> is resolvable directly for deterministic, manually-ticked polling
/// instead of running on its own background timer.
/// </summary>
internal sealed class ScheduleWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly string _root;
    private readonly bool _ownsRoot;
    private readonly FakeSpeedTestProvider _provider;

    public ScheduleWebApplicationFactory(FakeSpeedTestProvider provider, string? root = null)
    {
        _provider = provider;
        _ownsRoot = root is null;
        _root = root ?? Path.Combine(Path.GetTempPath(), "speedtest-schedule-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public string DatabasePath => Path.Combine(_root, "speedtest.db");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Storage:DatabasePath"] = DatabasePath,
                ["Authentication:DataProtectionPath"] = Path.Combine(_root, "dataprotection")
            }));
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<ISpeedTestProvider>();
            services.AddSingleton<ISpeedTestProvider>(_provider);

            var scheduleWorkerDescriptor = services.SingleOrDefault(descriptor =>
                descriptor.ServiceType == typeof(IHostedService) &&
                descriptor.ImplementationType == typeof(ScheduleWorker));
            if (scheduleWorkerDescriptor is not null)
            {
                services.Remove(scheduleWorkerDescriptor);
                services.AddSingleton<ScheduleWorker>();
            }
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing && _ownsRoot && Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
