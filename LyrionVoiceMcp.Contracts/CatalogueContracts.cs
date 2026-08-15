namespace LyrionVoiceMcp.Contracts;

public sealed record CatalogueStatusResponse(
    PublishedCatalogueGenerationResponse? PublishedGeneration,
    CatalogueRefreshRunResponse? LatestRefresh);

public sealed record PublishedCatalogueGenerationResponse(
    string Id,
    string SourceId,
    string? SourceRevision,
    string? SourceVersion,
    DateTimeOffset CapturedAt,
    DateTimeOffset? SourceLastScanAt,
    DateTimeOffset PublishedAt,
    int ArtistCount,
    int AlbumCount,
    int GenreCount,
    int TrackCount,
    int VirtualLibraryCount,
    int WarningCount);

public sealed record CatalogueRefreshRunResponse(
    string Id,
    string Status,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt,
    long? DurationMilliseconds,
    string? PublishedGenerationId,
    string? FailureMessage);
