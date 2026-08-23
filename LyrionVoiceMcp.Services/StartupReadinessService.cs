using LyrionVoiceMcp.Abstractions;
using Microsoft.Extensions.Logging;

namespace LyrionVoiceMcp.Services;

public sealed class StartupReadinessService(
    ICatalogueLifecycleService catalogue,
    ICatalogueRefreshService catalogueRefresh,
    ISearchIndexService searchIndexes,
    OperationalSchedulePolicy schedules,
    ILogger<StartupReadinessService> logger)
{
    public async Task CheckAsync(CancellationToken cancellationToken)
    {
        var catalogueStatus = await catalogueRefresh.GetStatusAsync(cancellationToken);
        if (catalogueStatus.LatestRefresh?.Status is JobStatus.Pending or JobStatus.Running)
        {
            logger.LogInformation(
                "Startup readiness found catalogue refresh job {JobId} already {Status}.",
                catalogueStatus.LatestRefresh.Id,
                catalogueStatus.LatestRefresh.Status);
            return;
        }

        var state = await catalogue.GetStateAsync(cancellationToken);
        if (state is null
            || state.Status != CatalogueStateStatus.Succeeded
            || state.Summary is null)
        {
            await EnsureCatalogueAsync(state, cancellationToken);
            return;
        }

        var index = await searchIndexes.GetAsync(cancellationToken);
        if (index.Artifact is not null
            && string.Equals(
                index.Artifact.CatalogueRefreshId,
                state.RefreshId,
                StringComparison.Ordinal))
        {
            logger.LogInformation(
                "Startup readiness found catalogue refresh {RefreshId} and its production search index ready.",
                state.RefreshId);
            return;
        }

        var jobId = await searchIndexes.EnqueueForStartupAsync(
            state.RefreshId,
            cancellationToken);
        if (jobId is null)
        {
            logger.LogInformation(
                "Startup readiness found production search-index recovery already queued or running for catalogue refresh {RefreshId}.",
                state.RefreshId);
            return;
        }

        logger.LogInformation(
            "Startup readiness queued production search-index recovery job {JobId} for catalogue refresh {RefreshId}.",
            jobId,
            state.RefreshId);
    }

    private async Task EnsureCatalogueAsync(
        CatalogueState? state,
        CancellationToken cancellationToken)
    {
        if (!schedules.CatalogueRefresh.Enabled)
        {
            logger.LogInformation(
                "Startup readiness found no successful catalogue; automatic refresh is disabled.");
            return;
        }

        var outcome = await catalogueRefresh.RefreshOnStartupAsync(cancellationToken);
        switch (outcome)
        {
            case CatalogueRefreshStarted started:
                logger.LogInformation(
                    "Startup readiness queued catalogue refresh job {JobId} because catalogue state was {State}.",
                    started.Status.LatestRefresh?.Id,
                    state?.Status.ToString() ?? "missing");
                break;
            case CatalogueRefreshAlreadyRunning running:
                logger.LogInformation(
                    "Startup readiness found catalogue refresh job {JobId} already queued or running.",
                    running.Status.LatestRefresh?.Id);
                break;
            case CatalogueRefreshFailed:
                logger.LogWarning(
                    "Startup readiness could not queue a catalogue refresh for catalogue state {State}.",
                    state?.Status.ToString() ?? "missing");
                break;
            default:
                throw new InvalidOperationException("Unknown catalogue refresh outcome.");
        }
    }
}
