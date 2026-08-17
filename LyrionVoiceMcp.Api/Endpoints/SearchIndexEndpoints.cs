using LyrionVoiceMcp.Abstractions;
using LyrionVoiceMcp.Contracts;

namespace LyrionVoiceMcp.Api.Endpoints;

public static class SearchIndexEndpoints
{
    public static IEndpointRouteBuilder MapSearchIndexEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/search/index", GetAsync);
        endpoints.MapPost("/api/search/index/rebuild", RebuildAsync);
        return endpoints;
    }

    private static async Task<IResult> GetAsync(
        ISearchIndexService service,
        CancellationToken cancellationToken) =>
        Results.Ok(ToResponse(await service.GetAsync(cancellationToken)));

    private static async Task<IResult> RebuildAsync(
        ISearchIndexService service,
        CancellationToken cancellationToken)
    {
        var outcome = await service.RebuildAsync(cancellationToken);
        return outcome switch
        {
            SearchIndexRebuildStarted started => Results.Accepted(
                $"/api/jobs/{started.Status.LatestJob!.Id}",
                ToResponse(started.Status)),
            SearchIndexRebuildRejected rejected => Results.Conflict(
                new ApiErrorResponse(rejected.Message)),
            _ => Results.StatusCode(StatusCodes.Status500InternalServerError)
        };
    }

    private static SearchIndexStatusResponse ToResponse(SearchIndexStatus status) => new(
        status.Resolver,
        status.Artifact is null
            ? null
            : new SearchIndexArtifactResponse(
                status.Artifact.ResolverVersion,
                status.Artifact.CatalogueRefreshId,
                status.Artifact.BuiltAt,
                status.Artifact.CandidateCount,
                status.Artifact.PreparationDurationMilliseconds,
                status.Artifact.IndexSizeBytes),
        status.LatestJob is null
            ? null
            : new SearchIndexJobResponse(
                status.LatestJob.Id,
                ToText(status.LatestJob.Status),
                status.LatestJob.StartedAt,
                status.LatestJob.CompletedAt,
                status.LatestJob.ErrorMessage));

    private static string ToText(JobStatus status) => status switch
    {
        JobStatus.Pending => "pending",
        JobStatus.Running => "running",
        JobStatus.Completed => "succeeded",
        JobStatus.Failed => "failed",
        JobStatus.Cancelled => "cancelled",
        _ => throw new InvalidOperationException("Unknown search-index job status.")
    };
}
