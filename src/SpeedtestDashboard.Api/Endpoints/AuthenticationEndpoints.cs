using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SpeedtestDashboard.Api.Authentication;
using SpeedtestDashboard.Infrastructure.Authentication;
using SpeedtestDashboard.Infrastructure.Persistence;
using SpeedtestDashboard.Infrastructure.Persistence.Entities;

namespace SpeedtestDashboard.Api.Endpoints;

public static partial class AuthenticationEndpoints
{
    public static IEndpointRouteBuilder MapAuthenticationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/auth/session", GetSessionAsync)
            .AllowAnonymous()
            .WithTags("Authentication");

        endpoints.MapGet("/api/auth/csrf", GetCsrf)
            .AllowAnonymous()
            .WithTags("Authentication");

        endpoints.MapPost("/api/auth/login", LoginAsync)
            .AllowAnonymous()
            .RequireRateLimiting("login")
            .RequireCsrf()
            .WithTags("Authentication");

        endpoints.MapPost("/api/auth/setup", SetupAsync)
            .RequireRateLimiting("login")
            .RequireCsrf()
            .WithTags("Authentication");

        endpoints.MapPost("/api/auth/disable", DisableAsync)
            .RequireCsrf()
            .WithTags("Authentication");

        endpoints.MapPost("/api/auth/preferences", UpdatePreferencesAsync)
            .RequireCsrf()
            .WithTags("Authentication");

        endpoints.MapPost("/api/auth/logout", LogoutAsync)
            .RequireCsrf()
            .WithTags("Authentication");

        endpoints.MapPost("/api/auth/change-password", ChangePasswordAsync)
            .RequireCsrf()
            .WithTags("Authentication");

        return endpoints;
    }

    private static async Task<IResult> GetSessionAsync(
        HttpContext context,
        UserManager<ApplicationUser> userManager,
        DashboardAuthenticationState state)
    {
        context.Response.Headers.CacheControl = "no-store";
        var accountConfigured = await userManager.Users.AnyAsync();
        if (!state.IsEnabled)
        {
            return Results.Ok(SessionResponse.Disabled(accountConfigured, state.ShowDisabledWarning));
        }

        if (context.User.Identity?.IsAuthenticated != true)
        {
            return Results.Ok(SessionResponse.Anonymous(state.ShowDisabledWarning));
        }

        var user = await userManager.GetUserAsync(context.User);
        return user is null
            ? Results.Ok(SessionResponse.Anonymous(state.ShowDisabledWarning))
            : Results.Ok(SessionResponse.ForUser(user, state.ShowDisabledWarning));
    }

    private static IResult GetCsrf(HttpContext context, IAntiforgery antiforgery)
    {
        context.Response.Headers.CacheControl = "no-store";
        var tokens = antiforgery.GetAndStoreTokens(context);
        return Results.Ok(new CsrfResponse(tokens.RequestToken!));
    }

    private static async Task<IResult> LoginAsync(
        LoginRequest request,
        HttpContext context,
        SignInManager<ApplicationUser> signInManager,
        UserManager<ApplicationUser> userManager,
        IOptions<DashboardAuthenticationOptions> options,
        DashboardAuthenticationState state,
        ILogger<Program> logger)
    {
        context.Response.Headers.CacheControl = "no-store";
        if (!state.IsEnabled)
        {
            return Problem(StatusCodes.Status409Conflict, "authentication_disabled",
                "Local authentication is disabled for this instance.");
        }
        if (!options.Value.AllowInsecureHttp && !context.Request.IsHttps)
        {
            return Problem(StatusCodes.Status400BadRequest, "https_required",
                "This dashboard accepts login credentials over HTTPS only.");
        }
        if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrEmpty(request.Password) ||
            request.Username.Length > 64 || request.Password.Length > 128)
        {
            return InvalidCredentials();
        }

        var result = await signInManager.PasswordSignInAsync(
            request.Username, request.Password, isPersistent: false, lockoutOnFailure: true);
        if (!result.Succeeded)
        {
            logger.LogWarning(result.IsLockedOut ? "Login failed because the account is locked." : "Login failed.");
            return InvalidCredentials();
        }

        var user = await userManager.FindByNameAsync(request.Username);
        if (user is null)
        {
            await signInManager.SignOutAsync();
            return InvalidCredentials();
        }

        user.LastLoginAtUtc = DateTimeOffset.UtcNow;
        var update = await userManager.UpdateAsync(user);
        if (!update.Succeeded)
        {
            await signInManager.SignOutAsync();
            return Problem(StatusCodes.Status500InternalServerError, "login_state_failed",
                "The sign-in could not be completed.");
        }

        logger.LogInformation("Login succeeded.");
        return Results.Ok(SessionResponse.ForUser(user, state.ShowDisabledWarning));
    }

    private static async Task<IResult> SetupAsync(
        SetupAuthenticationRequest request,
        HttpContext context,
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        DashboardDbContext database,
        DashboardAuthenticationState state,
        IOptions<DashboardAuthenticationOptions> options,
        ILogger<Program> logger)
    {
        await state.MutationLock.WaitAsync(context.RequestAborted);
        try
        {
            context.Response.Headers.CacheControl = "no-store";
            if (state.IsEnabled)
            {
                return Problem(StatusCodes.Status409Conflict, "authentication_enabled",
                    "Login protection is already enabled.");
            }
            if (!options.Value.AllowInsecureHttp && !context.Request.IsHttps)
            {
                return Problem(StatusCodes.Status400BadRequest, "https_required",
                    "This dashboard accepts credentials over HTTPS only.");
            }
            if (!UsernameRegex().IsMatch(request.Username) || !IsValidPasswordMaterial(request.Password))
            {
                return Problem(StatusCodes.Status400BadRequest, "invalid_credentials",
                    "Use a 3-64 character username and a 6-128 character password.");
            }

            var users = await userManager.Users.OrderBy(user => user.Id).Take(2).ToArrayAsync();
            if (users.Length > 1)
            {
                return Problem(StatusCodes.Status500InternalServerError, "invalid_account_state",
                    "The local account state requires administrator repair.");
            }

            ApplicationUser user;
            if (users.Length == 0)
            {
                user = new ApplicationUser
                {
                    Id = Guid.NewGuid(),
                    UserName = request.Username,
                    CreatedAtUtc = DateTimeOffset.UtcNow,
                    LockoutEnabled = true
                };
                var created = await userManager.CreateAsync(user, request.Password);
                if (!created.Succeeded)
                {
                    return Problem(StatusCodes.Status400BadRequest, "account_setup_failed",
                        string.Join(" ", created.Errors.Select(error => error.Description)));
                }
            }
            else
            {
                user = users[0];
                if (!string.Equals(user.NormalizedUserName, userManager.NormalizeName(request.Username), StringComparison.Ordinal) ||
                    !await userManager.CheckPasswordAsync(user, request.Password))
                {
                    return Problem(StatusCodes.Status401Unauthorized, "invalid_credentials",
                        "Invalid username or password.");
                }
            }

            var settings = await GetSettingsAsync(database);
            settings.AuthenticationEnabled = true;
            user.LastLoginAtUtc = DateTimeOffset.UtcNow;
            var updated = await userManager.UpdateAsync(user);
            if (!updated.Succeeded)
            {
                return Problem(StatusCodes.Status500InternalServerError, "account_setup_failed",
                    "Login protection could not be enabled.");
            }
            await database.SaveChangesAsync();
            state.Set(true, settings.ShowAuthenticationDisabledWarning);
            await signInManager.SignInAsync(user, isPersistent: false);
            logger.LogInformation("Login protection was enabled from dashboard settings.");
            return Results.Ok(SessionResponse.ForUser(user, state.ShowDisabledWarning));
        }
        finally
        {
            state.MutationLock.Release();
        }
    }

    private static async Task<IResult> LogoutAsync(
        SignInManager<ApplicationUser> signInManager,
        DashboardAuthenticationState state,
        ILogger<Program> logger)
    {
        if (!state.IsEnabled)
        {
            return Results.NoContent();
        }

        await signInManager.SignOutAsync();
        logger.LogInformation("The local operator signed out.");
        return Results.NoContent();
    }

    private static async Task<IResult> DisableAsync(
        DisableAuthenticationRequest request,
        HttpContext context,
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        DashboardDbContext database,
        DashboardAuthenticationState state,
        ILogger<Program> logger)
    {
        var user = await userManager.GetUserAsync(context.User);
        if (user is null || !await userManager.CheckPasswordAsync(user, request.CurrentPassword))
        {
            return Problem(StatusCodes.Status400BadRequest, "password_confirmation_failed",
                "The current password is incorrect.");
        }

        var settings = await GetSettingsAsync(database);
        settings.AuthenticationEnabled = false;
        await database.SaveChangesAsync();
        state.Set(false, settings.ShowAuthenticationDisabledWarning);
        await signInManager.SignOutAsync();
        logger.LogInformation("Login protection was disabled from dashboard settings.");
        return Results.Ok(SessionResponse.Disabled(accountConfigured: true, state.ShowDisabledWarning));
    }

    private static async Task<IResult> UpdatePreferencesAsync(
        AuthenticationPreferencesRequest request,
        DashboardDbContext database,
        DashboardAuthenticationState state)
    {
        if (state.IsEnabled)
        {
            return Problem(StatusCodes.Status409Conflict, "authentication_enabled",
                "This preference only applies while login protection is off.");
        }
        var settings = await GetSettingsAsync(database);
        settings.ShowAuthenticationDisabledWarning = request.ShowDisabledWarning;
        await database.SaveChangesAsync();
        state.Set(settings.AuthenticationEnabled, settings.ShowAuthenticationDisabledWarning);
        return Results.Ok(SessionResponse.Disabled(
            accountConfigured: await database.Users.AnyAsync(),
            showDisabledWarning: state.ShowDisabledWarning));
    }

    private static async Task<IResult> ChangePasswordAsync(
        ChangePasswordRequest request,
        HttpContext context,
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        DashboardAuthenticationState state,
        ILogger<Program> logger)
    {
        if (!state.IsEnabled)
        {
            return Problem(StatusCodes.Status409Conflict, "authentication_disabled",
                "Local authentication is disabled for this instance.");
        }

        var user = await userManager.GetUserAsync(context.User);
        if (user is null)
        {
            return Problem(StatusCodes.Status401Unauthorized, "authentication_required", "Sign in and try again.");
        }
        if (request.NewPassword.Length > 128 || request.NewPassword.Any(char.IsControl))
        {
            return Problem(StatusCodes.Status400BadRequest, "invalid_password",
                "The new password must be 6-128 characters and contain no control characters.");
        }

        var result = await userManager.ChangePasswordAsync(user, request.CurrentPassword, request.NewPassword);
        if (!result.Succeeded)
        {
            return Problem(StatusCodes.Status400BadRequest, "password_change_failed",
                string.Join(" ", result.Errors.Select(error => error.Description)));
        }

        await signInManager.RefreshSignInAsync(user);
        logger.LogInformation("The local operator changed their password.");
        return Results.NoContent();
    }

    private static IResult InvalidCredentials() => Problem(
        StatusCodes.Status401Unauthorized,
        "invalid_credentials",
        "Invalid username or password.");

    private static IResult Problem(int status, string code, string detail) => Results.Problem(
        statusCode: status,
        title: status == StatusCodes.Status401Unauthorized ? "Authentication failed" : "Invalid request",
        detail: detail,
        extensions: new Dictionary<string, object?> { ["code"] = code });

    private static bool IsValidPasswordMaterial(string password) =>
        password.Length is >= 6 and <= 128 && !password.Any(char.IsControl);

    private static async Task<DashboardSettingsEntity> GetSettingsAsync(DashboardDbContext database) =>
        await database.DashboardSettings.SingleAsync();

    [GeneratedRegex("^[A-Za-z0-9._-]{3,64}$", RegexOptions.CultureInvariant)]
    private static partial Regex UsernameRegex();
}

public sealed record LoginRequest(string Username, string Password);
public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);
public sealed record SetupAuthenticationRequest(string Username, string Password);
public sealed record DisableAuthenticationRequest(string CurrentPassword);
public sealed record AuthenticationPreferencesRequest(bool ShowDisabledWarning);
public sealed record CsrfResponse(string Token);
public sealed record SessionUserResponse(Guid Id, string Username);
public sealed record SessionResponse(
    string Mode,
    bool Authenticated,
    SessionUserResponse? User,
    bool LoginConfigured,
    bool ShowDisabledWarning)
{
    public static SessionResponse Anonymous(bool showDisabledWarning) =>
        new("local", false, null, true, showDisabledWarning);
    public static SessionResponse Disabled(bool accountConfigured, bool showDisabledWarning) =>
        new("none", true, null, accountConfigured, showDisabledWarning);
    public static SessionResponse ForUser(ApplicationUser user, bool showDisabledWarning) =>
        new("local", true, new SessionUserResponse(user.Id, user.UserName!), true, showDisabledWarning);
}
