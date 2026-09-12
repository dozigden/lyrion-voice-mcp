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
        var source = new BbcSoundsSearchSource(index);
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
        var source = new BbcSoundsBrowseSource(new Index(), new Client(), references.Browse);

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

    private sealed class Index : IBbcSubscriptionIndex
    {
        public int SearchCalls { get; private set; }
        public bool Available => true;
        public IReadOnlyList<BbcShow> Shows => [new("showa", "The Mira Vale Show")];
        public void Publish(BbcSubscriptions subscriptions) => throw new NotSupportedException();
        public IReadOnlyList<BbcShowMatch> Search(string name, CancellationToken token) { SearchCalls++; return []; }
    }
    private sealed class Client : IBbcSoundsClient
    {
        public Task<BbcSubscriptions> ReadSubscriptionsAsync(CancellationToken token) => throw new NotSupportedException();
        public Task<BbcEpisodePage> BrowseEpisodesAsync(string id, int offset, CancellationToken token) =>
            Task.FromResult(new BbcEpisodePage([new("episodea", "Fictional episode", "sounds://_versiona_episodea")], null));
        public Task<bool> IsAvailableAsync(BbcEpisodeTarget target, CancellationToken token) => Task.FromResult(true);
        public Task SubmitAsync(string player, BbcEpisodeTarget target, ProviderPlaybackCommand command, CancellationToken token) => throw new NotSupportedException();
    }
}
