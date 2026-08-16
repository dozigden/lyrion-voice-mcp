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
    public async Task<IReadOnlyList<SearchIndexStatus>> ListAsync(
        CancellationToken cancellationToken)
    {
        var results = new List<SearchIndexStatus>(builder.Resolvers.Count);
        foreach (var resolver in builder.Resolvers)
        {
            results.Add(new SearchIndexStatus(
                resolver,
                await builder.GetArtifactAsync(resolver, cancellationToken),
                await GetLatestJobAsync(resolver, cancellationToken)));
        }

        return results;
    }

    public Task<SearchIndexRebuildOutcome> RebuildAsync(
        string resolver,
        CancellationToken cancellationToken) =>
        lifecycleGate.ExecuteAsync<SearchIndexRebuildOutcome>(async token =>
        {
            if (!builder.Resolvers.Contains(resolver, StringComparer.Ordinal))
            {
                return new SearchIndexRebuildRejected(
                    $"Search-index resolver '{resolver}' is not supported.");
            }

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

            if (await GetActiveJobAsync(resolver, token) is not null)
            {
                return new SearchIndexRebuildRejected(
                    "A rebuild for this search index is already queued or running.");
            }

            var outcome = await EnqueueAsync(
                resolver,
                catalogue.RefreshId,
                $"search-index:{resolver}:manual:{Guid.NewGuid():N}",
                token);
            if (outcome is not JobEnqueued enqueued)
            {
                var message = outcome is JobEnqueueRejected rejected
                    ? rejected.Message
                    : "The search-index rebuild could not be queued.";
                return new SearchIndexRebuildRejected(message);
            }

            return new SearchIndexRebuildStarted(new SearchIndexStatus(
                resolver,
                await builder.GetArtifactAsync(resolver, token),
                enqueued.Job));
        }, cancellationToken);

    public async Task<IReadOnlyList<long>> EnqueueForCatalogueAsync(
        string catalogueRefreshId,
        CancellationToken cancellationToken)
    {
        var jobIds = new List<long>(builder.Resolvers.Count);
        foreach (var resolver in builder.Resolvers)
        {
            var outcome = await EnqueueAsync(
                resolver,
                catalogueRefreshId,
                $"search-index:{resolver}:catalogue:{catalogueRefreshId}",
                cancellationToken);
            if (outcome is JobEnqueued enqueued)
            {
                jobIds.Add(enqueued.Job.Id);
            }
        }

        return jobIds;
    }

    private Task<JobEnqueueOutcome> EnqueueAsync(
        string resolver,
        string catalogueRefreshId,
        string correlationId,
        CancellationToken cancellationToken) =>
        jobService.EnqueueAsync(
            new CreateJob(
                JobTypes.SearchIndexRebuild,
                JsonSerializer.Serialize(new SearchIndexRebuildPayload(
                    resolver,
                    catalogueRefreshId)),
                timeProvider.GetUtcNow(),
                correlationId),
            cancellationToken);

    private async Task<Job?> GetLatestJobAsync(
        string resolver,
        CancellationToken cancellationToken) =>
        await GetActiveJobAsync(resolver, cancellationToken)
        ?? await jobStore.GetLatestStartedByCorrelationPrefixesAsync(
            Prefix(resolver),
            Prefix(resolver),
            cancellationToken);

    private Task<Job?> GetActiveJobAsync(
        string resolver,
        CancellationToken cancellationToken) =>
        jobStore.GetLatestActiveByCorrelationPrefixesAsync(
            Prefix(resolver),
            Prefix(resolver),
            cancellationToken);

    private static string Prefix(string resolver) => $"search-index:{resolver}:";
}
