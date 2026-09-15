using System.Security.Claims;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using SpeedtestDashboard.Api.Authentication;
using SpeedtestDashboard.Infrastructure.Authentication;

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
        LocalAccountService accounts,
        DashboardAuthenticationState state)
    {
        context.Response.Headers.CacheControl = "no-store";
        var accountConfigured = (await accounts.GetSettingsAsync(context.RequestAborted)).AccountConfigured;
        if (!state.IsEnabled)
        {
            return Results.Ok(SessionResponse.Disabled(accountConfigured, state.ShowDisabledWarning));
        }

        if (context.User.Identity?.IsAuthenticated != true)
        {
            return Results.Ok(SessionResponse.Anonymous(state.ShowDisabledWarning));
        }

        var user = await GetCurrentUserAsync(context, accounts);
        return user is null
            ? Results.Ok(SessionResponse.Anonymous(state.ShowDisabledWarning))
            : Results.Ok(SessionResponse.ForUser(user, state.ShowDisabledWarning));
    }

    private static IResult GetCsrf(
        HttpContext context,
        IAntiforgery antiforgery,
        DashboardAuthenticationState state,
        IOptions<DashboardAuthenticationOptions> options)
    {
        context.Response.Headers.CacheControl = "no-store";
        if (state.IsEnabled && !options.Value.AllowInsecureHttp && !context.Request.IsHttps)
        {
            return Problem(StatusCodes.Status400BadRequest, "https_required",
                "Use HTTPS to initialize request protection while login protection is enabled.");
        }

        var tokens = antiforgery.GetAndStoreTokens(context);
        return Results.Ok(new CsrfResponse(tokens.RequestToken!));
    }

    private static async Task<IResult> LoginAsync(
        LoginRequest request,
        HttpContext context,
        LocalAccountService accounts,
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

        var result = await accounts.AuthenticateAsync(request.Username, request.Password, context.RequestAborted);
        if (result.Status != LocalLoginStatus.Succeeded || result.User is null)
        {
            logger.LogWarning(result.Status == LocalLoginStatus.LockedOut ? "Login failed because the account is locked." : "Login failed.");
            return InvalidCredentials();
        }

        var user = result.User;
        await SignInAsync(context, user);
        logger.LogInformation("Login succeeded.");
        return Results.Ok(SessionResponse.ForUser(user, state.ShowDisabledWarning));
    }

    private static async Task<IResult> SetupAsync(
        SetupAuthenticationRequest request,
        HttpContext context,
        LocalAccountService accounts,
        DashboardAuthenticationState state,
        IOptions<DashboardAuthenticationOptions> options,
        ILogger<Program> logger)
    {
        await state.WaitForMutationAsync(context.RequestAborted);
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

            var result = await accounts.EnableAsync(request.Username, request.Password, context.RequestAborted);
            if (result.Status == LocalSetupStatus.InvalidAccountState)
            {
                return Problem(StatusCodes.Status500InternalServerError, "account_setup_failed",
                    "The local account state requires administrator repair.");
            }
            if (result.Status == LocalSetupStatus.InvalidCredentials || result.User is null)
                return Problem(StatusCodes.Status401Unauthorized, "invalid_credentials", "Invalid username or password.");
            var user = result.User;
            state.Set(true, result.ShowDisabledWarning);
            await SignInAsync(context, user);
            logger.LogInformation("Login protection was enabled from dashboard settings.");
            return Results.Ok(SessionResponse.ForUser(user, state.ShowDisabledWarning));
        }
        finally
        {
            state.MutationLock.Release();
        }
    }

    private static async Task<IResult> LogoutAsync(
        HttpContext context,
        DashboardAuthenticationState state,
        ILogger<Program> logger)
    {
        if (!state.IsEnabled)
        {
            return Results.NoContent();
        }

        await context.SignOutAsync(IdentityConstants.ApplicationScheme);
        logger.LogInformation("The local operator signed out.");
        return Results.NoContent();
    }

    private static async Task<IResult> DisableAsync(
        DisableAuthenticationRequest request,
        HttpContext context,
        LocalAccountService accounts,
        DashboardAuthenticationState state,
        ILogger<Program> logger)
    {
        await state.WaitForMutationAsync(context.RequestAborted);
        try
        {
            var user = await GetCurrentUserAsync(context, accounts);
            if (user is null || !await accounts.DisableAsync(user.Id, request.CurrentPassword, context.RequestAborted))
            {
                return Problem(StatusCodes.Status400BadRequest, "password_confirmation_failed",
                    "The current password is incorrect.");
            }

            var settings = await accounts.GetSettingsAsync(context.RequestAborted);
            state.Set(false, settings.ShowDisabledWarning);
            await context.SignOutAsync(IdentityConstants.ApplicationScheme);
            logger.LogInformation("Login protection was disabled and the local account was removed from dashboard settings.");
            return Results.Ok(SessionResponse.Disabled(accountConfigured: false, state.ShowDisabledWarning));
        }
        finally
        {
            state.MutationLock.Release();
        }
    }

    private static async Task<IResult> UpdatePreferencesAsync(
        AuthenticationPreferencesRequest request,
        HttpContext context,
        LocalAccountService accounts,
        DashboardAuthenticationState state)
    {
        await state.WaitForMutationAsync(context.RequestAborted);
        try
        {
            if (state.IsEnabled)
            {
                return Problem(StatusCodes.Status409Conflict, "authentication_enabled",
                    "This preference only applies while login protection is off.");
            }
            await accounts.UpdatePreferenceAsync(request.ShowDisabledWarning, context.RequestAborted);
            var settings = await accounts.GetSettingsAsync(context.RequestAborted);
            state.Set(settings.AuthenticationEnabled, settings.ShowDisabledWarning);
            return Results.Ok(SessionResponse.Disabled(
                accountConfigured: settings.AccountConfigured,
                showDisabledWarning: state.ShowDisabledWarning));
        }
        finally
        {
            state.MutationLock.Release();
        }
    }

    private static async Task<IResult> ChangePasswordAsync(
        ChangePasswordRequest request,
        HttpContext context,
        LocalAccountService accounts,
        DashboardAuthenticationState state,
        ILogger<Program> logger)
    {
        if (!state.IsEnabled)
        {
            return Problem(StatusCodes.Status409Conflict, "authentication_disabled",
                "Local authentication is disabled for this instance.");
        }

        var user = await GetCurrentUserAsync(context, accounts);
        if (user is null)
        {
            return Problem(StatusCodes.Status401Unauthorized, "authentication_required", "Sign in and try again.");
        }
        if (request.NewPassword.Length is < 6 or > 128 || request.NewPassword.Any(char.IsControl))
        {
            return Problem(StatusCodes.Status400BadRequest, "invalid_password",
                "The new password must be 6-128 characters and contain no control characters.");
        }

        var updated = await accounts.ChangePasswordAsync(user.Id, request.CurrentPassword, request.NewPassword, context.RequestAborted);
        if (updated is null)
        {
            return Problem(StatusCodes.Status400BadRequest, "password_change_failed",
                "The current password is incorrect.");
        }

        await SignInAsync(context, updated);
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

    private static async Task<ApplicationUser?> GetCurrentUserAsync(HttpContext context, LocalAccountService accounts)
    {
        var value = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(value, out var id) ? await accounts.FindByIdAsync(id, context.RequestAborted) : null;
    }

    private static Task SignInAsync(HttpContext context, ApplicationUser user)
    {
        var identity = new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Name, user.UserName!),
            new Claim(LocalAuthenticationClaims.SecurityStamp, user.SecurityStamp!)
        ], IdentityConstants.ApplicationScheme);
        return context.SignInAsync(IdentityConstants.ApplicationScheme, new ClaimsPrincipal(identity));
    }

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
