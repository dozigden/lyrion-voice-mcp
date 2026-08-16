using LyrionVoiceMcp.Abstractions;

namespace LyrionVoiceMcp.Evaluation;

public sealed class EvaluationCatalogueRefresher(
    IMediaCatalogueStore store,
    ICatalogueSourceReader sourceReader,
    TimeProvider timeProvider)
{
    public async Task<CatalogueSummary> RefreshAsync(CancellationToken cancellationToken)
    {
        await store.InitialiseAsync(cancellationToken);
        var refreshId = Guid.NewGuid().ToString("N");
        var log = new EvaluationLogSink();
        await store.BeginRefreshAsync(
            refreshId,
            timeProvider.GetUtcNow(),
            cancellationToken);
        try
        {
            var source = await sourceReader.ReadAsync(refreshId, store, log, cancellationToken);
            var completion = await store.CompleteRefreshAsync(
                refreshId,
                source,
                timeProvider.GetUtcNow(),
                log.WarningCount,
                cancellationToken);
            return completion.Summary;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await store.FinishRefreshAsync(
                refreshId,
                CatalogueStateStatus.Cancelled,
                timeProvider.GetUtcNow(),
                CancellationToken.None);
            throw;
        }
        catch
        {
            await store.FinishRefreshAsync(
                refreshId,
                CatalogueStateStatus.Failed,
                timeProvider.GetUtcNow(),
                CancellationToken.None);
            throw;
        }
    }

    private sealed class EvaluationLogSink : ICatalogueRefreshLogSink
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

            return Task.CompletedTask;
        }
    }
}
