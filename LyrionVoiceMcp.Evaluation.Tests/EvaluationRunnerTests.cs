using System.Text.Json;
using LyrionVoiceMcp.Abstractions;

namespace LyrionVoiceMcp.Evaluation.Tests;

public sealed class EvaluationRunnerTests
{
    [Fact]
    public async Task RunAsync_scores_ranked_matches_and_no_match_cases()
    {
        var client = new StubSearchClient(query => query == "moon light owls"
            ? [
                Candidate(MediaEntityKind.Artist, "The Paper Comets"),
                Candidate(MediaEntityKind.Artist, "The Moonlit Owls")
            ]
            : []);
        var corpus = new EvaluationCorpus(1, [
            new EvaluationCase(
                "moonlit-owls",
                "moon light owls",
                [new ExpectedEntity(MediaEntityKind.Artist, "The Moonlit Owls", null, null)],
                "fictional-transcription",
                null),
            new EvaluationCase(
                "unheard-orchestra",
                "unheard orchestra",
                [],
                "no-match",
                null)
        ]);
        var runner = new EvaluationRunner(
            client,
            new FixedTimeProvider(new DateTimeOffset(2026, 8, 13, 9, 0, 0, TimeSpan.Zero)));

        var report = await runner.RunAsync(corpus, "corpus-hash", TestContext.Current.CancellationToken);

        Assert.Equal(2, report.Summary.PassedCases);
        Assert.Equal(0, report.Summary.Top1Matches);
        Assert.Equal(1, report.Summary.Top5Matches);
        Assert.Equal(1, report.Summary.CorrectNoMatches);
        Assert.Equal(0.5, report.Summary.MeanReciprocalRank);
        Assert.Equal(2, report.Cases[0].FirstMatchPosition);
        Assert.True(report.Cases[0].Results[1].Expected);
        Assert.Empty(report.Cases[1].Results);
    }

    [Fact]
    public async Task RunAsync_records_request_errors_without_treating_them_as_no_match()
    {
        var client = new StubSearchClient(_ => throw new LmsRequestException("LMS unavailable."));
        var corpus = new EvaluationCorpus(1, [
            new EvaluationCase("no-result", "nothing", [], "no-match", null)
        ]);
        var runner = new EvaluationRunner(client, TimeProvider.System);

        var report = await runner.RunAsync(corpus, "corpus-hash", TestContext.Current.CancellationToken);

        Assert.Equal(1, report.Summary.ErrorCases);
        Assert.Equal(0, report.Summary.PassedCases);
        Assert.False(report.Cases[0].Passed);
        Assert.Equal("LMS unavailable.", report.Cases[0].Error);
    }

    [Fact]
    public async Task RunAsync_report_omits_lms_ids_and_private_corpus_notes()
    {
        var client = new StubSearchClient(_ => [
            new LmsSearchCandidate(
                new MediaIdentity(MediaEntityKind.Track, "private-lms-id"),
                "Lantern Signals",
                "The Paper Comets",
                "Night Routes")
        ]);
        var corpus = new EvaluationCorpus(1, [
            new EvaluationCase(
                "lantern-signals",
                "lantern signals",
                [new ExpectedEntity(
                    MediaEntityKind.Track,
                    "Lantern Signals",
                    "The Paper Comets",
                    "Night Routes")],
                "fictional-exact",
                "private corpus note")
        ]);
        var runner = new EvaluationRunner(client, TimeProvider.System);

        var report = await runner.RunAsync(corpus, "corpus-hash", TestContext.Current.CancellationToken);
        var json = JsonSerializer.Serialize(report);

        Assert.DoesNotContain("private-lms-id", json, StringComparison.Ordinal);
        Assert.DoesNotContain("private corpus note", json, StringComparison.Ordinal);
        Assert.Contains("Lantern Signals", json, StringComparison.Ordinal);
    }

    private static LmsSearchCandidate Candidate(MediaEntityKind kind, string title) =>
        new(new MediaIdentity(kind, Guid.NewGuid().ToString("N")), title, null, null);

    private sealed class StubSearchClient(
        Func<string, IReadOnlyList<LmsSearchCandidate>> search) : ILmsSearchClient
    {
        public Task<LmsSearchResponse> SearchAsync(
            string query,
            CancellationToken cancellationToken)
        {
            var candidates = search(query);
            return Task.FromResult(new LmsSearchResponse(candidates, [], 1));
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset value) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => value;
    }
}
