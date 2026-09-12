namespace LyrionVoiceMcp.Abstractions.Providers.BbcSounds;

public static class BbcSoundsProvider
{
    public const string Id = "bbc-sounds";
    public const string RefreshJob = "bbc-sounds.refresh";
    public const string RestoreJob = "bbc-sounds.restore-index";
    public const int MaximumShows = 10_000;
}

public sealed record BbcShow(string Id, string Title);
public sealed record BbcSubscriptions(bool Available, IReadOnlyList<BbcShow> Shows);
public sealed record BbcEpisode(string Id, string Title, string AudioUrl);
public sealed record BbcEpisodePage(IReadOnlyList<BbcEpisode> Episodes, int? NextOffset);

public sealed record BbcBrowseTarget(string? ShowId = null, int Offset = 0)
    : ProviderBrowseTarget(BbcSoundsProvider.Id);
public sealed record BbcEpisodeTarget(string ShowId, string EpisodeId, string AudioUrl)
    : ProviderMediaTarget(BbcSoundsProvider.Id);

public interface IBbcSoundsClient
{
    Task<BbcSubscriptions> ReadSubscriptionsAsync(CancellationToken cancellationToken);
    Task<BbcEpisodePage> BrowseEpisodesAsync(string showId, int offset, CancellationToken cancellationToken);
    Task<bool> IsAvailableAsync(BbcEpisodeTarget target, CancellationToken cancellationToken);
    Task SubmitAsync(string playerId, BbcEpisodeTarget target,
        ProviderPlaybackCommand command, CancellationToken cancellationToken);
}

public sealed record BbcShowMatch(BbcShow Show, string Signal);

public interface IBbcSubscriptionIndex
{
    bool Available { get; }
    IReadOnlyList<BbcShow> Shows { get; }
    void Publish(BbcSubscriptions subscriptions);
    IReadOnlyList<BbcShowMatch> Search(string name, CancellationToken cancellationToken);
}
