namespace SpeedtestDashboard.Api.Endpoints;

internal static class ProblemResponses
{
    public static IResult BadRequest(string code, string detail) => Problem(
        StatusCodes.Status400BadRequest,
        "Invalid request",
        code,
        detail);

    public static IResult NotFound(string code, string detail) => Problem(
        StatusCodes.Status404NotFound,
        "Resource not found",
        code,
        detail);

    public static IResult Conflict(string code, string detail) => Problem(
        StatusCodes.Status409Conflict,
        "Request conflict",
        code,
        detail);

    public static IResult Internal(string code, string detail) => Problem(
        StatusCodes.Status500InternalServerError,
        "Operation failed",
        code,
        detail);

    public static IResult TooManyRequests(string code, string detail, int retryAfterSeconds, HttpContext context)
    {
        context.Response.Headers.RetryAfter = retryAfterSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return Problem(
            StatusCodes.Status429TooManyRequests,
            "Request throttled",
            code,
            detail,
            new Dictionary<string, object?> { ["retryAfterSeconds"] = retryAfterSeconds });
    }

    private static IResult Problem(
        int status,
        string title,
        string code,
        string detail,
        Dictionary<string, object?>? additionalExtensions = null)
    {
        var extensions = additionalExtensions ?? [];
        extensions["code"] = code;
        return Results.Problem(statusCode: status, title: title, detail: detail, extensions: extensions);
    }
}
