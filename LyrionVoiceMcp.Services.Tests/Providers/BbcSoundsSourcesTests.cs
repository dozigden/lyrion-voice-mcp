using LyrionVoiceMcp.Abstractions;
using LyrionVoiceMcp.Abstractions.Providers;
using LyrionVoiceMcp.Abstractions.Providers.BbcSounds;
using LyrionVoiceMcp.Services.Providers.BbcSounds;

namespace LyrionVoiceMcp.Services.Tests.Providers;

public sealed class BbcSoundsSourcesTests
{
    [Fact]
    public void MusicFiltersAndNameFreeSearchShouldNotSearchSubscriptions()
    {
        var index = new Index();
        var source = new BbcSoundsSearchSource(index, new StationIndex());
        foreach (var criteria in new[] { new SearchCriteria(null), new SearchCriteria("Mira", Genre: "Folk"),
            new SearchCriteria("Mira", FromYear: 2000, ToYear: 2020), new SearchCriteria("Mira", new RatingSearchConstraint(4, RatingMatchMode.Exact)) })
        {
            Assert.Empty(source.Search(criteria, TestContext.Current.CancellationToken).Candidates);
        }
        Assert.Equal(0, index.SearchCalls);
    }

    [Fact]
    public async Task EpisodeReferenceShouldRetainProgrammeSelectionCorrelationAndDisplayMetadata()
    {
        var references = new ReferenceCodecTestContext();
        var correlation = Guid.NewGuid().ToString("N");
        var source = new BbcSoundsBrowseSource(new Index(), new StationIndex(), new Client(), references.Browse);

        var page = Assert.IsType<BrowseSucceeded>(await source.BrowseAsync(new BbcBrowseTarget("showa"), correlation,
            TestContext.Current.CancellationToken));

        var episode = Assert.Single(page.Items);
        Assert.False(episode.HasBrowseReference);
        Assert.True(episode.HasPlayReference);
        var resolved = references.Resolver.Resolve(episode.Reference)!;
        Assert.Equal(correlation, resolved.SearchCorrelationId);
        Assert.IsType<BbcEpisodeTarget>(resolved.Media.ProviderTarget);
        Assert.Equal(ReferenceDisplayKind.Episode, references.DisplayMetadata.Resolve(episode.Reference)!.Kind);
        Assert.Equal("The Mira Vale Show", episode.Album);
    }

    [Fact]
    public void StationSearchShouldReturnAnIndependentBrowsableAndPlayableCandidate()
    {
        var stations = new StationIndex { SearchResult = true };
        var source = new BbcSoundsSearchSource(new Index(), stations);

        var candidate = Assert.Single(source.Search(
            new SearchCriteria("Fictional Radio"), TestContext.Current.CancellationToken).Candidates,
            item => item.Identity.Kind == MediaEntityKind.Station);

        Assert.IsType<BbcStationBrowseTarget>(candidate.BrowseTarget);
        Assert.IsType<BbcAudioTarget>(candidate.MediaTarget);
        Assert.Equal("stationa", candidate.Identity.Id);
    }

    [Fact]
    public async Task StationBrowseItemShouldCarryOneCorrelatedBrowseAndPlayReference()
    {
        var references = new ReferenceCodecTestContext();
        var source = new BbcSoundsBrowseSource(new Index(), new StationIndex(), new Client(), references.Browse);
        var correlation = Guid.NewGuid().ToString("N");

        var page = Assert.IsType<BrowseSucceeded>(await source.BrowseAsync(
            new BbcStationRootBrowseTarget(), correlation, TestContext.Current.CancellationToken));
        var station = Assert.Single(page.Items);
        var decoded = references.Browse.TryDecode(station.Reference)!;
        var playable = references.Resolver.Resolve(station.Reference)!;

        Assert.True(station.HasBrowseReference);
        Assert.True(station.HasPlayReference);
        Assert.Equal(correlation, decoded.SearchCorrelationId);
        Assert.IsType<BbcStationBrowseTarget>(decoded.ProviderTarget);
        Assert.IsType<BbcAudioTarget>(playable.Media.ProviderTarget);
        Assert.Equal(ReferenceDisplayKind.Station, references.DisplayMetadata.Resolve(station.Reference)!.Kind);
    }

    private sealed class Index : IBbcSubscriptionIndex
    {
        public int SearchCalls { get; private set; }
        public bool Available => true;
        public IReadOnlyList<BbcShow> Shows => [new("showa", "The Mira Vale Show")];
        public void Publish(BbcSubscriptions subscriptions) => throw new NotSupportedException();
        public IReadOnlyList<BbcShowMatch> Search(string name, CancellationToken token) { SearchCalls++; return []; }
    }
    private sealed class StationIndex : IBbcStationIndex
    {
        public bool SearchResult { get; init; }
        public bool Available => true;
        public IReadOnlyList<BbcStation> Stations => [new("stationa", "Fictional Radio", "sounds://_LIVE_stationa")];
        public void Publish(BbcStations stations) => throw new NotSupportedException();
        public IReadOnlyList<BbcStationMatch> Search(string name, CancellationToken token) => SearchResult
            ? [new(Stations[0], "exact_normalised")] : [];
    }
    private sealed class Client : IBbcSoundsClient
    {
        public Task<BbcSubscriptions> ReadSubscriptionsAsync(CancellationToken token) => throw new NotSupportedException();
        public Task<BbcStations> ReadStationsAsync(CancellationToken token) => throw new NotSupportedException();
        public Task<BbcEpisodePage> BrowseEpisodesAsync(string id, int offset, CancellationToken token) =>
            Task.FromResult(new BbcEpisodePage([new("episodea", "Fictional episode", "sounds://_versiona_episodea")], null));
        public Task<IReadOnlyList<BbcStationMenuItem>> BrowseStationAsync(string id, CancellationToken token) =>
            throw new NotSupportedException();
        public Task<IReadOnlyList<BbcStationMenuItem>> BrowseStationMenuAsync(BbcStationMenuTarget target, CancellationToken token) =>
            throw new NotSupportedException();
        public Task<bool> IsAvailableAsync(BbcEpisodeTarget target, CancellationToken token) => Task.FromResult(true);
        public Task<bool> IsAvailableAsync(BbcAudioTarget target, CancellationToken token) => Task.FromResult(true);
        public Task SubmitAsync(string player, BbcEpisodeTarget target, ProviderPlaybackCommand command, CancellationToken token) => throw new NotSupportedException();
        public Task SubmitAsync(string player, BbcAudioTarget target, ProviderPlaybackCommand command, CancellationToken token) => throw new NotSupportedException();
    }
}
