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
    [InlineData("mira vale orchard", true)]
    [InlineData("orchard", false)]
    public void MatchingShouldTolerateNameErrorsAndExtraWords(string query, bool expected)
    {
        var index = new BbcSubscriptionIndex();
        index.Publish(new BbcSubscriptions(true, [new("a", "The Mira Vale Show")]));
        Assert.Equal(expected, index.Search(query, TestContext.Current.CancellationToken).Count > 0);
    }

    [Theory]
    [InlineData("The Mira Vale Show: The Latest Episode")]
    [InlineData("The Mira Vale Show BBC Sounds")]
    [InlineData("On BBC Sounds, the latest episode of The Mira Vale Show")]
    [InlineData("The newest episode of The Mira Vale Show from BBC Sounds")]
    [InlineData("BBC SOUNDS: The Mira Vale Show — newest episode")]
    [InlineData("The Mira Vale Show, latest episode, on BBC Sounds")]
    public void BoundaryContextShouldMatchTheProgrammeAndRecordItsInterpretation(string query)
    {
        var index = CreateIndex(new("mira", "The Mira Vale Show"), new("orchard", "The Orchard Hour"));

        var match = index.Search(query, TestContext.Current.CancellationToken)[0];

        Assert.Equal("mira", match.Show.Id);
        Assert.Equal("programme_context_exact_normalised", match.Signal);
        Assert.Equal("The Mira Vale Show", match.Show.Title);
    }

    [Fact]
    public void PossessivesShouldSurviveRemovingEpisodeContext()
    {
        var index = CreateIndex(new BbcShow("mira", "Mira Vale's Evening Hour"));

        var match = Assert.Single(index.Search("Mira Vale’s Evening Hour: The Latest Episode", TestContext.Current.CancellationToken));

        Assert.Equal("programme_context_exact_normalised", match.Signal);
    }

    [Theory]
    [InlineData("The Reef Sallow Show BBC Sounds")]
    [InlineData("The Brich Sallow Show: the latest episode")]
    public void WholePhraseSimilarityShouldRecoverMistranscribedPresenterNames(string query)
    {
        var index = CreateIndex(new("birch", "The Birch Sallow Show"), new("orchard", "The Orchard Hour"));

        var match = index.Search(query, TestContext.Current.CancellationToken)[0];

        Assert.Equal("birch", match.Show.Id);
        Assert.NotEqual("exact_normalised", match.Signal);
        Assert.NotEqual("programme_context_exact_normalised", match.Signal);
    }

    [Fact]
    public void OriginalExactTitleShouldBeatItsContextInterpretation()
    {
        var index = CreateIndex(new("literal", "The Mira Vale Show BBC Sounds"), new("name", "The Mira Vale Show"));

        var matches = index.Search("The Mira Vale Show BBC Sounds", TestContext.Current.CancellationToken);

        Assert.Equal(new[] { "literal", "name" }, matches.Select(match => match.Show.Id));
        Assert.Equal("exact_normalised", matches[0].Signal);
        Assert.Equal("programme_context_exact_normalised", matches[1].Signal);
    }

    [Theory]
    [InlineData("BBC Sounds")]
    [InlineData("the latest episode")]
    [InlineData("the newest episode of")]
    [InlineData("on BBC Sounds: the latest episode")]
    public void ContextAloneShouldOnlyReturnAnOriginalExactTitle(string query)
    {
        var index = CreateIndex(new("literal", query), new("mira", "The Mira Vale Show"), new("episode", "Episode Stories"));

        var match = Assert.Single(index.Search(query, TestContext.Current.CancellationToken));

        Assert.Equal("literal", match.Show.Id);
        Assert.Equal("exact_normalised", match.Signal);
    }

    [Fact]
    public void UnknownWordsShouldKeepTheirCoveragePenaltyAndCompetingTitles()
    {
        var index = CreateIndex(new("complete", "Mira Vale Orchard"), new("partial", "The Mira Vale Show"));

        var matches = index.Search("Mira Vale Orchard", TestContext.Current.CancellationToken);

        Assert.Equal("complete", matches[0].Show.Id);
        Assert.Contains(matches, match => match.Show.Id == "partial");
        Assert.DoesNotContain(matches, match => match.Signal.StartsWith("programme_context_", StringComparison.Ordinal));
    }

    [Fact]
    public void ContextInterpretationsShouldNotDuplicateProgrammeResults()
    {
        var index = CreateIndex(new("mira", "The Mira Vale Show"), new("music", "Music with Mira Vale"));

        var matches = index.Search("Mira Vale BBC Sounds", TestContext.Current.CancellationToken);

        Assert.Equal(2, matches.Count);
        Assert.Equal(2, matches.Select(match => match.Show.Id).Distinct().Count());
    }

    [Fact]
    public void CancelledSearchShouldNotStartScoring()
    {
        var index = CreateIndex(new BbcShow("mira", "The Mira Vale Show"));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.Throws<OperationCanceledException>(() => index.Search("Mira Vale", cancellation.Token));
    }

    private static BbcSubscriptionIndex CreateIndex(params BbcShow[] shows)
    {
        var index = new BbcSubscriptionIndex();
        index.Publish(new BbcSubscriptions(true, shows));
        return index;
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
