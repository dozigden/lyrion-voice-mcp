namespace LyrionVoiceMcp.Contracts;

public sealed record CatalogueStatusResponse(
    CatalogueSummaryResponse? Summary,
    CatalogueRefreshRunResponse? LatestRefresh);

public sealed record CatalogueSummaryResponse(
    string SourceId,
    string Provider,
    string? SourceRevision,
    string? SourceVersion,
    DateTimeOffset CapturedAt,
    DateTimeOffset? SourceLastScanAt,
    DateTimeOffset RefreshedAt,
    int ArtistCount,
    int AlbumCount,
    int GenreCount,
    int TrackCount,
    int VirtualLibraryCount,
    int WarningCount);

public sealed record CatalogueRefreshLogResponse(
    long Id,
    DateTimeOffset OccurredAt,
    string Level,
    string Message,
    int? ProcessedCount,
    int? TotalCount);

public sealed record CatalogueRefreshRunResponse(
    string Id,
    string Status,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt,
    long? DurationMilliseconds,
    string? FailureMessage,
    IReadOnlyList<CatalogueRefreshLogResponse> Logs);
