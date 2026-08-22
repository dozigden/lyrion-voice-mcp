using LyrionVoiceMcp.Abstractions;

namespace LyrionVoiceMcp.Search;

public interface ISearchResolver
{
    SearchResolverDescriptor Descriptor { get; }
    SearchResolverMetrics Metrics { get; }

    Task<SearchExecution> SearchAsync(
        string query,
        CancellationToken cancellationToken);
}

public sealed record SearchExecution(
    IReadOnlyList<SearchCandidate> Candidates,
    string? Error);

public sealed record SearchCandidate(
    MediaEntityKind Kind,
    string Title,
    string? Artist,
    string? Album,
    int? NativeRating = null);

public sealed record SearchResolverMetrics(
    int? IndexedCandidateCount,
    long PreparationDurationMilliseconds,
    long? IndexSizeBytes);
