using LyrionVoiceMcp.Abstractions.Providers;
using Microsoft.Extensions.Logging;

namespace LyrionVoiceMcp.Services.Providers;

internal static class ProviderCatalogueWork
{
    public static async Task EnqueueAsync(IEnumerable<IProviderCatalogueContributor> contributors,
        string? refreshId, ILogger logger, CancellationToken cancellationToken)
    {
        foreach (var contributor in contributors)
        {
            try
            {
                if (refreshId is null) await contributor.EnqueueRestoreAsync(cancellationToken);
                else await contributor.EnqueueRefreshAsync(refreshId, cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogWarning(exception, "Could not enqueue optional provider work.");
            }
        }
    }
}
