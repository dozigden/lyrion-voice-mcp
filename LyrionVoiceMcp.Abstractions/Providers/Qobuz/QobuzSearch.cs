namespace LyrionVoiceMcp.Abstractions.Providers.Qobuz;

public sealed record QobuzSearchLocation(string Query, MediaEntityKind Kind, string Label, string? Artwork,
    QobuzSearchLocation? Parent = null) : ProviderBrowseTarget("qobuz");

public sealed record QobuzCollectionTarget(QobuzSearchLocation Location) : ProviderMediaTarget("qobuz");
public sealed record QobuzAudioTarget(string Url) : ProviderMediaTarget("qobuz");

public interface IQobuzSearchClient : IExplicitSearchSource
{
    Task<IReadOnlyList<ProviderSearchCandidate>> BrowseAsync(QobuzSearchLocation location, CancellationToken cancellationToken);
    Task<int> GetPlayableCountAsync(ProviderMediaTarget target, CancellationToken cancellationToken);
    Task SubmitAsync(string player, ProviderMediaTarget target, ProviderPlaybackCommand command, CancellationToken cancellationToken);
}
