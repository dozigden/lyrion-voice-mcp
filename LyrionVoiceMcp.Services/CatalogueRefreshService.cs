using LyrionVoiceMcp.Abstractions;

namespace LyrionVoiceMcp.Services;

public sealed class CatalogueRefreshService(
    IMediaCatalogueStore catalogueStore,
    IJobStore jobStore,
    IJobService jobService,
    TimeProvider timeProvider) : ICatalogueRefreshService
{
    public async Task<CatalogueStatus> GetStatusAsync(CancellationToken cancellationToken) => new(
        await catalogueStore.GetSummaryAsync(cancellationToken),
        await jobStore.GetLatestByTypeAsync(JobTypes.CatalogueRefresh, cancellationToken));

    public async Task<CatalogueRefreshOutcome> RefreshAsync(CancellationToken cancellationToken)
    {
        if (await jobStore.GetLatestActiveByTypeAsync(
                JobTypes.CatalogueRefresh,
                cancellationToken) is not null)
        {
            return new CatalogueRefreshAlreadyRunning(await GetStatusAsync(cancellationToken));
        }

        var outcome = await jobService.EnqueueAsync(
            new CreateJob(
                JobTypes.CatalogueRefresh,
                "{}",
                timeProvider.GetUtcNow(),
                $"manual:catalogue.refresh:{Guid.NewGuid():N}"),
            cancellationToken);
        return outcome switch
        {
            JobEnqueued => new CatalogueRefreshStarted(await GetStatusAsync(cancellationToken)),
            JobEnqueueRejected => new CatalogueRefreshAlreadyRunning(
                await GetStatusAsync(cancellationToken)),
            _ => new CatalogueRefreshFailed(await GetStatusAsync(cancellationToken))
        };
    }
}
