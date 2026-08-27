using LyrionVoiceMcp.Abstractions;
using Microsoft.Extensions.Logging;

namespace LyrionVoiceMcp.Services;

public sealed class CatalogueSearchAvailabilityService(
    ICatalogueLifecycleService catalogue,
    ICatalogueRefreshService catalogueRefresh,
    ISearchIndexService searchIndexes,
    ILogger<CatalogueSearchAvailabilityService> logger)
    : ICatalogueSearchAvailabilityService
{
    public async Task<string> DescribeUnavailableAsync(
        string fallbackMessage,
        CancellationToken cancellationToken)
    {
        try
        {
            var refresh = await catalogueRefresh.GetStatusAsync(cancellationToken);
            var state = await catalogue.GetStateAsync(cancellationToken);
            if (refresh.LatestRefresh?.Status is JobStatus.Pending or JobStatus.Running
                || state?.Status == CatalogueStateStatus.Running)
            {
                return "The music catalogue is being prepared; search will become available after indexing completes.";
            }

            var index = await searchIndexes.GetAsync(cancellationToken);
            if (index.LatestJob?.Status is JobStatus.Pending or JobStatus.Running)
            {
                return "The music catalogue has been imported and the search index is being prepared; try again later.";
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "Could not determine the current catalogue search preparation phase.");
        }

        return fallbackMessage;
    }
}
