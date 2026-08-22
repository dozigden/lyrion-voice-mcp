namespace LyrionVoiceMcp.Abstractions;

public enum BrowseItemKind
{
    Category,
    AlbumArtist,
    Artist,
    Album,
    Genre,
    Playlist,
    Track,
    Year
}

public enum LmsBrowseQueryKind
{
    AlbumArtists,
    Artists,
    Albums,
    Genres,
    Playlists,
    RecentlyAddedAlbums,
    Years,
    AlbumArtistAlbums,
    ArtistAlbums,
    GenreAlbums,
    YearAlbums,
    AlbumTracks,
    PlaylistTracks
}

public enum BrowseTargetKind
{
    AlbumArtists,
    Artists,
    Albums,
    Genres,
    Playlists,
    RecentlyAddedAlbums,
    Years,
    AlbumArtistAlbums,
    ArtistAlbums,
    GenreAlbums,
    YearAlbums,
    AlbumTracks,
    PlaylistTracks,
    RatingBuckets,
    RatingTracks
}

public sealed record LmsBrowseRequest(
    LmsBrowseQueryKind Kind,
    string? FilterId,
    int Offset,
    int Limit);

public sealed record LmsBrowseItem(
    BrowseItemKind Kind,
    string Id,
    string Title,
    string? Artist,
    string? Album);

public sealed record LmsBrowsePage(
    IReadOnlyList<LmsBrowseItem> Items,
    int TotalCount);

public interface ILmsBrowseClient
{
    Task<LmsBrowsePage> BrowseAsync(
        LmsBrowseRequest request,
        CancellationToken cancellationToken);
}

public sealed record BrowseTarget(
    BrowseTargetKind Kind,
    string? FilterId,
    int Offset);

public sealed record RatingBrowseTrack(
    MediaIdentity Identity,
    string Title,
    string? Artist,
    string? Album,
    int NativeRating);

public sealed record RatingBrowsePage(
    IReadOnlyList<RatingBrowseTrack> Items,
    bool HasMore);

public interface IRatingBrowseResolver
{
    Task<RatingBrowsePage> BrowseAsync(
        int bucket,
        int offset,
        int limit,
        CancellationToken cancellationToken);
}

public sealed record BrowseReferenceValue(
    BrowseTarget? Target,
    PlayableMedia? Media,
    string? SearchCorrelationId = null);

public interface IBrowseReferenceCodec
{
    string Encode(BrowseReferenceValue value);

    BrowseReferenceValue? TryDecode(string reference);
}

public sealed record PlayableReferenceValue(
    PlayableMedia Media,
    string? SearchCorrelationId);

public interface IPlayableReferenceResolver
{
    PlayableReferenceValue? Resolve(string reference);
}

public sealed record BrowseItemResult(
    string Reference,
    BrowseItemKind Kind,
    string Title,
    string? Artist,
    string? Album,
    bool Browsable,
    bool Playable,
    int? NativeRating = null);

public enum BrowseRejectionReason
{
    InvalidReference,
    NotBrowsable,
    BrowseUnavailable
}

public abstract record BrowseOutcome;

public sealed record BrowseSucceeded(
    IReadOnlyList<BrowseItemResult> Items,
    string? Continuation) : BrowseOutcome;

public sealed record BrowseRejected(
    BrowseRejectionReason Reason,
    string Message) : BrowseOutcome;

public interface IBrowseService
{
    Task<BrowseOutcome> BrowseAsync(
        string? reference,
        CancellationToken cancellationToken);
}
