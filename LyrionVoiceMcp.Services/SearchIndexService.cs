using System.Text.Json;
using LyrionVoiceMcp.Abstractions;

namespace LyrionVoiceMcp.Services;

public sealed class SearchIndexService(
    ISearchIndexBuilder builder,
    IMediaCatalogueStore catalogueStore,
    IJobStore jobStore,
    IJobService jobService,
    IJobLifecycleGate lifecycleGate,
    TimeProvider timeProvider) : ISearchIndexService
{
    private const string Resolver = "catalogue-phuzzy-sqlite";

    public async Task<SearchIndexStatus> GetAsync(CancellationToken cancellationToken) =>
        new(
            Resolver,
            await builder.GetArtifactAsync(cancellationToken),
            await GetLatestJobAsync(cancellationToken));

    public Task<SearchIndexRebuildOutcome> RebuildAsync(CancellationToken cancellationToken) =>
        lifecycleGate.ExecuteAsync<SearchIndexRebuildOutcome>(async token =>
        {
            if (await jobStore.GetLatestActiveByTypeAsync(
                    JobTypes.CatalogueRefresh,
                    token) is not null)
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
                Resolver,
                await builder.GetArtifactAsync(token),
                enqueued.Job));
        }, cancellationToken);

    public async Task<long?> EnqueueForCatalogueAsync(
        string catalogueRefreshId,
        CancellationToken cancellationToken)
    {
        var outcome = await EnqueueAsync(
            catalogueRefreshId,
            $"search-index:production:catalogue:{catalogueRefreshId}",
            cancellationToken);
        return outcome is JobEnqueued enqueued ? enqueued.Job.Id : null;
    }

    private Task<JobEnqueueOutcome> EnqueueAsync(
        string catalogueRefreshId,
        string correlationId,
        CancellationToken cancellationToken) =>
        jobService.EnqueueAsync(
            new CreateJob(
                JobTypes.SearchIndexRebuild,
                JsonSerializer.Serialize(new SearchIndexRebuildPayload(catalogueRefreshId)),
                timeProvider.GetUtcNow(),
                correlationId),
            cancellationToken);

    private async Task<Job?> GetLatestJobAsync(CancellationToken cancellationToken) =>
        await GetActiveJobAsync(cancellationToken)
        ?? await jobStore.GetLatestStartedByCorrelationPrefixesAsync(
            Prefix,
            Prefix,
            cancellationToken);

    private Task<Job?> GetActiveJobAsync(CancellationToken cancellationToken) =>
        jobStore.GetLatestActiveByCorrelationPrefixesAsync(
            Prefix,
            Prefix,
            cancellationToken);

    private const string Prefix = "search-index:production:";
}
