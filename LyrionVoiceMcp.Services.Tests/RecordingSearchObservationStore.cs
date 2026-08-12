using LyrionVoiceMcp.Abstractions;

namespace LyrionVoiceMcp.Services.Tests;

internal sealed class RecordingSearchObservationStore : ISearchObservationStore
{
    public SearchObservation? Recorded { get; private set; }
    public IReadOnlyCollection<string>? SelectedCorrelationIds { get; private set; }
    public int RetentionDays => 90;
    public Task InitialiseAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task RecordAsync(SearchObservation observation, CancellationToken cancellationToken)
    {
        Recorded = observation;
        return Task.CompletedTask;
    }
    public Task MarkSelectedAsync(IReadOnlyCollection<string> correlationIds, DateTimeOffset selectedAt, CancellationToken cancellationToken)
    {
        SelectedCorrelationIds = correlationIds;
        return Task.CompletedTask;
    }
    public Task<SearchObservationPage> BrowseAsync(SearchObservationQuery query, CancellationToken cancellationToken) =>
        Task.FromResult(new SearchObservationPage([], 0, query.Offset, query.Limit));
    public Task<SearchObservation?> GetAsync(string id, CancellationToken cancellationToken) => Task.FromResult(Recorded);
    public Task<bool> SaveReviewAsync(string id, SearchObservationReview review, CancellationToken cancellationToken) => Task.FromResult(true);
    public Task<IReadOnlyList<SearchEvaluationCase>> ExportAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<SearchEvaluationCase>>([]);
}
