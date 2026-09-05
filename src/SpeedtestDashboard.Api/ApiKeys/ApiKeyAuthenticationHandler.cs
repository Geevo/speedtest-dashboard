using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using SpeedtestDashboard.Infrastructure.ApiKeys;

namespace SpeedtestDashboard.Api.ApiKeys;

internal sealed class ApiKeyAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IApiCredentialService credentialService)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    private const string BearerPrefix = "Bearer ";
    private const int MaximumPresentedKeyLength = 512;

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue("Authorization", out var headerValues))
        {
            return AuthenticateResult.Fail("Missing Authorization header.");
        }

        var header = headerValues.ToString();
        if (!header.StartsWith(BearerPrefix, StringComparison.Ordinal))
        {
            return AuthenticateResult.Fail("Malformed Authorization header.");
        }

        var presentedKey = header[BearerPrefix.Length..].Trim();
        if (presentedKey.Length == 0 || presentedKey.Length > MaximumPresentedKeyLength)
        {
            return AuthenticateResult.Fail("Malformed API key.");
        }

        var isValid = await credentialService.ValidateAsync(presentedKey, Context.RequestAborted);
        if (!isValid)
        {
            return AuthenticateResult.Fail("Invalid API key.");
        }

        var identity = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "instance-api-key")], ApiKeyAuthenticationDefaults.SchemeName);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), ApiKeyAuthenticationDefaults.SchemeName);
        return AuthenticateResult.Success(ticket);
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        Response.Headers["WWW-Authenticate"] = "Bearer";
        return Task.CompletedTask;
    }
}
