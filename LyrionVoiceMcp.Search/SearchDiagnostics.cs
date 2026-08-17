using LyrionVoiceMcp.Abstractions;

namespace LyrionVoiceMcp.Search;

public interface IEvaluationDiagnosticSearchResolver : IEvaluationSearchResolver
{
    Task<EvaluationDiagnosticSearchResponse> SearchDetailedAsync(
        string query,
        CancellationToken cancellationToken);
}

public sealed record EvaluationDiagnosticSearchResponse(
    string Resolver,
    string ResolverVersion,
    EvaluationResolverMetrics ResolverMetrics,
    double RetrievalDurationMilliseconds,
    double RerankDurationMilliseconds,
    double TotalDurationMilliseconds,
    int RetrievedCandidateCount,
    IReadOnlyList<EvaluationLaneMeasurement> Lanes,
    IReadOnlyList<EvaluationDiagnosticCandidate> Results);

public sealed record EvaluationLaneMeasurement(
    string Name,
    double DurationMilliseconds,
    int MatchedCandidateCount,
    int RetrievedCandidateCount,
    int NewCandidateCount);

public sealed record EvaluationDiagnosticCandidate(
    int Position,
    MediaEntityKind Kind,
    string Title,
    string? Artist,
    string? Album,
    int Score,
    IReadOnlyList<string> RetrievalLanes,
    EvaluationScoreEvidence? ScoreEvidence);

public sealed record EvaluationScoreEvidence(
    string Field,
    string Signal,
    string QuerySpan,
    int MatchedTokenCount,
    int IgnoredTokenCount,
    int SignalScore,
    int FieldPenalty,
    int CoveragePenalty,
    int FinalScore);

internal sealed record RankedPhuzzyCandidate(
    PhuzzyCandidate Candidate,
    int Score,
    EvaluationScoreEvidence? Evidence);

internal sealed record ResolverSearchExecution(
    double RetrievalDurationMilliseconds,
    double RerankDurationMilliseconds,
    double TotalDurationMilliseconds,
    IReadOnlyList<EvaluationLaneMeasurement> Lanes,
    IReadOnlyList<RankedPhuzzyCandidate> Ranked,
    IReadOnlyDictionary<string, IReadOnlyList<string>> RetrievalLanes);

internal readonly record struct LaneRetrieval(
    int MatchedCandidateCount,
    int RetrievedCandidateCount);

internal sealed class CandidateCollector<TKey>(bool captureEvidence)
    where TKey : notnull
{
    private readonly HashSet<TKey> candidateIds = [];
    private readonly Dictionary<TKey, HashSet<string>>? evidence = captureEvidence
        ? new Dictionary<TKey, HashSet<string>>()
        : null;

    public int Count => candidateIds.Count;
    public IReadOnlyCollection<TKey> CandidateIds => candidateIds;

    public void Add(TKey candidateId, string lane)
    {
        candidateIds.Add(candidateId);
        if (evidence is null)
        {
            return;
        }

        if (!evidence.TryGetValue(candidateId, out var lanes))
        {
            lanes = new HashSet<string>(StringComparer.Ordinal);
            evidence.Add(candidateId, lanes);
        }

        lanes.Add(lane);
    }

    public IReadOnlyList<string> GetEvidence(TKey candidateId) =>
        evidence is not null && evidence.TryGetValue(candidateId, out var lanes)
            ? lanes.Order(StringComparer.Ordinal).ToArray()
            : [];
}

internal static class EvaluationDiagnosticResults
{
    public static EvaluationDiagnosticSearchResponse Create(
        IEvaluationSearchResolver resolver,
        double retrievalDurationMilliseconds,
        double rerankDurationMilliseconds,
        double totalDurationMilliseconds,
        IReadOnlyList<EvaluationLaneMeasurement> lanes,
        IReadOnlyList<RankedPhuzzyCandidate> ranked,
        IReadOnlyDictionary<string, IReadOnlyList<string>> retrievalLanes)
    {
        var results = ranked.Select((item, index) =>
        {
            var value = item.Candidate.Source.Value;
            retrievalLanes.TryGetValue(item.Candidate.Source.StableKey, out var candidateLanes);
            return new EvaluationDiagnosticCandidate(
                index + 1,
                value.Kind,
                value.Title,
                value.Artist,
                value.Album,
                item.Score,
                candidateLanes ?? [],
                item.Evidence);
        }).ToArray();
        return new EvaluationDiagnosticSearchResponse(
            resolver.Name,
            resolver.Version,
            resolver.Metrics,
            retrievalDurationMilliseconds,
            rerankDurationMilliseconds,
            totalDurationMilliseconds,
            results.Length,
            lanes,
            results);
    }
}
