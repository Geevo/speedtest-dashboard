using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace SpeedtestDashboard.Infrastructure.Persistence;

public sealed class DashboardDbContextDesignFactory : IDesignTimeDbContextFactory<DashboardDbContext>
{
    public DashboardDbContext CreateDbContext(string[] args)
    {
        var options = new StorageOptions
        {
            DatabasePath = Path.Combine(Path.GetTempPath(), "speedtest-dashboard-design.db")
        };
        var builder = new DbContextOptionsBuilder<DashboardDbContext>();
        builder.UseSqlite(PersistenceServiceCollectionExtensions.BuildConnectionString(options));
        return new DashboardDbContext(builder.Options);
    }
}
