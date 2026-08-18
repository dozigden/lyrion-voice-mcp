using System.Text.Json;
using LyrionVoiceMcp.Abstractions;
using LyrionVoiceMcp.Ef.Abstractions.DataAccess;
using LyrionVoiceMcp.Ef.Abstractions.Jobs;

namespace LyrionVoiceMcp.Services;

public sealed class SearchIndexService(
    ISearchIndexBuilder builder,
    IMediaCatalogueStore catalogueStore,
    IDbContextScopeFactory scopeFactory,
    IJobRepository jobRepository,
    IJobService jobService,
    IJobLifecycleGate lifecycleGate,
    TimeProvider timeProvider) : ISearchIndexService
{
    public async Task<SearchIndexStatus> GetAsync(CancellationToken cancellationToken) => new(
        builder.Descriptor.Name,
        await builder.GetArtifactAsync(cancellationToken),
        await GetLatestJobAsync(cancellationToken));

    public Task<SearchIndexRebuildOutcome> RebuildAsync(CancellationToken cancellationToken) =>
        lifecycleGate.ExecuteAsync<SearchIndexRebuildOutcome>(async token =>
        {
            if (await GetLatestActiveByTypeAsync(JobTypes.CatalogueRefresh, token) is not null)
            {
                return new SearchIndexRebuildRejected(
                    "The catalogue is currently being refreshed.");
            }

            var catalogue = await catalogueStore.GetStateAsync(token);
            if (catalogue is null
                || catalogue.Status != CatalogueStateStatus.Succeeded
                || catalogue.Summary is null)
            {
                return new SearchIndexRebuildRejected(
                    "The catalogue has not completed successfully.");
            }

            if (await GetActiveJobAsync(token) is not null)
            {
                return new SearchIndexRebuildRejected(
                    "A rebuild for this search index is already queued or running.");
            }

            var outcome = await EnqueueAsync(
                catalogue.RefreshId,
                $"search-index:production:manual:{Guid.NewGuid():N}",
                token);
            if (outcome is not JobEnqueued enqueued)
            {
                var message = outcome is JobEnqueueRejected rejected
                    ? rejected.Message
                    : "The search-index rebuild could not be queued.";
                return new SearchIndexRebuildRejected(message);
            }

            return new SearchIndexRebuildStarted(new SearchIndexStatus(
                builder.Descriptor.Name,
                await builder.GetArtifactAsync(token),
                enqueued.Job));
        }, cancellationToken);

    public Task<long?> EnqueueForCatalogueAsync(
        string catalogueRefreshId,
        CancellationToken cancellationToken) => lifecycleGate.ExecuteAsync<long?>(async token =>
        {
            if (await GetActiveJobAsync(token) is not null)
            {
                return null;
            }

            var outcome = await EnqueueAsync(
                catalogueRefreshId,
                $"search-index:production:catalogue:{catalogueRefreshId}",
                token);
            return outcome is JobEnqueued enqueued ? enqueued.Job.Id : null;
        }, cancellationToken);

    private Task<JobEnqueueOutcome> EnqueueAsync(
        string catalogueRefreshId,
        string correlationId,
        CancellationToken cancellationToken) => jobService.EnqueueAsync(
        new CreateJob(
            JobTypes.SearchIndexRebuild,
            JsonSerializer.Serialize(new SearchIndexRebuildPayload(catalogueRefreshId)),
            timeProvider.GetUtcNow(),
            correlationId),
        cancellationToken);

    private async Task<Job?> GetLatestJobAsync(CancellationToken cancellationToken) =>
        await GetActiveJobAsync(cancellationToken)
        ?? await GetLatestStartedAsync(cancellationToken);

    private Task<Job?> GetActiveJobAsync(CancellationToken cancellationToken) =>
        GetByCorrelationPrefixesAsync(true, cancellationToken);

    private Task<Job?> GetLatestStartedAsync(CancellationToken cancellationToken) =>
        GetByCorrelationPrefixesAsync(false, cancellationToken);

    private async Task<Job?> GetByCorrelationPrefixesAsync(
        bool active,
        CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateReadOnly();
        var entity = active
            ? await jobRepository.GetLatestActiveByCorrelationPrefixesAsync(
                Prefix,
                Prefix,
                cancellationToken)
            : await jobRepository.GetLatestStartedByCorrelationPrefixesAsync(
                Prefix,
                Prefix,
                cancellationToken);
        return entity is null ? null : OperationalEntityMapper.ToModel(entity);
    }

    private async Task<Job?> GetLatestActiveByTypeAsync(
        string type,
        CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateReadOnly();
        var entity = await jobRepository.GetLatestActiveByTypeAsync(type, cancellationToken);
        return entity is null ? null : OperationalEntityMapper.ToModel(entity);
    }

    private const string Prefix = "search-index:production:";
}
