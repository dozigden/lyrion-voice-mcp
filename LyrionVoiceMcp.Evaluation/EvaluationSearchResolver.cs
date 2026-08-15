using LyrionVoiceMcp.Abstractions;

namespace LyrionVoiceMcp.Evaluation;

public interface IEvaluationSearchResolver
{
    string Name { get; }
    string Version { get; }
    EvaluationResolverMetrics Metrics { get; }

    Task<EvaluationSearchResponse> SearchAsync(
        string query,
        CancellationToken cancellationToken);
}

public sealed record EvaluationSearchResponse(
    IReadOnlyList<EvaluationSearchCandidate> Candidates,
    string? Error);

public sealed record EvaluationSearchCandidate(
    MediaEntityKind Kind,
    string Title,
    string? Artist,
    string? Album);

public sealed record EvaluationResolverMetrics(
    int? IndexedCandidateCount,
    long PreparationDurationMilliseconds,
    long? IndexSizeBytes);
