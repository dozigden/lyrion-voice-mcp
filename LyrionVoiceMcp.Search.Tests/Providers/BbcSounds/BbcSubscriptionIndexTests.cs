using LyrionVoiceMcp.Abstractions.Providers.BbcSounds;
using LyrionVoiceMcp.Search.Providers.BbcSounds;

namespace LyrionVoiceMcp.Search.Tests.Providers.BbcSounds;

public sealed class BbcSubscriptionIndexTests
{
    [Fact]
    public void DistinctiveNameShouldMatchInsideProgrammeTitleAndPreserveAmbiguity()
    {
        var index = new BbcSubscriptionIndex();
        index.Publish(new BbcSubscriptions(true, [new("a", "The Mira Vale Show"),
            new("b", "Mira Vale"), new("c", "Music with Mira Vale"), new("d", "The Orchard Hour")]));

        var results = index.Search("Mira Vale", TestContext.Current.CancellationToken);

        Assert.Equal("b", results[0].Show.Id);
        Assert.Equal(new[] { "b", "c", "a" }, results.Select(x => x.Show.Id));
        Assert.Equal("exact_normalised", results[0].Signal);
        Assert.Equal("complete_title_span", results[1].Signal);
    }

    [Theory]
    [InlineData("mira vale", true)]
    [InlineData("MÍRA VALE", true)]
    [InlineData("vale mira", true)]
    [InlineData("mira vael", true)]
    [InlineData("mira vale orchard", false)]
    [InlineData("orchard", false)]
    public void MatchingShouldCoverEveryQueryWord(string query, bool expected)
    {
        var index = new BbcSubscriptionIndex();
        index.Publish(new BbcSubscriptions(true, [new("a", "The Mira Vale Show")]));
        Assert.Equal(expected, index.Search(query, TestContext.Current.CancellationToken).Count > 0);
    }

    [Fact]
    public void PublicationShouldReplaceTheSnapshotAndBoundSearchResults()
    {
        var index = new BbcSubscriptionIndex();
        index.Publish(new BbcSubscriptions(true, Enumerable.Range(1, 8)
            .Select(i => new BbcShow(i.ToString(), $"Mira Vale Programme {i}")).ToArray()));
        var oldShows = index.Shows;
        Assert.Equal(5, index.Search("Mira Vale", TestContext.Current.CancellationToken).Count);

        index.Publish(new BbcSubscriptions(false, []));

        Assert.False(index.Available);
        Assert.Empty(index.Search("Mira Vale", TestContext.Current.CancellationToken));
        Assert.Equal(8, oldShows.Count);
    }
}
