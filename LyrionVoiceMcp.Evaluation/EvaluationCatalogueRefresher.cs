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
        var startedAt = timeProvider.GetUtcNow();
        await store.BeginRefreshAsync(refreshId, startedAt, cancellationToken);

        try
        {
            var source = await sourceReader.ReadAsync(refreshId, store, cancellationToken);
            var completedAt = timeProvider.GetUtcNow();
            return await store.CompleteRefreshAsync(
                refreshId,
                source,
                completedAt,
                DurationMilliseconds(startedAt, completedAt),
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await TryRecordFailureAsync(
                refreshId,
                CatalogueRefreshRunStatus.Cancelled,
                startedAt,
                "Evaluation catalogue refresh was cancelled.");
            throw;
        }
        catch
        {
            await TryRecordFailureAsync(
                refreshId,
                CatalogueRefreshRunStatus.Failed,
                startedAt,
                "Evaluation catalogue refresh failed.");
            throw;
        }
    }

    private async Task TryRecordFailureAsync(
        string refreshId,
        CatalogueRefreshRunStatus status,
        DateTimeOffset startedAt,
        string message)
    {
        try
        {
            var completedAt = timeProvider.GetUtcNow();
            await store.CompleteFailedRefreshAsync(
                refreshId,
                status,
                completedAt,
                DurationMilliseconds(startedAt, completedAt),
                message,
                CancellationToken.None);
        }
        catch
        {
            // Preserve the source failure. A later initialisation marks an abandoned run interrupted.
        }
    }

    private static long DurationMilliseconds(
        DateTimeOffset startedAt,
        DateTimeOffset completedAt) =>
        Math.Max(0, (long)(completedAt - startedAt).TotalMilliseconds);
}
