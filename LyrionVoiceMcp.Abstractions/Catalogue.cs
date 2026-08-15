namespace LyrionVoiceMcp.Abstractions;

public sealed record CatalogueImportSource(
    string Id,
    string Provider,
    string? Version,
    string? Revision);

public sealed record CatalogueImportContributor(
    string SourceId,
    string Name,
    string? ExternalId);

public sealed record CatalogueImportAlbum(
    string SourceId,
    string Title,
    string? AlbumArtistSourceId,
    int? Year,
    int? DiscCount,
    bool? IsCompilation,
    string? ReleaseType,
    string? ArtworkTrackSourceId,
    string? ExternalId);

public sealed record CatalogueImportGenre(
    string SourceId,
    string Name);

public sealed record CatalogueImportTrackContributor(
    string ContributorSourceId,
    string Role);

public sealed record CatalogueImportTrackStatistics(
    string Source,
    int? Rating,
    int? PlayCount,
    DateTimeOffset? LastPlayedAt);

public sealed record CatalogueImportTrack(
    string SourceId,
    string Title,
    string? Subtitle,
    string Url,
    string? ContentType,
    bool IsRemote,
    string? ExternalId,
    string? AlbumSourceId,
    int? Year,
    int? DiscNumber,
    int? DiscCount,
    int? TrackNumber,
    double? DurationSeconds,
    long? FileSizeBytes,
    int? SampleRate,
    DateTimeOffset? AddedAt,
    DateTimeOffset? SourceModifiedAt,
    DateTimeOffset? SourceUpdatedAt,
    string? ReleaseType,
    bool? IsCompilation,
    string? ArtworkTrackSourceId,
    string? WorkSourceId,
    string? WorkTitle,
    string? Performance,
    string? Grouping,
    IReadOnlyList<CatalogueImportTrackContributor> Contributors,
    IReadOnlyList<string> GenreSourceIds,
    IReadOnlyList<CatalogueImportTrackStatistics> Statistics);

public sealed record CatalogueImportVirtualLibrary(
    string SourceId,
    string Name,
    IReadOnlyList<string> TrackSourceIds);

public sealed record CatalogueImportWarning(
    string Code,
    string Message,
    int Occurrences);

public sealed record CatalogueImportSnapshot(
    CatalogueImportSource Source,
    DateTimeOffset CapturedAt,
    DateTimeOffset? SourceLastScanAt,
    IReadOnlyList<CatalogueImportContributor> Contributors,
    IReadOnlyList<CatalogueImportAlbum> Albums,
    IReadOnlyList<CatalogueImportGenre> Genres,
    IReadOnlyList<CatalogueImportTrack> Tracks,
    IReadOnlyList<CatalogueImportVirtualLibrary> VirtualLibraries,
    IReadOnlyList<CatalogueImportWarning> Warnings);

public interface ICatalogueSourceReader
{
    Task<CatalogueImportSnapshot> ReadAsync(CancellationToken cancellationToken);
}

public sealed record PublishedCatalogueGeneration(
    string Id,
    string SourceId,
    string? SourceRevision,
    string? SourceVersion,
    DateTimeOffset CapturedAt,
    DateTimeOffset? SourceLastScanAt,
    DateTimeOffset PublishedAt,
    int ContributorCount,
    int AlbumCount,
    int GenreCount,
    int TrackCount,
    int VirtualLibraryCount,
    int WarningCount);

public enum CatalogueRefreshRunStatus
{
    Running,
    Succeeded,
    Failed,
    Cancelled,
    Interrupted
}

public sealed record CatalogueRefreshRun(
    string Id,
    CatalogueRefreshRunStatus Status,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt,
    long? DurationMilliseconds,
    string? PublishedGenerationId,
    string? FailureMessage);

public interface IMediaCatalogueStore
{
    Task InitialiseAsync(CancellationToken cancellationToken);

    Task<PublishedCatalogueGeneration?> GetPublishedGenerationAsync(
        CancellationToken cancellationToken);

    Task<CatalogueRefreshRun?> GetLatestRefreshRunAsync(
        CancellationToken cancellationToken);

    Task BeginRefreshAsync(
        string refreshId,
        DateTimeOffset startedAt,
        CancellationToken cancellationToken);

    Task<PublishedCatalogueGeneration> PublishAsync(
        CatalogueImportSnapshot snapshot,
        string refreshId,
        DateTimeOffset completedAt,
        long durationMilliseconds,
        CancellationToken cancellationToken);

    Task CompleteFailedRefreshAsync(
        string refreshId,
        CatalogueRefreshRunStatus status,
        DateTimeOffset completedAt,
        long durationMilliseconds,
        string failureMessage,
        CancellationToken cancellationToken);
}
