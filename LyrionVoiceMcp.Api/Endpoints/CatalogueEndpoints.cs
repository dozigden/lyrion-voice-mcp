using LyrionVoiceMcp.Abstractions;
using LyrionVoiceMcp.Contracts;

namespace LyrionVoiceMcp.Api.Endpoints;

public static class CatalogueEndpoints
{
    public static IEndpointRouteBuilder MapCatalogueEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/catalogue", GetStatusAsync);
        endpoints.MapPost("/api/catalogue/refresh", RefreshAsync);
        return endpoints;
    }

    private static async Task<IResult> GetStatusAsync(
        ICatalogueRefreshService service,
        CancellationToken cancellationToken) =>
        Results.Ok(ToResponse(await service.GetStatusAsync(cancellationToken)));

    private static async Task<IResult> RefreshAsync(
        ICatalogueRefreshService service,
        CancellationToken cancellationToken)
    {
        var outcome = await service.RefreshAsync(cancellationToken);
        return outcome switch
        {
            CatalogueRefreshStarted started => Results.Accepted(
                "/api/catalogue",
                ToResponse(started.Status)),
            CatalogueRefreshAlreadyRunning running => Results.Conflict(ToResponse(running.Status)),
            CatalogueRefreshFailed failed => Results.Json(
                ToResponse(failed.Status),
                statusCode: StatusCodes.Status502BadGateway),
            _ => Results.StatusCode(StatusCodes.Status500InternalServerError)
        };
    }

    private static CatalogueStatusResponse ToResponse(CatalogueStatus status) => new(
        status.PublishedGeneration is null
            ? null
            : new PublishedCatalogueGenerationResponse(
                status.PublishedGeneration.Id,
                status.PublishedGeneration.SourceId,
                status.PublishedGeneration.SourceRevision,
                status.PublishedGeneration.SourceVersion,
                status.PublishedGeneration.CapturedAt,
                status.PublishedGeneration.SourceLastScanAt,
                status.PublishedGeneration.PublishedAt,
                status.PublishedGeneration.ArtistCount,
                status.PublishedGeneration.AlbumCount,
                status.PublishedGeneration.GenreCount,
                status.PublishedGeneration.TrackCount,
                status.PublishedGeneration.VirtualLibraryCount,
                status.PublishedGeneration.WarningCount),
        status.LatestRefresh is null
            ? null
            : new CatalogueRefreshRunResponse(
                status.LatestRefresh.Id,
                ToText(status.LatestRefresh.Status),
                status.LatestRefresh.StartedAt,
                status.LatestRefresh.CompletedAt,
                status.LatestRefresh.DurationMilliseconds,
                status.LatestRefresh.PublishedGenerationId,
                status.LatestRefresh.FailureMessage));

    private static string ToText(CatalogueRefreshRunStatus status) => status.ToString().ToLowerInvariant();
}
