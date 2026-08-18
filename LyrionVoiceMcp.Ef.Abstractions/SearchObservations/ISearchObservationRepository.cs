using LyrionVoiceMcp.Ef.Abstractions.Entities;

namespace LyrionVoiceMcp.Ef.Abstractions.SearchObservations;

public interface ISearchObservationRepository
{
    void Add(EntitySearchObservation observation);

    Task<IReadOnlySet<string>> ListExistingObservationIdsAsync(
        IReadOnlyCollection<string> observationIds,
        CancellationToken cancellationToken);

    Task<EntitySearchObservationPage> BrowseAsync(
        EntitySearchObservationQuery query,
        CancellationToken cancellationToken);

    Task<EntitySearchObservation?> GetAsync(
        string observationId,
        CancellationToken cancellationToken);

    Task<EntitySearchObservation?> GetForReviewAsync(
        string observationId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<EntitySearchObservationCandidate>> ListCandidatesForSelectionAsync(
        IReadOnlyCollection<string> correlationIds,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<EntitySearchObservation>> ListForExportAsync(
        CancellationToken cancellationToken);

    Task<int> DeleteOlderThanAsync(
        DateTime cutoffUtc,
        CancellationToken cancellationToken);
}

public sealed record EntitySearchObservationQuery(
    string? Text,
    bool? Reviewed,
    EntitySearchObservationResultFilter Result,
    int Offset,
    int Limit);

public enum EntitySearchObservationResultFilter
{
    All,
    NoResults,
    Selected,
    Failed
}

public sealed record EntitySearchObservationSummary(
    string ObservationId,
    DateTime CreatedAtUtc,
    string OriginalQuery,
    string Resolver,
    string ResolverVersion,
    EntitySearchObservationStatus Status,
    int ResultCount,
    int? SelectedPosition,
    long TotalDurationMilliseconds,
    EntitySearchReviewClassification? Classification,
    bool IncludeInEvaluation);

public sealed record EntitySearchObservationPage(
    IReadOnlyList<EntitySearchObservationSummary> Items,
    int Total,
    int Offset,
    int Limit);
