using LyrionVoiceMcp.Abstractions;

namespace LyrionVoiceMcp.Services;

internal sealed class NullSearchObservationStore : ISearchObservationStore
{
    public static NullSearchObservationStore Instance { get; } = new();

    public int RetentionDays => 0;

    public Task InitialiseAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task RecordAsync(SearchObservation observation, CancellationToken cancellationToken) =>
        Task.CompletedTask;

    public Task MarkSelectedAsync(
        IReadOnlyCollection<string> correlationIds,
        DateTimeOffset selectedAt,
        CancellationToken cancellationToken) => Task.CompletedTask;

    public Task<SearchObservationPage> BrowseAsync(
        SearchObservationQuery query,
        CancellationToken cancellationToken) =>
        Task.FromResult(new SearchObservationPage([], 0, query.Offset, query.Limit));

    public Task<SearchObservation?> GetAsync(string id, CancellationToken cancellationToken) =>
        Task.FromResult<SearchObservation?>(null);

    public Task<bool> SaveReviewAsync(
        string id,
        SearchObservationReview review,
        CancellationToken cancellationToken) => Task.FromResult(false);

    public Task<IReadOnlyList<SearchEvaluationCase>> ExportAsync(
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<SearchEvaluationCase>>([]);
}
