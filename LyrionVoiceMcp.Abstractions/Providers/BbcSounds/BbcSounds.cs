namespace LyrionVoiceMcp.Abstractions.Providers.BbcSounds;

public static class BbcSoundsProvider
{
    public const string Id = "bbc-sounds";
    public const string RefreshJob = "bbc-sounds.refresh";
    public const string RestoreJob = "bbc-sounds.restore-index";
    public const int MaximumShows = 10_000;
    public const int MaximumStations = 500;
    public const int StationSearchLimit = 5;
}

public sealed record BbcShow(string Id, string Title);
public sealed record BbcSubscriptions(bool Available, IReadOnlyList<BbcShow> Shows);
public sealed record BbcEpisode(string Id, string Title, string AudioUrl);
public sealed record BbcEpisodePage(IReadOnlyList<BbcEpisode> Episodes, int? NextOffset);
public sealed record BbcStation(string Id, string Name, string LiveAudioUrl);
public sealed record BbcStations(bool Available, IReadOnlyList<BbcStation> Stations);

public enum BbcStationMenuLevel
{
    Station,
    ScheduleDays,
    Programmes,
    PlayableActions,
    AudioItems
}

public enum BbcAudioKind
{
    Episode,
    Station,
    Rewind
}

public sealed record BbcBrowseTarget(string? ShowId = null, int Offset = 0)
    : ProviderBrowseTarget(BbcSoundsProvider.Id);
public sealed record BbcEpisodeTarget(string ShowId, string EpisodeId, string AudioUrl)
    : ProviderMediaTarget(BbcSoundsProvider.Id);
public sealed record BbcStationBrowseTarget(string StationId)
    : ProviderBrowseTarget(BbcSoundsProvider.Id);
public sealed record BbcStationRootBrowseTarget(int Offset = 0)
    : ProviderBrowseTarget(BbcSoundsProvider.Id);
public sealed record BbcStationMenuTarget(
    string StationId,
    string Path,
    BbcStationMenuLevel Level)
    : ProviderBrowseTarget(BbcSoundsProvider.Id);
public sealed record BbcAudioTarget(
    BbcAudioKind Kind,
    string Id,
    string AudioUrl,
    string? ValidationPath = null)
    : ProviderMediaTarget(BbcSoundsProvider.Id);

public sealed record BbcStationMenuItem(
    string Title,
    BrowseItemKind Kind,
    BbcStationMenuTarget? BrowseTarget = null,
    BbcAudioTarget? MediaTarget = null);

public interface IBbcSoundsClient
{
    Task<BbcSubscriptions> ReadSubscriptionsAsync(CancellationToken cancellationToken);
    Task<BbcStations> ReadStationsAsync(CancellationToken cancellationToken);
    Task<BbcEpisodePage> BrowseEpisodesAsync(string showId, int offset, CancellationToken cancellationToken);
    Task<IReadOnlyList<BbcStationMenuItem>> BrowseStationAsync(
        string stationId, CancellationToken cancellationToken);
    Task<IReadOnlyList<BbcStationMenuItem>> BrowseStationMenuAsync(
        BbcStationMenuTarget target, CancellationToken cancellationToken);
    Task<bool> IsAvailableAsync(BbcEpisodeTarget target, CancellationToken cancellationToken);
    Task<bool> IsAvailableAsync(BbcAudioTarget target, CancellationToken cancellationToken);
    Task SubmitAsync(string playerId, BbcEpisodeTarget target,
        ProviderPlaybackCommand command, CancellationToken cancellationToken);
    Task SubmitAsync(string playerId, BbcAudioTarget target,
        ProviderPlaybackCommand command, CancellationToken cancellationToken);
}

public sealed record BbcShowMatch(BbcShow Show, string Signal);
public sealed record BbcStationMatch(BbcStation Station, string Signal);

public interface IBbcSubscriptionIndex
{
    bool Available { get; }
    IReadOnlyList<BbcShow> Shows { get; }
    void Publish(BbcSubscriptions subscriptions);
    IReadOnlyList<BbcShowMatch> Search(string name, CancellationToken cancellationToken);
}

public interface IBbcStationIndex
{
    bool Available { get; }
    IReadOnlyList<BbcStation> Stations { get; }
    void Publish(BbcStations stations);
    IReadOnlyList<BbcStationMatch> Search(string name, CancellationToken cancellationToken);
}
