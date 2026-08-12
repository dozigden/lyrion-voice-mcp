using LyrionVoiceMcp.Abstractions;

namespace LyrionVoiceMcp.Services;

public sealed class SearchObservationReviewService(
    ISearchObservationStore store) : ISearchObservationReviewService
{
    public Task<SearchObservationPage> BrowseAsync(
        SearchObservationQuery query,
        CancellationToken cancellationToken) =>
        store.BrowseAsync(query, cancellationToken);

    public Task<SearchObservation?> GetAsync(
        string id,
        CancellationToken cancellationToken) =>
        store.GetAsync(id, cancellationToken);

    public async Task<SaveSearchReviewOutcome> SaveReviewAsync(
        string id,
        SearchObservationReview review,
        CancellationToken cancellationToken)
    {
        var observation = await store.GetAsync(id, cancellationToken);
        if (observation is null)
        {
            return new SaveSearchReviewRejected(
                SaveSearchReviewRejectionReason.NotFound,
                "The search observation was not found.");
        }

        if (review.ExpectedCorrelationId is not null
            && observation.Candidates.All(candidate =>
                !string.Equals(
                    candidate.CorrelationId,
                    review.ExpectedCorrelationId,
                    StringComparison.Ordinal)))
        {
            return new SaveSearchReviewRejected(
                SaveSearchReviewRejectionReason.InvalidReview,
                "The expected result is not one of this search's candidates.");
        }

        if (review.IncludeInEvaluation
            && observation.Status != SearchObservationStatus.Completed)
        {
            return new SaveSearchReviewRejected(
                SaveSearchReviewRejectionReason.InvalidReview,
                "Failed searches cannot be included in the evaluation corpus.");
        }

        if (!await store.SaveReviewAsync(id, review, cancellationToken)
            || await store.GetAsync(id, cancellationToken) is not { } saved)
        {
            return new SaveSearchReviewRejected(
                SaveSearchReviewRejectionReason.NotFound,
                "The search observation was not found.");
        }

        return new SearchReviewSaved(saved);
    }

    public Task<IReadOnlyList<SearchEvaluationCase>> ExportAsync(
        CancellationToken cancellationToken) =>
        store.ExportAsync(cancellationToken);
}
