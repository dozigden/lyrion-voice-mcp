using System.Text.Json;
using LyrionVoiceMcp.Abstractions;

namespace LyrionVoiceMcp.Evaluation.Tests;

public sealed class LmsEvaluationSearchResolverTests
{
    [Fact]
    public async Task SearchAsync_removes_lms_identity_from_evaluation_candidates()
    {
        var client = new StubSearchClient(new LmsSearchResponse(
            [
                new LmsSearchCandidate(
                    new MediaIdentity(MediaEntityKind.Track, "private-lms-id"),
                    "Lantern Signals",
                    "The Paper Comets",
                    "Night Routes")
            ],
            [],
            1));
        var resolver = new LmsEvaluationSearchResolver(client);

        var response = await resolver.SearchAsync(
            "lantern signals",
            TestContext.Current.CancellationToken);
        var json = JsonSerializer.Serialize(response);

        Assert.DoesNotContain("private-lms-id", json, StringComparison.Ordinal);
        Assert.Contains("Lantern Signals", json, StringComparison.Ordinal);
    }

    private sealed class StubSearchClient(
        LmsSearchResponse response) : ILmsSearchClient
    {
        public Task<LmsSearchResponse> SearchAsync(
            string query,
            CancellationToken cancellationToken) => Task.FromResult(response);
    }
}
