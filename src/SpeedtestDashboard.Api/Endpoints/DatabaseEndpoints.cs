using SpeedtestDashboard.Api.Authentication;
using SpeedtestDashboard.Infrastructure.Persistence;

namespace SpeedtestDashboard.Api.Endpoints;

public static class DatabaseEndpoints
{
    public static IEndpointRouteBuilder MapDatabaseEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/database/storage", GetStorage)
            .WithName("GetDatabaseStorage")
            .WithTags("Database")
            .Produces<DatabaseStorageResponse>();

        endpoints.MapPost("/api/database/compact", CompactAsync)
            .RequireCsrf()
            .WithName("CompactDatabase")
            .WithTags("Database")
            .Produces<DatabaseCompactionResponse>();

        return endpoints;
    }

    private static IResult GetStorage(DatabaseMaintenanceService maintenance) =>
        Results.Ok(DatabaseStorageResponse.From(maintenance.GetStorageInfo()));

    private static async Task<IResult> CompactAsync(
        DatabaseMaintenanceService maintenance,
        CancellationToken cancellationToken)
    {
        var result = await maintenance.CompactAsync(cancellationToken);
        return Results.Ok(new DatabaseCompactionResponse(
            DatabaseStorageResponse.From(result.Before),
            DatabaseStorageResponse.From(result.After)));
    }
}

public sealed record DatabaseStorageResponse(
    long DatabaseBytes,
    long WriteAheadLogBytes,
    long SharedMemoryBytes,
    long TotalBytes)
{
    public static DatabaseStorageResponse From(DatabaseStorageInfo storage) => new(
        storage.DatabaseBytes,
        storage.WriteAheadLogBytes,
        storage.SharedMemoryBytes,
        storage.TotalBytes);
}

public sealed record DatabaseCompactionResponse(DatabaseStorageResponse Before, DatabaseStorageResponse After);
