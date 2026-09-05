using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace SpeedtestDashboard.Api.Tests;

public sealed class DashboardWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly string _storageDirectory = Path.Combine(
        Path.GetTempPath(),
        "speedtest-dashboard-tests",
        Guid.NewGuid().ToString("N"));

    public string DatabasePath => Path.Combine(_storageDirectory, "speedtest.db");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Storage:DatabasePath"] = DatabasePath
            }));
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing && Directory.Exists(_storageDirectory))
        {
            Directory.Delete(_storageDirectory, recursive: true);
        }
    }
}
