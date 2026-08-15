using LyrionVoiceMcp.Abstractions;

namespace LyrionVoiceMcp.Evaluation;

public sealed class LmsEvaluationSearchResolver(
    ILmsSearchClient searchClient) : IEvaluationSearchResolver
{
    public string Name => "lms-pass-through";
    public string Version => "1";
    public EvaluationResolverMetrics Metrics { get; } = new(null, 0, null);

    public async Task<EvaluationSearchResponse> SearchAsync(
        string query,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await searchClient.SearchAsync(query, cancellationToken);
            return new EvaluationSearchResponse(Map(response.Candidates), null);
        }
        catch (LmsSearchFailedException exception)
        {
            return new EvaluationSearchResponse(
                Map(exception.Response.Candidates),
                exception.Message);
        }
    }

    private static IReadOnlyList<EvaluationSearchCandidate> Map(
        IReadOnlyList<LmsSearchCandidate> candidates) =>
        candidates.Select(candidate => new EvaluationSearchCandidate(
            candidate.Identity.Kind,
            candidate.Title,
            candidate.Artist,
            candidate.Album)).ToArray();
}
