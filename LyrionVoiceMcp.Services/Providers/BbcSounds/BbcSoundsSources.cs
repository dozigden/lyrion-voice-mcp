using System.Diagnostics;
using LyrionVoiceMcp.Abstractions;
using LyrionVoiceMcp.Abstractions.Providers;
using LyrionVoiceMcp.Abstractions.Providers.BbcSounds;

namespace LyrionVoiceMcp.Services.Providers.BbcSounds;

internal sealed class BbcSoundsSearchSource(
    IBbcSubscriptionIndex subscriptionIndex,
    IBbcStationIndex stationIndex) : IProviderSearchSource
{
    public string ProviderId => BbcSoundsProvider.Id;
    public ProviderSearchResult Search(SearchCriteria criteria, CancellationToken cancellationToken)
    {
        var watch = Stopwatch.StartNew();
        var applicable = !string.IsNullOrWhiteSpace(criteria.Query)
            && criteria.RatingConstraint is null && string.IsNullOrWhiteSpace(criteria.Genre)
            && criteria.FromYear is null && criteria.ToYear is null;
        var showMatches = applicable ? subscriptionIndex.Search(criteria.Query!, cancellationToken) : [];
        var stationMatches = applicable ? stationIndex.Search(criteria.Query!, cancellationToken) : [];
        var candidates = showMatches.Select(match => new ProviderSearchCandidate(
            new MediaIdentity(MediaEntityKind.Programme, match.Show.Id, BbcSoundsProvider.Id),
            match.Show.Title, match.Signal, new BbcBrowseTarget(match.Show.Id)))
            .Concat(stationMatches.Select(match => new ProviderSearchCandidate(
                new MediaIdentity(MediaEntityKind.Station, match.Station.Id, BbcSoundsProvider.Id),
                match.Station.Name,
                match.Signal,
                new BbcStationBrowseTarget(match.Station.Id),
                new BbcAudioTarget(BbcAudioKind.Station, match.Station.Id, match.Station.LiveAudioUrl))))
            .ToArray();
        return new ProviderSearchResult(candidates, new LmsSearchRequestObservation(
            BbcSoundsProvider.Id, applicable ? "provider-indexes" : "not-applicable",
            LmsSearchRequestStatus.Completed, null, watch.ElapsedMilliseconds, candidates.Length));
    }
}

internal sealed class BbcSoundsBrowseSource(
    IBbcSubscriptionIndex subscriptionIndex,
    IBbcStationIndex stationIndex,
    IBbcSoundsClient client,
    IBrowseReferenceCodec references) : IProviderBrowseSource
{
    public string ProviderId => BbcSoundsProvider.Id;

    public IReadOnlyList<BrowseItemResult> GetRoots()
    {
        var roots = new List<BrowseItemResult>();
        if (subscriptionIndex.Available)
            roots.Add(Item("BBC Sounds Subscriptions", BrowseItemKind.Category, new BbcBrowseTarget(), null));
        if (stationIndex.Available)
            roots.Add(Item("BBC Sounds Stations", BrowseItemKind.Category, new BbcStationRootBrowseTarget(), null));
        return roots;
    }

    public async Task<BrowseOutcome> BrowseAsync(
        ProviderBrowseTarget target, string? correlationId, CancellationToken cancellationToken)
    {
        try
        {
            return target switch
            {
                BbcBrowseTarget location => await BrowseSubscriptionsAsync(location, correlationId, cancellationToken),
                BbcStationRootBrowseTarget location => BrowseStations(location, correlationId),
                BbcStationBrowseTarget location => await BrowseStationAsync(location, correlationId, cancellationToken),
                BbcStationMenuTarget location => await BrowseStationMenuAsync(location, correlationId, cancellationToken),
                _ => new BrowseRejected(BrowseRejectionReason.InvalidReference, "The BBC Sounds browse reference is invalid.")
            };
        }
        catch (LmsRequestException exception)
        {
            return new BrowseRejected(BrowseRejectionReason.BrowseUnavailable, exception.Message);
        }
    }

    private async Task<BrowseOutcome> BrowseSubscriptionsAsync(
        BbcBrowseTarget location, string? correlationId, CancellationToken cancellationToken)
    {
        if (location.Offset < 0)
            return new BrowseRejected(BrowseRejectionReason.InvalidReference, "The programme browse reference is invalid.");
        if (!subscriptionIndex.Available)
            return new BrowseRejected(BrowseRejectionReason.BrowseUnavailable, "BBC Sounds subscriptions are unavailable.");
        if (location.ShowId is null)
        {
            var shows = subscriptionIndex.Shows;
            var showPage = shows.Skip(location.Offset).Take(50).ToArray();
            var showNext = location.Offset + showPage.Length;
            return new BrowseSucceeded(showPage.Select(show => Item(show.Title, BrowseItemKind.Programme,
                new BbcBrowseTarget(show.Id), correlationId)).ToArray(),
                showNext < shows.Count ? Reference(location with { Offset = showNext }, correlationId, "Subscribed shows", true) : null);
        }
        var show = subscriptionIndex.Shows.SingleOrDefault(x => x.Id == location.ShowId);
        if (show is null)
            return new BrowseRejected(BrowseRejectionReason.BrowseUnavailable, "The show is no longer subscribed.");
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

    private BrowseOutcome BrowseStations(BbcStationRootBrowseTarget location, string? correlationId)
    {
        if (location.Offset < 0)
            return new BrowseRejected(BrowseRejectionReason.InvalidReference, "The station browse reference is invalid.");
        if (!stationIndex.Available)
            return new BrowseRejected(BrowseRejectionReason.BrowseUnavailable, "BBC Sounds stations are unavailable.");
        var stations = stationIndex.Stations;
        var page = stations.Skip(location.Offset).Take(50).ToArray();
        var next = location.Offset + page.Length;
        return new BrowseSucceeded(page.Select(station => StationItem(station, correlationId)).ToArray(),
            next < stations.Count
                ? Reference(location with { Offset = next }, correlationId, "BBC Sounds Stations", true)
                : null);
    }

    private async Task<BrowseOutcome> BrowseStationAsync(
        BbcStationBrowseTarget location, string? correlationId, CancellationToken cancellationToken)
    {
        if (!stationIndex.Available)
            return new BrowseRejected(BrowseRejectionReason.BrowseUnavailable, "BBC Sounds stations are unavailable.");
        var station = stationIndex.Stations.SingleOrDefault(x => x.Id == location.StationId);
        if (station is null)
            return new BrowseRejected(BrowseRejectionReason.BrowseUnavailable, "The BBC Sounds station is no longer available.");
        return Menu(await client.BrowseStationAsync(station.Id, cancellationToken), correlationId);
    }

    private async Task<BrowseOutcome> BrowseStationMenuAsync(
        BbcStationMenuTarget location, string? correlationId, CancellationToken cancellationToken)
    {
        if (!stationIndex.Available || stationIndex.Stations.All(x => x.Id != location.StationId))
            return new BrowseRejected(BrowseRejectionReason.BrowseUnavailable, "The BBC Sounds station is no longer available.");
        return Menu(await client.BrowseStationMenuAsync(location, cancellationToken), correlationId);
    }

    private BrowseSucceeded Menu(IReadOnlyList<BbcStationMenuItem> items, string? correlationId) =>
        new(items.Select(item =>
        {
            var metadata = new ReferenceDisplayMetadata(DisplayKind(item.Kind), item.Title);
            PlayableMedia? media = item.MediaTarget is null ? null : new PlayableMedia(
                new MediaIdentity(item.Kind == BrowseItemKind.Station ? MediaEntityKind.Station : MediaEntityKind.Episode,
                    item.MediaTarget.Id, ProviderId), item.MediaTarget);
            return new BrowseItemResult(references.Encode(new BrowseReferenceValue(
                    null, media, correlationId, metadata, item.BrowseTarget)),
                item.Kind, item.Title, null, null, item.BrowseTarget is not null, media is not null);
        }).ToArray(), null);

    private BrowseItemResult StationItem(BbcStation station, string? correlationId)
    {
        var metadata = new ReferenceDisplayMetadata(ReferenceDisplayKind.Station, station.Name);
        var media = new PlayableMedia(new MediaIdentity(MediaEntityKind.Station, station.Id, ProviderId),
            new BbcAudioTarget(BbcAudioKind.Station, station.Id, station.LiveAudioUrl));
        return new BrowseItemResult(references.Encode(new BrowseReferenceValue(
                null, media, correlationId, metadata, new BbcStationBrowseTarget(station.Id))),
            BrowseItemKind.Station, station.Name, null, null, true, true);
    }

    private BrowseItemResult Item(string title, BrowseItemKind kind, ProviderBrowseTarget target, string? correlationId) =>
        new(Reference(target, correlationId, title, false), kind, title, null, null, true, false);

    private string Reference(ProviderBrowseTarget target, string? correlationId, string title, bool continuation) =>
        references.Encode(new BrowseReferenceValue(null, null, correlationId,
            new ReferenceDisplayMetadata(target is BbcBrowseTarget { ShowId: not null }
                    ? ReferenceDisplayKind.Programme : ReferenceDisplayKind.Category,
                title, IsContinuation: continuation), target));

    private static ReferenceDisplayKind DisplayKind(BrowseItemKind kind) => kind switch
    {
        BrowseItemKind.Category => ReferenceDisplayKind.Category,
        BrowseItemKind.Programme => ReferenceDisplayKind.Programme,
        BrowseItemKind.Episode => ReferenceDisplayKind.Episode,
        BrowseItemKind.Station => ReferenceDisplayKind.Station,
        _ => throw new InvalidOperationException($"Unsupported BBC Sounds browse item kind {kind}.")
    };
}

internal sealed class BbcSoundsPlaybackSource(IBbcSoundsClient client) : IProviderPlaybackSource
{
    public string ProviderId => BbcSoundsProvider.Id;
    public async Task<int> GetPlayableItemCountAsync(ProviderMediaTarget target, CancellationToken cancellationToken) =>
        target switch
        {
            BbcEpisodeTarget episode when await client.IsAvailableAsync(episode, cancellationToken) => 1,
            BbcAudioTarget audio when await client.IsAvailableAsync(audio, cancellationToken) => 1,
            _ => 0
        };
    public Task SubmitAsync(string playerId, ProviderMediaTarget target,
        ProviderPlaybackCommand command, CancellationToken cancellationToken) =>
        target is BbcEpisodeTarget episode
            ? client.SubmitAsync(playerId, episode, command, cancellationToken)
            : target is BbcAudioTarget audio
                ? client.SubmitAsync(playerId, audio, command, cancellationToken)
                : throw new LmsRequestException("The BBC Sounds audio reference is invalid.");
}
