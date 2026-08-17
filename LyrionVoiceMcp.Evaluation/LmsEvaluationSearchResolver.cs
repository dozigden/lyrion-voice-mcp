using LyrionVoiceMcp.Abstractions;

namespace LyrionVoiceMcp.Evaluation;

public sealed class LmsEvaluationSearchResolver(
    ILmsSearchClient searchClient) : ISearchResolver
{
    public string Name => "lms-pass-through";
    public string Version => "1";
    public SearchResolverMetrics Metrics { get; } = new(null, 0, null);

    public async Task<SearchExecution> SearchAsync(
        string query,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await searchClient.SearchAsync(query, cancellationToken);
            return new SearchExecution(Map(response.Candidates), null);
        }
        catch (LmsSearchFailedException exception)
        {
            return new SearchExecution(
                Map(exception.Response.Candidates),
                exception.Message);
        }
    }

    private static IReadOnlyList<SearchCandidate> Map(
        IReadOnlyList<LmsSearchCandidate> candidates) =>
        candidates.Select(candidate => new SearchCandidate(
            candidate.Identity.Kind,
            candidate.Title,
            candidate.Artist,
            candidate.Album)).ToArray();
}
