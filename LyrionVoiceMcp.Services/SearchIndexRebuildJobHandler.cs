using System.Text.Json;
using LyrionVoiceMcp.Abstractions;

namespace LyrionVoiceMcp.Services;

public sealed class SearchIndexRebuildJobHandler(
    ICatalogueLifecycleService catalogue,
    ISearchIndexBuilder builder,
    IJobLogWriter logs) : JobHandlerBase<SearchIndexRebuildPayload>
{
    public override string Type => JobTypes.SearchIndexRebuild;

    protected override async Task<JobHandlerResult> HandleAsync(
        JobContext context,
        SearchIndexRebuildPayload payload,
        CancellationToken cancellationToken)
    {
        var state = await catalogue.GetStateAsync(cancellationToken);
        if (state is null
            || state.Status != CatalogueStateStatus.Succeeded
            || state.Summary is null)
        {
            return JobHandlerResult.Failed(
                "The catalogue has not completed successfully.");
        }

        if (!string.Equals(
                state.RefreshId,
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
            new { payload.CatalogueRefreshId },
            cancellationToken);
        var result = await builder.RebuildAsync(
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
