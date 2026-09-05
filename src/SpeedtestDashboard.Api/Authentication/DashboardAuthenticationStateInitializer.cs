using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SpeedtestDashboard.Infrastructure.Authentication;
using SpeedtestDashboard.Infrastructure.Persistence;
using SpeedtestDashboard.Infrastructure.Persistence.Entities;

namespace SpeedtestDashboard.Api.Authentication;

internal sealed class DashboardAuthenticationStateInitializer(
    DashboardDbContext context,
    UserManager<ApplicationUser> userManager,
    DashboardAuthenticationState state)
{
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var users = await userManager.Users.OrderBy(user => user.Id).Take(2).ToArrayAsync(cancellationToken);
        if (users.Length > 1)
        {
            throw new InvalidOperationException(
                "The database contains more than one application user. Speedtest Dashboard supports exactly one local operator account.");
        }

        var settings = await context.DashboardSettings.SingleOrDefaultAsync(cancellationToken);
        if (settings is null)
        {
            settings = new DashboardSettingsEntity();
            context.DashboardSettings.Add(settings);
            await context.SaveChangesAsync(cancellationToken);
        }
        if (settings.AuthenticationEnabled && users.Length == 0)
        {
            throw new InvalidOperationException(
                "Dashboard authentication is enabled but the local operator account is missing.");
        }

        state.Set(settings.AuthenticationEnabled, settings.ShowAuthenticationDisabledWarning);
    }
}
