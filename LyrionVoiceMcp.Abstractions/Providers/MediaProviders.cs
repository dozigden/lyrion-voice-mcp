namespace LyrionVoiceMcp.Abstractions.Providers;

public abstract record ProviderBrowseTarget(string ProviderId);

public abstract record ProviderMediaTarget(string ProviderId);

public sealed record ProviderSearchCandidate(
    MediaIdentity Identity,
    string Title,
    string MatchSignal,
    ProviderBrowseTarget? BrowseTarget,
    ProviderMediaTarget? MediaTarget = null,
    string? Artist = null,
    string? Album = null);

public sealed record ProviderSearchResult(
    IReadOnlyList<ProviderSearchCandidate> Candidates,
    LmsSearchRequestObservation Observation);

public interface IExplicitSearchSource
{
    string ProviderId { get; }
    Task<IReadOnlyList<ProviderSearchCandidate>> SearchAsync(string name, CancellationToken cancellationToken);
}

public interface IProviderSearchSource
{
    string ProviderId { get; }
    ProviderSearchResult Search(SearchCriteria criteria, CancellationToken cancellationToken);
}

public interface IProviderBrowseSource
{
    string ProviderId { get; }
    Task<IReadOnlyList<BrowseItemResult>> GetRootsAsync(CancellationToken cancellationToken);
    Task<BrowseOutcome> BrowseAsync(
        ProviderBrowseTarget target, string? correlationId, CancellationToken cancellationToken);
}

public enum ProviderPlaybackCommand { Load, Add, Insert }

public interface IProviderPlaybackSource
{
    string ProviderId { get; }
    Task<int> GetPlayableItemCountAsync(ProviderMediaTarget target, CancellationToken cancellationToken);
    Task SubmitAsync(string playerId, ProviderMediaTarget target,
        ProviderPlaybackCommand command, CancellationToken cancellationToken);
}

public interface IProviderCatalogueContributor
{
    Task EnqueueRefreshAsync(string correlationId, CancellationToken cancellationToken);
    Task EnqueueRestoreAsync(CancellationToken cancellationToken);
}
