using LyrionVoiceMcp.Abstractions;
using LyrionVoiceMcp.Services;

namespace LyrionVoiceMcp.Services.Tests;

public sealed class SearchCandidateSelectorTests
{
    [Fact]
    public void RotationShouldPreserveScoreBandsBeforeVaryingEqualCandidates()
    {
        var candidates = new[]
        {
            Candidate("high-a", "Album A", 1_200),
            Candidate("low-a", "Album D", 1_100),
            Candidate("high-b", "Album B", 1_200),
            Candidate("low-b", "Album E", 1_100),
            Candidate("high-c", "Album C", 1_200)
        };
        var selector = new SearchCandidateSelector(new Random(23));

        var selected = selector.Rotate(candidates, 3);

        Assert.All(selected, candidate => Assert.Equal(1_200, candidate.Score));
        Assert.Equal(3, selected.Select(candidate => candidate.Album).Distinct().Count());
    }

    [Fact]
    public void RotationShouldSpreadAlbumsWithinAnEqualScoreBand()
    {
        var candidates = new[]
        {
            Candidate("a-1", "Album A", 1_120),
            Candidate("a-2", "Album A", 1_120),
            Candidate("a-3", "Album A", 1_120),
            Candidate("b-1", "Album B", 1_120),
            Candidate("c-1", "Album C", 1_120)
        };
        var selector = new SearchCandidateSelector(new Random(29));

        var selected = selector.Rotate(candidates, 3);

        Assert.Equal(3, selected.Select(candidate => candidate.Album).Distinct().Count());
    }

    [Fact]
    public void ReservoirShouldRemainBoundedAndAdmitLaterCandidates()
    {
        var selector = new SearchCandidateSelector(new Random(31));
        var reservoir = selector.CreateReservoir(10);

        foreach (var index in Enumerable.Range(1, 100))
        {
            reservoir.Consider(Candidate($"track-{index}", "Album", 1_120));
        }

        Assert.Equal(10, reservoir.Candidates.Count);
        Assert.Contains(
            reservoir.Candidates,
            candidate => int.Parse(candidate.Identity.Id["track-".Length..]) > 10);
    }

    [Fact]
    public void WeightedRotationShouldPreferFiveStarsWithoutExcludingFourStars()
    {
        var candidates = new[]
        {
            Candidate("four-star", "Album Four", 1_120, 80),
            Candidate("five-star", "Album Five", 1_120, 100)
        };
        var selector = new SearchCandidateSelector(new Random(37));
        var selections = Enumerable.Range(1, 2_000)
            .Select(_ => selector.Rotate(
                candidates,
                1,
                candidate => 1 + Math.Max(0, candidate.NativeRating - 80) / 10d)[0])
            .ToArray();

        var fourStarCount = selections.Count(candidate => candidate.NativeRating == 80);
        var fiveStarCount = selections.Count(candidate => candidate.NativeRating == 100);
        Assert.True(fiveStarCount > fourStarCount);
        Assert.True(fourStarCount > 0);
    }

    private static CatalogueSearchCandidate Candidate(
        string id,
        string album,
        int score,
        int nativeRating = 0) =>
        new(
            new MediaIdentity(MediaEntityKind.Track, id),
            id,
            "The Imaginaries",
            album,
            score,
            nativeRating);
}
