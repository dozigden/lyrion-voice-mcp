namespace LyrionVoiceMcp.Abstractions;

public enum SearchObservationStatus
{
    Completed,
    Failed
}

public enum SearchReviewClassification
{
    Good,
    WrongOrder,
    NoMatch,
    Ambiguous,
    TranscriptionError,
    Other
}

public sealed record SearchObservationCandidate(
    int Position,
    string CorrelationId,
    MediaIdentity Identity,
    string Title,
    string? Artist,
    string? Album,
    DateTimeOffset? SelectedAt);

public sealed record SearchObservationReview(
    SearchReviewClassification Classification,
    string? ExpectedCorrelationId,
    MediaEntityKind? ExpectedKind,
    string? ExpectedTitle,
    string? ExpectedArtist,
    string? ExpectedAlbum,
    string? Notes,
    bool IncludeInEvaluation,
    DateTimeOffset ReviewedAt);

public sealed record SearchObservation(
    string Id,
    DateTimeOffset CreatedAt,
    string OriginalQuery,
    string NormalisedQuery,
    MediaEntityKind? RequestedKind,
    string Provider,
    string Collection,
    string Resolver,
    string ResolverVersion,
    SearchObservationStatus Status,
    string? FailureMessage,
    long TotalDurationMilliseconds,
    long RetrievalDurationMilliseconds,
    long ProcessingDurationMilliseconds,
    IReadOnlyList<LmsSearchRequestObservation> Requests,
    IReadOnlyList<SearchObservationCandidate> Candidates,
    SearchObservationReview? Review);

public enum SearchObservationReviewFilter
{
    All,
    Unreviewed,
    Reviewed
}

public enum SearchObservationResultFilter
{
    All,
    NoResults,
    Selected,
    Failed
}

public sealed record SearchObservationQuery(
    string? Text,
    SearchObservationReviewFilter Review,
    SearchObservationResultFilter Result,
    int Offset,
    int Limit);

public sealed record SearchObservationSummary(
    string Id,
    DateTimeOffset CreatedAt,
    string OriginalQuery,
    string Resolver,
    string ResolverVersion,
    SearchObservationStatus Status,
    int ResultCount,
    int? SelectedPosition,
    long TotalDurationMilliseconds,
    SearchReviewClassification? Classification,
    bool IncludeInEvaluation);

public sealed record SearchObservationPage(
    IReadOnlyList<SearchObservationSummary> Items,
    int Total,
    int Offset,
    int Limit);

public sealed record SearchObservationRetentionPolicy(int RetentionDays);

public sealed record LegacySearchObservationCursor(
    DateTimeOffset CreatedAt,
    string ObservationId);

public interface ILegacySearchObservationSource
{
    Task<IReadOnlyList<SearchObservation>> ReadBatchAsync(
        DateTimeOffset cutoff,
        LegacySearchObservationCursor? after,
        int limit,
        CancellationToken cancellationToken);
}

public sealed record EvaluationCandidate(
    int Position,
    MediaEntityKind Kind,
    string Title,
    string? Artist,
    string? Album,
    bool Selected,
    bool Expected);

public sealed record SearchEvaluationCase(
    string Query,
    SearchReviewClassification Classification,
    MediaEntityKind? ExpectedKind,
    string? ExpectedTitle,
    string? ExpectedArtist,
    string? ExpectedAlbum,
    IReadOnlyList<EvaluationCandidate> OriginalCandidates);

public interface ISearchObservationStore
{
    int RetentionDays { get; }

    Task InitialiseAsync(CancellationToken cancellationToken);

    Task RecordAsync(SearchObservation observation, CancellationToken cancellationToken);

    Task MarkSelectedAsync(
        IReadOnlyCollection<string> correlationIds,
        DateTimeOffset selectedAt,
        CancellationToken cancellationToken);

    Task<SearchObservationPage> BrowseAsync(
        SearchObservationQuery query,
        CancellationToken cancellationToken);

    Task<SearchObservation?> GetAsync(string id, CancellationToken cancellationToken);

    Task<bool> SaveReviewAsync(
        string id,
        SearchObservationReview review,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<SearchEvaluationCase>> ExportAsync(
        CancellationToken cancellationToken);
}

public enum SaveSearchReviewRejectionReason
{
    NotFound,
    InvalidReview
}

public abstract record SaveSearchReviewOutcome;

public sealed record SearchReviewSaved(SearchObservation Observation) : SaveSearchReviewOutcome;

public sealed record SaveSearchReviewRejected(
    SaveSearchReviewRejectionReason Reason,
    string Message) : SaveSearchReviewOutcome;

public interface ISearchObservationReviewService
{
    Task<SearchObservationPage> BrowseAsync(
        SearchObservationQuery query,
        CancellationToken cancellationToken);

    Task<SearchObservation?> GetAsync(string id, CancellationToken cancellationToken);

    Task<SaveSearchReviewOutcome> SaveReviewAsync(
        string id,
        SearchObservationReview review,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<SearchEvaluationCase>> ExportAsync(CancellationToken cancellationToken);
}
