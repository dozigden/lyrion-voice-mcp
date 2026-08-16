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
        status.Summary is null
            ? null
            : new CatalogueSummaryResponse(
                status.Summary.SourceId,
                status.Summary.Provider,
                status.Summary.SourceRevision,
                status.Summary.SourceVersion,
                status.Summary.CapturedAt,
                status.Summary.SourceLastScanAt,
                status.Summary.RefreshedAt,
                status.Summary.ArtistCount,
                status.Summary.AlbumCount,
                status.Summary.GenreCount,
                status.Summary.TrackCount,
                status.Summary.VirtualLibraryCount,
                status.Summary.WarningCount),
        status.LatestRefresh is null
            ? null
            : new CatalogueRefreshRunResponse(
                status.LatestRefresh.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ToText(status.LatestRefresh.Status),
                status.LatestRefresh.StartedAt ?? status.LatestRefresh.CreatedAt,
                status.LatestRefresh.CompletedAt,
                DurationMilliseconds(status.LatestRefresh),
                status.LatestRefresh.ErrorMessage,
                []));

    private static string ToText(JobStatus status) => status switch
    {
        JobStatus.Pending or JobStatus.Running => "running",
        JobStatus.Completed => "succeeded",
        JobStatus.Failed => "failed",
        JobStatus.Cancelled => "cancelled",
        _ => throw new InvalidOperationException("Unknown catalogue job status.")
    };

    private static long? DurationMilliseconds(Job job) =>
        job.StartedAt is { } startedAt && job.CompletedAt is { } completedAt
            ? Math.Max(0, (long)(completedAt - startedAt).TotalMilliseconds)
            : null;
}
