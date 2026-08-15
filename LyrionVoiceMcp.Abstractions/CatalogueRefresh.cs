namespace LyrionVoiceMcp.Abstractions;

public sealed record CatalogueStatus(
    CatalogueSummary? Summary,
    CatalogueRefreshRun? LatestRefresh);

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
}
