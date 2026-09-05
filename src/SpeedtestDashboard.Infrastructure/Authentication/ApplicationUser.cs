using Microsoft.AspNetCore.Identity;

namespace SpeedtestDashboard.Infrastructure.Authentication;

public sealed class ApplicationUser : IdentityUser<Guid>
{
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset? LastLoginAtUtc { get; set; }
}
