using System.Diagnostics;
using LyrionVoiceMcp.Abstractions;
using LyrionVoiceMcp.Abstractions.Providers;
using LyrionVoiceMcp.Abstractions.Providers.BbcSounds;

namespace LyrionVoiceMcp.Services.Providers.BbcSounds;

internal sealed class BbcSoundsSearchSource(IBbcSubscriptionIndex index) : IProviderSearchSource
{
    public string ProviderId => BbcSoundsProvider.Id;
    public ProviderSearchResult Search(SearchCriteria criteria, CancellationToken cancellationToken)
    {
        var watch = Stopwatch.StartNew();
        var applicable = !string.IsNullOrWhiteSpace(criteria.Query)
            && criteria.RatingConstraint is null && string.IsNullOrWhiteSpace(criteria.Genre)
            && criteria.FromYear is null && criteria.ToYear is null;
        var matches = applicable ? index.Search(criteria.Query!, cancellationToken) : [];
        var candidates = matches.Select(match => new ProviderSearchCandidate(
            new MediaIdentity(MediaEntityKind.Programme, match.Show.Id, BbcSoundsProvider.Id),
            match.Show.Title, match.Signal, new BbcBrowseTarget(match.Show.Id))).ToArray();
        return new ProviderSearchResult(candidates, new LmsSearchRequestObservation(
            BbcSoundsProvider.Id, applicable ? "subscription-index" : "not-applicable",
            LmsSearchRequestStatus.Completed, null, watch.ElapsedMilliseconds, candidates.Length));
    }
}

internal sealed class BbcSoundsBrowseSource(
    IBbcSubscriptionIndex index, IBbcSoundsClient client, IBrowseReferenceCodec references) : IProviderBrowseSource
{
    public string ProviderId => BbcSoundsProvider.Id;

    public IReadOnlyList<BrowseItemResult> GetRoots() => index.Available
        ? [Item("BBC Sounds Subscriptions", BrowseItemKind.Category, new BbcBrowseTarget(), null)] : [];

    public async Task<BrowseOutcome> BrowseAsync(
        ProviderBrowseTarget target, string? correlationId, CancellationToken cancellationToken)
    {
        if (target is not BbcBrowseTarget location || location.Offset < 0)
            return new BrowseRejected(BrowseRejectionReason.InvalidReference, "The programme browse reference is invalid.");
        if (!index.Available)
            return new BrowseRejected(BrowseRejectionReason.BrowseUnavailable, "BBC Sounds subscriptions are unavailable.");
        if (location.ShowId is null)
        {
            var shows = index.Shows;
            var page = shows.Skip(location.Offset).Take(50).ToArray();
            var next = location.Offset + page.Length;
            return new BrowseSucceeded(page.Select(show => Item(show.Title, BrowseItemKind.Programme,
                new BbcBrowseTarget(show.Id), correlationId)).ToArray(),
                next < shows.Count ? Reference(location with { Offset = next }, correlationId, "Subscribed shows", true) : null);
        }
        var show = index.Shows.SingleOrDefault(x => x.Id == location.ShowId);
        if (show is null)
            return new BrowseRejected(BrowseRejectionReason.BrowseUnavailable, "The show is no longer subscribed.");
        try
        {
            var page = await client.BrowseEpisodesAsync(show.Id, location.Offset, cancellationToken);
            return new BrowseSucceeded(page.Episodes.Select(episode =>
            {
                var metadata = new ReferenceDisplayMetadata(ReferenceDisplayKind.Episode, episode.Title, Album: show.Title);
                var media = new PlayableMedia(new MediaIdentity(MediaEntityKind.Episode, episode.Id, ProviderId),
                    new BbcEpisodeTarget(show.Id, episode.Id, episode.AudioUrl));
                return new BrowseItemResult(references.Encode(new BrowseReferenceValue(
                    null, media, correlationId, metadata)), BrowseItemKind.Episode, episode.Title,
                    null, show.Title, false, true);
            }).ToArray(), page.NextOffset is { } next
                ? Reference(location with { Offset = next }, correlationId, show.Title, true) : null);
        }
        catch (LmsRequestException exception)
        {
            return new BrowseRejected(BrowseRejectionReason.BrowseUnavailable, exception.Message);
        }
    }

    private BrowseItemResult Item(string title, BrowseItemKind kind, BbcBrowseTarget target, string? correlationId) =>
        new(Reference(target, correlationId, title, false), kind, title, null, null, true, false);

    private string Reference(BbcBrowseTarget target, string? correlationId, string title, bool continuation) =>
        references.Encode(new BrowseReferenceValue(null, null, correlationId,
            new ReferenceDisplayMetadata(target.ShowId is null ? ReferenceDisplayKind.Category : ReferenceDisplayKind.Programme,
                title, IsContinuation: continuation), target));
}

internal sealed class BbcSoundsPlaybackSource(IBbcSoundsClient client) : IProviderPlaybackSource
{
    public string ProviderId => BbcSoundsProvider.Id;
    public async Task<int> GetPlayableItemCountAsync(ProviderMediaTarget target, CancellationToken cancellationToken) =>
        target is BbcEpisodeTarget episode && await client.IsAvailableAsync(episode, cancellationToken) ? 1 : 0;
    public Task SubmitAsync(string playerId, ProviderMediaTarget target,
        ProviderPlaybackCommand command, CancellationToken cancellationToken) =>
        target is BbcEpisodeTarget episode
            ? client.SubmitAsync(playerId, episode, command, cancellationToken)
            : throw new LmsRequestException("The BBC Sounds episode reference is invalid.");
}
