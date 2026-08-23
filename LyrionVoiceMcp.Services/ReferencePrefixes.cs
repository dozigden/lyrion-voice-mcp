using LyrionVoiceMcp.Abstractions;

namespace LyrionVoiceMcp.Services;

internal static class ReferencePrefixes
{
    public static string ForMedia(MediaEntityKind kind) => kind switch
    {
        MediaEntityKind.Artist => "artist_",
        MediaEntityKind.Album => "album_",
        MediaEntityKind.Track => "track_",
        MediaEntityKind.Playlist => "playlist_",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
    };

    public static string ForBrowse(BrowseReferenceValue value)
    {
        if (value.Media is { } media)
        {
            return ForMedia(media.Identity.Kind);
        }

        var target = value.Target
            ?? throw new ArgumentException(
                "The browse reference has no target or media.",
                nameof(value));
        return target.Kind switch
        {
            BrowseTargetKind.AlbumArtists => "album_artists_",
            BrowseTargetKind.Artists => "artists_",
            BrowseTargetKind.Albums => "albums_",
            BrowseTargetKind.Genres => "genres_",
            BrowseTargetKind.Playlists => "playlists_",
            BrowseTargetKind.RecentlyAddedAlbums => "recent_albums_",
            BrowseTargetKind.Years => "years_",
            BrowseTargetKind.RatingBuckets => "ratings_",
            BrowseTargetKind.AlbumArtistAlbums when target.Offset == 0 =>
                "album_artist_",
            BrowseTargetKind.ArtistAlbums when target.Offset == 0 => "artist_",
            BrowseTargetKind.GenreAlbums when target.Offset == 0 => "genre_",
            BrowseTargetKind.YearAlbums when target.Offset == 0 => "year_",
            BrowseTargetKind.RatingTracks when target.Offset == 0 => "rating_",
            BrowseTargetKind.AlbumArtistAlbums or
            BrowseTargetKind.ArtistAlbums or
            BrowseTargetKind.GenreAlbums or
            BrowseTargetKind.YearAlbums => "albums_",
            BrowseTargetKind.AlbumTracks or
            BrowseTargetKind.PlaylistTracks or
            BrowseTargetKind.RatingTracks => "tracks_",
            _ => throw new ArgumentOutOfRangeException(
                nameof(value),
                target.Kind,
                "The browse target kind is unsupported.")
        };
    }
}
