namespace LyrionVoiceMcp.Abstractions;

public sealed record CatalogueImportSource(
    string Id,
    string Provider,
    string? Version,
    string? Revision);

public sealed record CatalogueImportArtist(
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
    IReadOnlyList<string> ArtistSourceIds,
    IReadOnlyList<string> GenreSourceIds,
    IReadOnlyList<CatalogueImportTrackStatistics> Statistics);

public sealed record CatalogueImportVirtualLibrary(
    string SourceId,
    string Name);

public sealed record CatalogueImportVirtualLibraryMembership(
    string LibrarySourceId,
    int TrackCount);

public sealed record CatalogueSourceReadResult(
    CatalogueImportSource Source,
    DateTimeOffset CapturedAt,
    DateTimeOffset? SourceLastScanAt,
    int ArtistLookupCount,
    int AlbumCount,
    int GenreCount,
    int TrackCount,
    int VirtualLibraryCount,
    IReadOnlyList<CatalogueImportVirtualLibraryMembership> VirtualLibraryMemberships);

public interface ICatalogueImportWriter
{
    Task WriteAlbumsAsync(
        string refreshId,
        IReadOnlyList<CatalogueImportAlbum> albums,
        CancellationToken cancellationToken);

    Task WriteGenresAsync(
        string refreshId,
        IReadOnlyList<CatalogueImportGenre> genres,
        CancellationToken cancellationToken);

    Task WriteTracksAsync(
        string refreshId,
        IReadOnlyList<CatalogueImportTrack> tracks,
        CancellationToken cancellationToken);

    Task WriteArtistsAsync(
        string refreshId,
        IReadOnlyList<CatalogueImportArtist> artists,
        CancellationToken cancellationToken);

    Task WriteVirtualLibrariesAsync(
        string refreshId,
        IReadOnlyList<CatalogueImportVirtualLibrary> libraries,
        CancellationToken cancellationToken);

    Task WriteVirtualLibraryTracksAsync(
        string refreshId,
        string librarySourceId,
        IReadOnlyList<string> trackSourceIds,
        CancellationToken cancellationToken);

}

public interface ICatalogueRefreshLogSink
{
    Task WriteAsync(
        CatalogueRefreshLogLevel level,
        string message,
        int? processedCount,
        int? totalCount,
        CancellationToken cancellationToken);
}

public interface ICatalogueSourceReader
{
    Task<CatalogueSourceReadResult> ReadAsync(
        string refreshId,
        ICatalogueImportWriter writer,
        ICatalogueRefreshLogSink log,
        CancellationToken cancellationToken);
}

public sealed record CatalogueSummary(
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

public enum CatalogueStateStatus
{
    Running,
    Succeeded,
    Failed,
    Cancelled,
    Interrupted
}

public sealed record CatalogueState(
    string RefreshId,
    CatalogueStateStatus Status,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt,
    CatalogueSummary? Summary);

public enum CatalogueRefreshLogLevel
{
    Information,
    Warning,
    Error
}

public sealed record CatalogueRefreshWarning(
    CatalogueRefreshLogLevel Level,
    string Message,
    int? ProcessedCount,
    int? TotalCount);

public sealed record CatalogueRefreshCompletion(
    CatalogueSummary Summary,
    IReadOnlyList<CatalogueRefreshWarning> Warnings);

public interface IMediaCatalogueStore : ICatalogueImportWriter
{
    Task InitialiseAsync(CancellationToken cancellationToken);

    Task<CatalogueState?> GetStateAsync(CancellationToken cancellationToken);

    Task<CatalogueSummary?> GetSummaryAsync(CancellationToken cancellationToken);

    Task BeginRefreshAsync(
        string refreshId,
        DateTimeOffset startedAt,
        CancellationToken cancellationToken);

    Task<CatalogueRefreshCompletion> CompleteRefreshAsync(
        string refreshId,
        CatalogueSourceReadResult source,
        DateTimeOffset completedAt,
        int existingWarningCount,
        CancellationToken cancellationToken);

    Task FinishRefreshAsync(
        string refreshId,
        CatalogueStateStatus status,
        DateTimeOffset completedAt,
        CancellationToken cancellationToken);
}
