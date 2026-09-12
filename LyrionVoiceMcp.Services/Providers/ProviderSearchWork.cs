using System.Diagnostics;
using LyrionVoiceMcp.Abstractions;
using LyrionVoiceMcp.Abstractions.Providers;
using Microsoft.Extensions.Logging;

namespace LyrionVoiceMcp.Services.Providers;

internal static class ProviderSearchWork
{
    public static ProviderSearchResult Read(IProviderSearchSource source, SearchCriteria criteria,
        ILogger logger, CancellationToken cancellationToken)
    {
        var watch = Stopwatch.StartNew();
        try
        {
            return source.Search(criteria, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "Optional search source {Provider} failed.", source.ProviderId);
            return new ProviderSearchResult([], new LmsSearchRequestObservation(source.ProviderId,
                "provider-search", LmsSearchRequestStatus.Failed, "The optional search source is unavailable.",
                watch.ElapsedMilliseconds, 0));
        }
    }
}
