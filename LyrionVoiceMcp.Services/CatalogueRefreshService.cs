using LyrionVoiceMcp.Abstractions;
using LyrionVoiceMcp.Ef.Abstractions.DataAccess;
using LyrionVoiceMcp.Ef.Abstractions.Jobs;

namespace LyrionVoiceMcp.Services;

public sealed class CatalogueRefreshService(
    ICatalogueLifecycleService catalogue,
    IDbContextScopeFactory scopeFactory,
    IJobRepository jobRepository,
    IJobService jobService,
    IJobLifecycleGate lifecycleGate,
    TimeProvider timeProvider) : ICatalogueRefreshService
{
    public async Task<CatalogueStatus> GetStatusAsync(CancellationToken cancellationToken) => new(
        await catalogue.GetSummaryAsync(cancellationToken),
        await GetLatestAsync(cancellationToken));

    public Task<CatalogueRefreshOutcome> RefreshAsync(CancellationToken cancellationToken) =>
        lifecycleGate.ExecuteAsync(token => EnqueueAsync(
            $"manual:catalogue.refresh:{Guid.NewGuid():N}",
            token), cancellationToken);

    public Task<CatalogueRefreshOutcome> RefreshOnStartupAsync(
        CancellationToken cancellationToken) => lifecycleGate.ExecuteAsync(token => EnqueueAsync(
            $"startup:catalogue.refresh:{Guid.NewGuid():N}",
            token), cancellationToken);

    private async Task<CatalogueRefreshOutcome> EnqueueAsync(
        string correlationId,
        CancellationToken cancellationToken)
    {
        if (await GetLatestActiveAsync(cancellationToken) is not null)
        {
            return new CatalogueRefreshAlreadyRunning(await GetStatusAsync(cancellationToken));
        }

        var outcome = await jobService.EnqueueAsync(
            new CreateJob(
                JobTypes.CatalogueRefresh,
                "{}",
                timeProvider.GetUtcNow(),
                correlationId),
            cancellationToken);
        return outcome switch
        {
            JobEnqueued => new CatalogueRefreshStarted(await GetStatusAsync(cancellationToken)),
            JobEnqueueRejected => new CatalogueRefreshAlreadyRunning(
                await GetStatusAsync(cancellationToken)),
            _ => new CatalogueRefreshFailed(await GetStatusAsync(cancellationToken))
        };
    }

    private async Task<Job?> GetLatestActiveAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateReadOnly();
        var entity = await jobRepository.GetLatestActiveByTypeAsync(
            JobTypes.CatalogueRefresh,
            cancellationToken);
        return entity is null ? null : OperationalEntityMapper.ToModel(entity);
    }

    private async Task<Job?> GetLatestAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateReadOnly();
        var entity = await jobRepository.GetLatestByTypeAsync(
            JobTypes.CatalogueRefresh,
            cancellationToken);
        return entity is null ? null : OperationalEntityMapper.ToModel(entity);
    }
}
