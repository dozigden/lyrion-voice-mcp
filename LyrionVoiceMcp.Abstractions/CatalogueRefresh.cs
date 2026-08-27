namespace LyrionVoiceMcp.Abstractions;

public sealed record CatalogueInitialisationPolicy(bool SourceConfigured);

public sealed record CatalogueStatus(
    CatalogueSummary? Summary,
    Job? LatestRefresh);

public abstract record CatalogueRefreshOutcome;

public sealed record CatalogueRefreshStarted(
    CatalogueStatus Status) : CatalogueRefreshOutcome;

public sealed record CatalogueRefreshAlreadyRunning(
    CatalogueStatus Status) : CatalogueRefreshOutcome;

public sealed record CatalogueRefreshFailed(
    CatalogueStatus Status) : CatalogueRefreshOutcome;

public interface ICatalogueRefreshService
{
    Task<CatalogueStatus> GetStatusAsync(CancellationToken cancellationToken);

    Task<CatalogueRefreshOutcome> RefreshAsync(CancellationToken cancellationToken);

    Task<CatalogueRefreshOutcome> RefreshOnStartupAsync(
        CancellationToken cancellationToken);
}
