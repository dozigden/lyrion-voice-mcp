using System.Text.Json;
using LyrionVoiceMcp.Abstractions;

namespace LyrionVoiceMcp.Services;

public sealed class CatalogueRefreshJobHandler(
    ICatalogueSourceReader sourceReader,
    IMediaCatalogueStore store,
    ISearchIndexService searchIndexes,
    IJobLogWriter logs,
    TimeProvider timeProvider) : JobHandlerBase<CatalogueRefreshJobHandler.Payload>
{
    public override string Type => JobTypes.CatalogueRefresh;

    protected override async Task<JobHandlerResult> HandleAsync(
        JobContext context,
        Payload payload,
        CancellationToken cancellationToken)
    {
        var refreshId = $"job-{context.JobId}";
        var sink = new CatalogueJobLogSink(context.JobId, logs);
        CatalogueRefreshCompletion completion;
        var refreshStarted = false;
        try
        {
            await store.BeginRefreshAsync(
                refreshId,
                timeProvider.GetUtcNow(),
                cancellationToken);
            refreshStarted = true;
            await logs.WriteAsync(
                context.JobId,
                JobLogLevel.Information,
                "Catalogue refresh started.",
                null,
                cancellationToken);
            var source = await sourceReader.ReadAsync(
                refreshId,
                store,
                sink,
                cancellationToken);
            completion = await store.CompleteRefreshAsync(
                refreshId,
                source,
                timeProvider.GetUtcNow(),
                sink.WarningCount,
                cancellationToken);
        }
        catch (OperationCanceledException) when (
            refreshStarted && cancellationToken.IsCancellationRequested)
        {
            await store.FinishRefreshAsync(
                refreshId,
                CatalogueStateStatus.Cancelled,
                timeProvider.GetUtcNow(),
                CancellationToken.None);
            throw;
        }
        catch when (refreshStarted)
        {
            await store.FinishRefreshAsync(
                refreshId,
                CatalogueStateStatus.Failed,
                timeProvider.GetUtcNow(),
                CancellationToken.None);
            throw;
        }

        foreach (var warning in completion.Warnings)
        {
            await sink.WriteAsync(
                warning.Level,
                warning.Message,
                warning.ProcessedCount,
                warning.TotalCount,
                cancellationToken);
        }

        var indexJobIds = await searchIndexes.EnqueueForCatalogueAsync(
            refreshId,
            cancellationToken);
        await logs.WriteAsync(
            context.JobId,
            JobLogLevel.Information,
            "Queued search-index rebuilds.",
            new { jobIds = indexJobIds },
            cancellationToken);
        await logs.WriteAsync(
            context.JobId,
            JobLogLevel.Information,
            "Catalogue refresh completed.",
            completion.Summary,
            cancellationToken);
        return JobHandlerResult.Succeeded(JsonSerializer.Serialize(new
        {
            catalogue = completion.Summary,
            searchIndexJobIds = indexJobIds
        }));
    }

    public sealed record Payload;

    private sealed class CatalogueJobLogSink(
        long jobId,
        IJobLogWriter logs) : ICatalogueRefreshLogSink
    {
        public int WarningCount { get; private set; }

        public Task WriteAsync(
            CatalogueRefreshLogLevel level,
            string message,
            int? processedCount,
            int? totalCount,
            CancellationToken cancellationToken)
        {
            if (level == CatalogueRefreshLogLevel.Warning)
            {
                WarningCount++;
            }

            return logs.WriteAsync(
                jobId,
                level switch
                {
                    CatalogueRefreshLogLevel.Information => JobLogLevel.Information,
                    CatalogueRefreshLogLevel.Warning => JobLogLevel.Warning,
                    CatalogueRefreshLogLevel.Error => JobLogLevel.Error,
                    _ => throw new InvalidOperationException("Unknown catalogue log level.")
                },
                message,
                processedCount is null && totalCount is null
                    ? null
                    : new { processedCount, totalCount },
                cancellationToken);
        }
    }
}
