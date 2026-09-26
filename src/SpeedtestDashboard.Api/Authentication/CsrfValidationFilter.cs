using Microsoft.AspNetCore.Antiforgery;

namespace SpeedtestDashboard.Api.Authentication;

internal sealed class CsrfValidationFilter(
    IAntiforgery antiforgery,
    DashboardAuthenticationState state,
    ILogger<CsrfValidationFilter> logger) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var path = context.HttpContext.Request.Path;
        var isSetup = path.Equals("/api/auth/setup");
        var alwaysValidate = isSetup || path.Equals("/api/auth/preferences");
        if (!state.IsEnabled && !alwaysValidate)
        {
            return await next(context);
        }

        try
        {
            await antiforgery.ValidateRequestAsync(context.HttpContext);
        }
        catch (AntiforgeryValidationException exception)
        {
            logger.LogInformation(exception, "An unsafe browser request failed antiforgery validation.");
            return Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Invalid request",
                detail: "The request could not be verified. Refresh the page and try again.",
                extensions: new Dictionary<string, object?> { ["code"] = "csrf_validation_failed" });
        }

        return await next(context);
    }
}

public static class CsrfEndpointConventionExtensions
{
    public static RouteHandlerBuilder RequireCsrf(this RouteHandlerBuilder builder) =>
        builder.AddEndpointFilter<CsrfValidationFilter>();
}
