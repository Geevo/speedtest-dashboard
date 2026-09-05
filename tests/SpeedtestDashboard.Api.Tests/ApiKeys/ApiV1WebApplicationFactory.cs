using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using SpeedtestDashboard.Core.Network;
using SpeedtestDashboard.Core.Providers;

namespace SpeedtestDashboard.Api.Tests.ApiKeys;

internal sealed class ApiV1WebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "speedtest-apiv1-tests", Guid.NewGuid().ToString("N"));
    private readonly ISpeedTestProvider _provider;
    private readonly INetworkIdentityService? _identity;
    private readonly bool _runWorker;
    private readonly int _queueCapacity;
    private readonly TimeProvider? _timeProvider;

    public ApiV1WebApplicationFactory(
        ISpeedTestProvider provider,
        INetworkIdentityService? identity = null,
        bool runWorker = true,
        int queueCapacity = 4,
        TimeProvider? timeProvider = null)
    {
        _provider = provider;
        _identity = identity;
        _runWorker = runWorker;
        _queueCapacity = queueCapacity;
        _timeProvider = timeProvider;
        Directory.CreateDirectory(_root);
    }

    public string DatabasePath => Path.Combine(_root, "speedtest.db");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Storage:DatabasePath"] = DatabasePath,
                ["Authentication:DataProtectionPath"] = Path.Combine(_root, "dataprotection"),
                ["SpeedTests:QueueCapacity"] = _queueCapacity.ToString(),
                ["SpeedTests:QueueFullRetryAfterSeconds"] = "2"
            }));
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<ISpeedTestProvider>();
            services.AddSingleton(_provider);
            if (_identity is not null)
            {
                services.RemoveAll<INetworkIdentityService>();
                services.AddSingleton(_identity);
            }

            if (_timeProvider is not null)
            {
                services.RemoveAll<TimeProvider>();
                services.AddSingleton(_timeProvider);
            }

            if (!_runWorker)
            {
                services.RemoveAll<IHostedService>();
            }
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing && Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
