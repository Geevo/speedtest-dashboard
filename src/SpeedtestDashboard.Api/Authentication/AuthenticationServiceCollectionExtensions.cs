using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using SpeedtestDashboard.Infrastructure.Authentication;
using SpeedtestDashboard.Infrastructure.Persistence;

namespace SpeedtestDashboard.Api.Authentication;

public static class AuthenticationServiceCollectionExtensions
{
    private const string SelectorScheme = "DashboardAuthentication";

    public static IServiceCollection AddDashboardAuthentication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<DashboardAuthenticationOptions>()
            .Bind(configuration.GetSection(DashboardAuthenticationOptions.SectionName))
            .Validate(options => Path.IsPathFullyQualified(options.DataProtectionPath),
                "Data Protection path must be absolute.")
            .ValidateOnStart();

        services.AddDataProtection().SetApplicationName("SpeedtestDashboard");
        services.AddOptions<KeyManagementOptions>()
            .Configure<IOptions<DashboardAuthenticationOptions>, ILoggerFactory>((keyOptions, authOptions, loggerFactory) =>
                keyOptions.XmlRepository = new FileSystemXmlRepository(
                    new DirectoryInfo(authOptions.Value.DataProtectionPath), loggerFactory));

        services.AddOptions<PasswordHasherOptions>();
        services.AddSingleton<IPasswordHasher<ApplicationUser>, PasswordHasher<ApplicationUser>>();
        services.AddSingleton<LocalAccountService>();

        services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = SelectorScheme;
                options.DefaultChallengeScheme = SelectorScheme;
                options.DefaultForbidScheme = SelectorScheme;
                options.DefaultSignInScheme = IdentityConstants.ApplicationScheme;
                options.DefaultSignOutScheme = IdentityConstants.ApplicationScheme;
            })
            .AddPolicyScheme(SelectorScheme, SelectorScheme, options =>
                options.ForwardDefaultSelector = context =>
                    !context.RequestServices.GetRequiredService<DashboardAuthenticationState>().IsEnabled
                        ? DisabledAuthenticationHandler.SchemeName
                        : IdentityConstants.ApplicationScheme)
            .AddCookie(IdentityConstants.ApplicationScheme, options =>
            {
                options.Cookie.Name = "SpeedtestDashboard.Session";
                options.Cookie.HttpOnly = true;
                options.Cookie.SameSite = SameSiteMode.Lax;
                options.Cookie.Path = "/";
                options.SlidingExpiration = true;
                options.ExpireTimeSpan = TimeSpan.FromHours(12);
                options.Events.OnRedirectToLogin = context =>
                {
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return Task.CompletedTask;
                };
                options.Events.OnRedirectToAccessDenied = context =>
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    return Task.CompletedTask;
                };
                options.Events.OnValidatePrincipal = async context =>
                {
                    var idValue = context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
                    var stamp = context.Principal?.FindFirstValue(LocalAuthenticationClaims.SecurityStamp);
                    if (!Guid.TryParse(idValue, out var id) || stamp is null)
                    {
                        context.RejectPrincipal();
                        return;
                    }
                    var account = context.HttpContext.RequestServices.GetRequiredService<LocalAccountService>();
                    var user = await account.FindByIdAsync(id, context.HttpContext.RequestAborted);
                    if (user is null || !string.Equals(user.SecurityStamp, stamp, StringComparison.Ordinal))
                        context.RejectPrincipal();
                };
            })
            .AddScheme<AuthenticationSchemeOptions, DisabledAuthenticationHandler>(
                DisabledAuthenticationHandler.SchemeName, _ => { });

        services.AddOptions<CookieAuthenticationOptions>(IdentityConstants.ApplicationScheme)
            .Configure<IOptions<DashboardAuthenticationOptions>>((cookie, auth) =>
                cookie.Cookie.SecurePolicy = auth.Value.AllowInsecureHttp
                    ? CookieSecurePolicy.SameAsRequest
                    : CookieSecurePolicy.Always);
        services.AddAuthorizationBuilder().SetFallbackPolicy(new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .Build());

        services.AddAntiforgery(options =>
        {
            options.HeaderName = "X-CSRF-TOKEN";
            options.Cookie.Name = "SpeedtestDashboard.Antiforgery";
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Strict;
            // Anonymous dashboards support HTTP. Session cookies and credential endpoints
            // retain their separate HTTPS requirements.
            options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        });

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddPolicy("login", context => RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 10,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                    AutoReplenishment = true
                }));
        });

        services.AddScoped<CsrfValidationFilter>();
        services.AddSingleton<DashboardAuthenticationState>();
        services.AddScoped<DashboardAuthenticationStateInitializer>();
        return services;
    }
}
