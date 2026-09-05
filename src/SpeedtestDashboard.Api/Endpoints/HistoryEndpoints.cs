using System.Text.Json;
using SpeedtestDashboard.Core.History;
using SpeedtestDashboard.Core.Providers;
using SpeedtestDashboard.Core.Tests;

namespace SpeedtestDashboard.Api.Endpoints;

public static class HistoryEndpoints
{
    private const int DefaultLimit = 50;
    private const int MaximumLimit = 200;

    public static IEndpointRouteBuilder MapHistoryEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/history", ListAsync)
            .WithName("GetSpeedTestHistory")
            .WithTags("History")
            .Produces<HistoryListResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest);

        endpoints.MapGet("/api/history/{id:long}", GetAsync)
            .WithName("GetSpeedTestHistoryRecord")
            .WithTags("History")
            .Produces<HistoryDetailResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        endpoints.MapDelete("/api/history/{id:long}", DeleteAsync)
            .WithName("DeleteSpeedTestHistoryRecord")
            .WithTags("History")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound);
        return endpoints;
    }

    private static async Task<IResult> ListAsync(
        HttpRequest request,
        ISpeedTestHistoryStore history,
        CancellationToken cancellationToken)
    {
        if (!TryParseQuery(request.Query, out var query, out var error))
        {
            return ProblemResponses.BadRequest("invalid_history_query", error);
        }

        try
        {
            var page = await history.ListAsync(query, cancellationToken);
            return Results.Ok(new HistoryListResponse(
                page.Items.Select(HistoryListItemResponse.From).ToArray(),
                page.NextCursor));
        }
        catch (ArgumentException)
        {
            return ProblemResponses.BadRequest("invalid_history_cursor", "The history cursor is invalid.");
        }
    }

    private static async Task<IResult> GetAsync(
        long id,
        ISpeedTestHistoryStore history,
        CancellationToken cancellationToken)
    {
        var record = await history.GetAsync(id, cancellationToken);
        return record is null
            ? ProblemResponses.NotFound("history_not_found", "The requested history record was not found.")
            : Results.Ok(HistoryDetailResponse.From(record));
    }

    private static async Task<IResult> DeleteAsync(
        long id,
        ISpeedTestHistoryStore history,
        ISpeedTestJobStore jobStore,
        CancellationToken cancellationToken)
    {
        var result = await history.DeleteAsync(id, cancellationToken);
        if (!result.Deleted || result.JobId is null)
        {
            return ProblemResponses.NotFound("history_not_found", "The requested history record was not found.");
        }

        jobStore.TryRemoveTerminal(result.JobId.Value);
        return Results.NoContent();
    }

    private static bool TryParseQuery(
        IQueryCollection values,
        out HistoryQuery query,
        out string error)
    {
        query = null!;
        error = string.Empty;
        ProviderId? providerId = null;
        if (values.TryGetValue("providerId", out var providerValue) && !string.IsNullOrWhiteSpace(providerValue))
        {
            if (!ProviderId.TryParse(providerValue.ToString(), out var parsedProvider))
            {
                error = "Provider ID must be a valid lowercase provider identifier.";
                return false;
            }
            providerId = parsedProvider;
        }

        SpeedTestJobStatus? status = null;
        if (values.TryGetValue("status", out var statusValue) && !string.IsNullOrWhiteSpace(statusValue))
        {
            if (!Enum.TryParse<SpeedTestJobStatus>(statusValue.ToString(), ignoreCase: true, out var parsedStatus) ||
                parsedStatus is not (SpeedTestJobStatus.Completed or SpeedTestJobStatus.Failed or SpeedTestJobStatus.Cancelled))
            {
                error = "Status must be completed, failed, or cancelled.";
                return false;
            }
            status = parsedStatus;
        }

        if (!TryParseUtc(values, "fromUtc", out var fromUtc, out error) ||
            !TryParseUtc(values, "toUtc", out var toUtc, out error))
        {
            return false;
        }
        if (fromUtc > toUtc)
        {
            error = "fromUtc must be earlier than or equal to toUtc.";
            return false;
        }

        var limit = DefaultLimit;
        if (values.TryGetValue("limit", out var limitValue) &&
            (!int.TryParse(limitValue.ToString(), out limit) || limit is < 1 or > MaximumLimit))
        {
            error = $"Limit must be between 1 and {MaximumLimit}.";
            return false;
        }

        var cursor = values.TryGetValue("cursor", out var cursorValue) && !string.IsNullOrWhiteSpace(cursorValue)
            ? cursorValue.ToString()
            : null;
        query = new HistoryQuery(providerId, status, fromUtc, toUtc, limit, cursor);
        return true;
    }

    private static bool TryParseUtc(
        IQueryCollection values,
        string name,
        out DateTimeOffset? parsed,
        out string error)
    {
        parsed = null;
        error = string.Empty;
        if (!values.TryGetValue(name, out var value) || string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        var text = value.ToString();
        if (!(text.EndsWith('Z') || text.EndsWith("+00:00", StringComparison.Ordinal)) ||
            !DateTimeOffset.TryParse(
                text,
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.RoundtripKind,
                out var timestamp) || timestamp.Offset != TimeSpan.Zero)
        {
            error = $"{name} must be a valid UTC timestamp.";
            return false;
        }

        parsed = timestamp;
        return true;
    }
}

public sealed record HistoryListResponse(
    IReadOnlyList<HistoryListItemResponse> Items,
    string? NextCursor);

public sealed record HistoryListItemResponse(
    long Id,
    Guid JobId,
    string ProviderId,
    string Status,
    DateTimeOffset CompletedAtUtc,
    string? ServerName,
    string? ServerLocation,
    decimal? DownloadMbps,
    decimal? UploadMbps,
    decimal? LatencyMilliseconds,
    decimal? JitterMilliseconds,
    decimal? PacketLossPercent,
    string? Ipv4Address,
    string? Ipv6Address,
    SpeedTestFailureResponse? Failure)
{
    public static HistoryListItemResponse From(SpeedTestHistoryRecord record) => new(
        record.Id,
        record.JobId,
        record.ProviderId.Value,
        StatusValue(record.Status),
        record.CompletedAtUtc,
        record.ServerName,
        record.ServerLocation,
        record.DownloadMbps,
        record.UploadMbps,
        record.LatencyMilliseconds,
        record.JitterMilliseconds,
        record.PacketLossPercent,
        record.EgressIdentity?.IPv4?.Address,
        record.EgressIdentity?.IPv6?.Address,
        record.Failure is null ? null : new SpeedTestFailureResponse(record.Failure.Code, record.Failure.Message));

    internal static string StatusValue(SpeedTestJobStatus status) => status.ToString().ToLowerInvariant();
}

public sealed record HistoryDetailResponse(
    long Id,
    Guid JobId,
    string ProviderId,
    string Status,
    DateTimeOffset QueuedAtUtc,
    DateTimeOffset? StartedAtUtc,
    DateTimeOffset CompletedAtUtc,
    string? RequestedServerId,
    SpeedTestResultResponse? Result,
    NetworkIdentityResponse? EgressIdentity,
    SpeedTestFailureResponse? Failure,
    JsonElement? ProviderMetadata)
{
    public static HistoryDetailResponse From(SpeedTestHistoryRecord record) => new(
        record.Id,
        record.JobId,
        record.ProviderId.Value,
        HistoryListItemResponse.StatusValue(record.Status),
        record.QueuedAtUtc,
        record.StartedAtUtc,
        record.CompletedAtUtc,
        record.RequestedServerId,
        record.Status == SpeedTestJobStatus.Completed
            ? new SpeedTestResultResponse(
                record.ProviderId.Value,
                record.ServerId,
                record.ServerName,
                record.ServerLocation,
                record.DownloadMbps,
                record.UploadMbps,
                record.LatencyMilliseconds,
                record.JitterMilliseconds,
                record.PacketLossPercent,
                record.ResultUrl)
            : null,
        record.EgressIdentity is null ? null : NetworkIdentityResponse.From(record.EgressIdentity),
        record.Failure is null ? null : new SpeedTestFailureResponse(record.Failure.Code, record.Failure.Message),
        ParseMetadata(record.ProviderMetadataJson));

    private static JsonElement? ParseMetadata(string? json)
    {
        if (json is null)
        {
            return null;
        }

        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
