using System.Text.Json;
using LyrionVoiceMcp.Abstractions;

namespace LyrionVoiceMcp.Services;

public sealed class SearchIndexRebuildJobHandler(
    IMediaCatalogueStore catalogueStore,
    ISearchIndexBuilder builder,
    IJobLogWriter logs) : JobHandlerBase<SearchIndexRebuildPayload>
{
    public override string Type => JobTypes.SearchIndexRebuild;

    protected override async Task<JobHandlerResult> HandleAsync(
        JobContext context,
        SearchIndexRebuildPayload payload,
        CancellationToken cancellationToken)
    {
        if (!builder.Resolvers.Contains(payload.Resolver, StringComparer.Ordinal))
        {
            return JobHandlerResult.Failed(
                $"Search-index resolver '{payload.Resolver}' is not supported.");
        }

        var catalogue = await catalogueStore.GetStateAsync(cancellationToken);
        if (catalogue is null
            || catalogue.Status != CatalogueStateStatus.Succeeded
            || catalogue.Summary is null)
        {
            return JobHandlerResult.Failed(
                "The catalogue has not completed successfully.");
        }

        if (!string.Equals(
                catalogue.RefreshId,
                payload.CatalogueRefreshId,
                StringComparison.Ordinal))
        {
            return JobHandlerResult.Failed(
                "The catalogue refresh no longer matches this index job.");
        }

        await logs.WriteAsync(
            context.JobId,
            JobLogLevel.Information,
            "Search-index rebuild started.",
            new { payload.Resolver, payload.CatalogueRefreshId },
            cancellationToken);
        var result = await builder.RebuildAsync(
            payload.Resolver,
            payload.CatalogueRefreshId,
            context.JobId,
            new JobProgress(context.JobId, logs),
            cancellationToken);
        await logs.WriteAsync(
            context.JobId,
            JobLogLevel.Information,
            "Search-index rebuild completed.",
            result.Artifact,
            cancellationToken);
        return JobHandlerResult.Succeeded(JsonSerializer.Serialize(result));
    }

    private sealed class JobProgress(
        long jobId,
        IJobLogWriter logs) : ISearchIndexProgress
    {
        public Task ReportAsync(
            string message,
            object? data,
            CancellationToken cancellationToken) =>
            logs.WriteAsync(
                jobId,
                JobLogLevel.Information,
                message,
                data,
                cancellationToken);
    }
}
