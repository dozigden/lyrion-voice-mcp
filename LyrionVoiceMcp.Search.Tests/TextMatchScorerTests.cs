using LyrionVoiceMcp.Abstractions;

namespace LyrionVoiceMcp.Search.Tests;

public sealed class TextMatchScorerTests
{
    [Theory]
    [InlineData("Copper Lines", "Copper Lines", "exact_normalised", 1300, 0)]
    [InlineData("Lines Copper", "Copper Lines", "same_tokens", 1260, 0)]
    [InlineData("CopperLines", "Copper Lines", "exact_compact", 1230, 0)]
    [InlineData("Copper", "Copper Lines", "prefix", 1140, 0)]
    [InlineData("Kopper Lines", "Copper Lines", "consonant_skeleton", 1080, 0)]
    [InlineData("Nite Signal", "Night Signal", "consonant_skeleton", 1080, 0)]
    [InlineData("Wright Signal", "Right Signal", "double_metaphone", 1040, 0)]
    [InlineData("Copper Xines", "Copper Lines", "bounded_edit", 960, 0)]
    [InlineData("Volume six", "Volume VI", "roman_cardinal_equivalent", 1200, 0)]
    [InlineData("Copper Lines extra", "Copper Lines", "exact_normalised", 980, 320)]
    public void MusicRankingShouldPreserveItsSignalScoresAndCoverageEvidence(
        string query, string title, string signal, int score, int coveragePenalty)
    {
        var candidate = CatalogueSearchRanker.CreateCandidate(CatalogueIndexCandidate.FromDocument(
            new CatalogueSearchDocument(new MediaIdentity(MediaEntityKind.Track, "fictional"), title, null, null)));

        var result = Assert.Single(CatalogueSearchRanker.RankCandidates(query, [candidate], false, true,
            TestContext.Current.CancellationToken));

        Assert.Equal(score, result.Score);
        Assert.Equal(signal, result.MatchSignal);
        Assert.Equal(coveragePenalty, result.Evidence!.CoveragePenalty);
        Assert.Equal("title", result.Evidence.Field);
        Assert.Equal(score, result.Evidence.FinalScore);
    }

    [Theory]
    [InlineData("Copper Lines", null, "artist", 1120, 180)]
    [InlineData(null, "Copper Lines", "album", 1060, 240)]
    public void MusicRankingShouldKeepItsFieldPenalties(string? artist, string? album, string field, int score, int penalty)
    {
        var candidate = CatalogueSearchRanker.CreateCandidate(CatalogueIndexCandidate.FromDocument(
            new CatalogueSearchDocument(new MediaIdentity(MediaEntityKind.Track, "fictional"), "Orchard", artist, album)));

        var result = Assert.Single(CatalogueSearchRanker.RankCandidates("Copper Lines", [candidate], false, true,
            TestContext.Current.CancellationToken));

        Assert.Equal(score, result.Score);
        Assert.Equal(field, result.Evidence!.Field);
        Assert.Equal(penalty, result.Evidence.FieldPenalty);
    }
}
