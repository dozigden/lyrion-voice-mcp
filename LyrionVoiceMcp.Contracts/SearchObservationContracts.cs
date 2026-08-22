namespace LyrionVoiceMcp.Contracts;

public sealed record SearchObservationPageResponse(
    IReadOnlyList<SearchObservationSummaryResponse> Items,
    int Total,
    int Offset,
    int Limit,
    int RetentionDays);

public sealed record SearchObservationSummaryResponse(
    string Id,
    DateTimeOffset CreatedAt,
    string OriginalQuery,
    string Resolver,
    string ResolverVersion,
    string Status,
    int ResultCount,
    int? SelectedPosition,
    long TotalDurationMilliseconds,
    string? Classification,
    bool IncludeInEvaluation);

public sealed record SearchObservationDetailResponse(
    string Id,
    DateTimeOffset CreatedAt,
    string OriginalQuery,
    string NormalisedQuery,
    decimal? Rating,
    string? RatingMatch,
    string? RequestedKind,
    string Provider,
    string Collection,
    string Resolver,
    string ResolverVersion,
    string Status,
    string? FailureMessage,
    long TotalDurationMilliseconds,
    long RetrievalDurationMilliseconds,
    long ProcessingDurationMilliseconds,
    IReadOnlyList<SearchRequestObservationResponse> Requests,
    IReadOnlyList<SearchCandidateObservationResponse> Candidates,
    SearchObservationReviewResponse? Review,
    int RetentionDays);

public sealed record SearchRequestObservationResponse(
    string Source,
    string Command,
    string Status,
    string? FailureMessage,
    long DurationMilliseconds,
    int ResultCount);

public sealed record SearchCandidateObservationResponse(
    int Position,
    string CorrelationId,
    string Kind,
    string Title,
    string? Artist,
    string? Album,
    decimal? Rating,
    DateTimeOffset? SelectedAt);

public sealed record SearchObservationReviewResponse(
    string Classification,
    string? ExpectedCorrelationId,
    string? ExpectedKind,
    string? ExpectedTitle,
    string? ExpectedArtist,
    string? ExpectedAlbum,
    string? Notes,
    bool IncludeInEvaluation,
    DateTimeOffset ReviewedAt);

public sealed record SaveSearchObservationReviewRequest(
    string Classification,
    string? ExpectedCorrelationId,
    string? ExpectedKind,
    string? ExpectedTitle,
    string? ExpectedArtist,
    string? ExpectedAlbum,
    string? Notes,
    bool IncludeInEvaluation);

public sealed record SearchEvaluationExportResponse(
    int SchemaVersion,
    DateTimeOffset ExportedAt,
    IReadOnlyList<SearchEvaluationCaseResponse> Cases);

public sealed record SearchEvaluationCaseResponse(
    string Query,
    decimal? Rating,
    string? RatingMatch,
    string Classification,
    string? ExpectedKind,
    string? ExpectedTitle,
    string? ExpectedArtist,
    string? ExpectedAlbum,
    IReadOnlyList<SearchEvaluationCandidateResponse> OriginalCandidates);

public sealed record SearchEvaluationCandidateResponse(
    int Position,
    string Kind,
    string Title,
    string? Artist,
    string? Album,
    decimal? Rating,
    bool Selected,
    bool Expected);
