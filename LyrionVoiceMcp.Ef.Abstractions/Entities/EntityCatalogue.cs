namespace LyrionVoiceMcp.Ef.Abstractions.Entities;

public enum EntityCatalogueStateStatus
{
    Running,
    Succeeded,
    Failed,
    Cancelled,
    Interrupted
}

public sealed class EntityCatalogueState
{
    public int Id { get; set; }
    public string RefreshId { get; set; } = string.Empty;
    public EntityCatalogueStateStatus Status { get; set; }
    public DateTime StartedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public string? SourceId { get; set; }
    public string? SourceProvider { get; set; }
    public string? SourceRevision { get; set; }
    public string? SourceVersion { get; set; }
    public DateTime? CapturedAtUtc { get; set; }
    public DateTime? SourceLastScanAtUtc { get; set; }
    public DateTime? RefreshedAtUtc { get; set; }
    public int? ArtistCount { get; set; }
    public int? AlbumCount { get; set; }
    public int? GenreCount { get; set; }
    public int? TrackCount { get; set; }
    public int? VirtualLibraryCount { get; set; }
    public int? WarningCount { get; set; }
}

public sealed class EntityCatalogueArtist
{
    public int Id { get; set; }
    public string SourceId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? ExternalId { get; set; }
    public string SeenRefreshId { get; set; } = string.Empty;
}

public sealed class EntityCatalogueArtistLookup
{
    public int Id { get; set; }
    public string SourceId { get; set; } = string.Empty;
    public string SeenRefreshId { get; set; } = string.Empty;
}

public sealed class EntityCatalogueAlbum
{
    public int Id { get; set; }
    public string SourceId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? AlbumArtistSourceId { get; set; }
    public int? Year { get; set; }
    public int? DiscCount { get; set; }
    public bool? IsCompilation { get; set; }
    public string? ReleaseType { get; set; }
    public string? ArtworkTrackSourceId { get; set; }
    public string? ExternalId { get; set; }
    public string SeenRefreshId { get; set; } = string.Empty;
}

public sealed class EntityCatalogueGenre
{
    public int Id { get; set; }
    public string SourceId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string SeenRefreshId { get; set; } = string.Empty;
}

public sealed class EntityCatalogueTrack
{
    public int Id { get; set; }
    public string SourceId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? Subtitle { get; set; }
    public string Url { get; set; } = string.Empty;
    public string? ContentType { get; set; }
    public bool IsRemote { get; set; }
    public string? ExternalId { get; set; }
    public string? AlbumSourceId { get; set; }
    public int? Year { get; set; }
    public int? DiscNumber { get; set; }
    public int? DiscCount { get; set; }
    public int? TrackNumber { get; set; }
    public double? DurationSeconds { get; set; }
    public long? FileSizeBytes { get; set; }
    public int? SampleRate { get; set; }
    public DateTime? AddedAtUtc { get; set; }
    public DateTime? SourceModifiedAtUtc { get; set; }
    public DateTime? SourceUpdatedAtUtc { get; set; }
    public string? ReleaseType { get; set; }
    public bool? IsCompilation { get; set; }
    public string? ArtworkTrackSourceId { get; set; }
    public string? WorkSourceId { get; set; }
    public string? WorkTitle { get; set; }
    public string? Performance { get; set; }
    public string? Grouping { get; set; }
    public string SeenRefreshId { get; set; } = string.Empty;
    public List<EntityCatalogueTrackArtist> Artists { get; set; } = [];
    public List<EntityCatalogueTrackGenre> Genres { get; set; } = [];
    public List<EntityCatalogueTrackStatistic> Statistics { get; set; } = [];
}

public sealed class EntityCatalogueTrackArtist
{
    public int Id { get; set; }
    public int TrackId { get; set; }
    public EntityCatalogueTrack Track { get; set; } = null!;
    public string ArtistSourceId { get; set; } = string.Empty;
}

public sealed class EntityCatalogueTrackGenre
{
    public int Id { get; set; }
    public int TrackId { get; set; }
    public EntityCatalogueTrack Track { get; set; } = null!;
    public string GenreSourceId { get; set; } = string.Empty;
}

public sealed class EntityCatalogueTrackStatistic
{
    public int Id { get; set; }
    public int TrackId { get; set; }
    public EntityCatalogueTrack Track { get; set; } = null!;
    public string Source { get; set; } = string.Empty;
    public int? Rating { get; set; }
    public int? PlayCount { get; set; }
    public DateTime? LastPlayedAtUtc { get; set; }
}

public sealed class EntityCatalogueVirtualLibrary
{
    public int Id { get; set; }
    public string SourceId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string SeenRefreshId { get; set; } = string.Empty;
    public List<EntityCatalogueVirtualLibraryTrack> Tracks { get; set; } = [];
}

public sealed class EntityCatalogueVirtualLibraryTrack
{
    public int Id { get; set; }
    public int VirtualLibraryId { get; set; }
    public EntityCatalogueVirtualLibrary VirtualLibrary { get; set; } = null!;
    public string TrackSourceId { get; set; } = string.Empty;
    public string SeenRefreshId { get; set; } = string.Empty;
}
