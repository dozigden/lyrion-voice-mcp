using System.Text.Json;
using LyrionVoiceMcp.Abstractions;

namespace LyrionVoiceMcp.Lms;

public sealed class LmsBrowseClient(LmsJsonRpcClient jsonRpcClient) : ILmsBrowseClient
{
    public async Task<LmsBrowsePage> BrowseAsync(
        LmsBrowseRequest request,
        CancellationToken cancellationToken)
    {
        var command = BuildCommand(request);
        var result = await jsonRpcClient.SendAsync(command, cancellationToken);
        var totalCount = LmsJson.ReadInt(result, "count")
            ?? throw new LmsRequestException(
                "LMS browse response did not include a valid count.");
        if (totalCount < 0)
        {
            throw new LmsRequestException(
                "LMS browse response included a negative count.");
        }

        var (loopName, itemKind, idName, titleName) = ResponseShape(request.Kind);
        if (!result.TryGetProperty(loopName, out var loop))
        {
            return new LmsBrowsePage([], totalCount);
        }

        if (loop.ValueKind != JsonValueKind.Array)
        {
            throw new LmsRequestException(
                $"LMS browse response did not include a valid {loopName} array.");
        }

        var items = loop.EnumerateArray()
            .Select(item => MapItem(item, loopName, itemKind, idName, titleName))
            .ToArray();
        return new LmsBrowsePage(items, totalCount);
    }

    private static object[] BuildCommand(LmsBrowseRequest request)
    {
        if (request.Offset < 0 || request.Limit <= 0)
        {
            throw new InvalidOperationException(
                "The LMS browse offset must be non-negative and the limit must be positive.");
        }

        return request.Kind switch
        {
            LmsBrowseQueryKind.AlbumArtists =>
                ["artists", request.Offset, request.Limit, "role_id:ALBUMARTIST"],
            LmsBrowseQueryKind.Artists =>
                ["artists", request.Offset, request.Limit],
            LmsBrowseQueryKind.Albums =>
                ["albums", request.Offset, request.Limit, "tags:la"],
            LmsBrowseQueryKind.Genres =>
                ["genres", request.Offset, request.Limit],
            LmsBrowseQueryKind.Playlists =>
                ["playlists", request.Offset, request.Limit],
            LmsBrowseQueryKind.RecentlyAddedAlbums =>
                ["albums", request.Offset, request.Limit, "sort:new", "tags:la"],
            LmsBrowseQueryKind.Years =>
                ["years", request.Offset, request.Limit, "hasAlbums:1"],
            LmsBrowseQueryKind.AlbumArtistAlbums =>
                [
                    "albums",
                    request.Offset,
                    request.Limit,
                    RequiredFilter("artist_id", request.FilterId),
                    "role_id:ALBUMARTIST",
                    "tags:la"
                ],
            LmsBrowseQueryKind.ArtistAlbums =>
                [
                    "albums",
                    request.Offset,
                    request.Limit,
                    RequiredFilter("artist_id", request.FilterId),
                    "tags:la"
                ],
            LmsBrowseQueryKind.GenreAlbums =>
                [
                    "albums",
                    request.Offset,
                    request.Limit,
                    RequiredFilter("genre_id", request.FilterId),
                    "tags:la"
                ],
            LmsBrowseQueryKind.YearAlbums =>
                [
                    "albums",
                    request.Offset,
                    request.Limit,
                    RequiredFilter("year", request.FilterId),
                    "tags:la"
                ],
            LmsBrowseQueryKind.AlbumTracks =>
                [
                    "titles",
                    request.Offset,
                    request.Limit,
                    RequiredFilter("album_id", request.FilterId),
                    "sort:tracknum",
                    "tags:ald"
                ],
            LmsBrowseQueryKind.PlaylistTracks =>
                [
                    "playlists",
                    "tracks",
                    request.Offset,
                    request.Limit,
                    RequiredFilter("playlist_id", request.FilterId),
                    "tags:ald"
                ],
            _ => throw new InvalidOperationException(
                $"Unsupported LMS browse query {request.Kind}.")
        };
    }

    private static string RequiredFilter(string name, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(
                $"The LMS browse query requires a {name} filter.");
        }

        return $"{name}:{value}";
    }

    private static (string Loop, BrowseItemKind Kind, string Id, string Title) ResponseShape(
        LmsBrowseQueryKind kind) => kind switch
        {
            LmsBrowseQueryKind.AlbumArtists =>
                ("artists_loop", BrowseItemKind.AlbumArtist, "id", "artist"),
            LmsBrowseQueryKind.Artists =>
                ("artists_loop", BrowseItemKind.Artist, "id", "artist"),
            LmsBrowseQueryKind.Albums or
            LmsBrowseQueryKind.RecentlyAddedAlbums or
            LmsBrowseQueryKind.AlbumArtistAlbums or
            LmsBrowseQueryKind.ArtistAlbums or
            LmsBrowseQueryKind.GenreAlbums or
            LmsBrowseQueryKind.YearAlbums =>
                ("albums_loop", BrowseItemKind.Album, "id", "album"),
            LmsBrowseQueryKind.Genres =>
                ("genres_loop", BrowseItemKind.Genre, "id", "genre"),
            LmsBrowseQueryKind.Playlists =>
                ("playlists_loop", BrowseItemKind.Playlist, "id", "playlist"),
            LmsBrowseQueryKind.Years =>
                ("years_loop", BrowseItemKind.Year, "year", "year"),
            LmsBrowseQueryKind.AlbumTracks =>
                ("titles_loop", BrowseItemKind.Track, "id", "title"),
            LmsBrowseQueryKind.PlaylistTracks =>
                ("playlisttracks_loop", BrowseItemKind.Track, "id", "title"),
            _ => throw new InvalidOperationException(
                $"Unsupported LMS browse query {kind}.")
        };

    private static LmsBrowseItem MapItem(
        JsonElement item,
        string loopName,
        BrowseItemKind kind,
        string idName,
        string titleName)
    {
        if (item.ValueKind != JsonValueKind.Object)
        {
            throw new LmsRequestException(
                $"LMS browse response contained an invalid {loopName} item.");
        }

        return new LmsBrowseItem(
            kind,
            ReadRequiredString(item, idName),
            ReadRequiredString(item, titleName),
            kind is BrowseItemKind.Album or BrowseItemKind.Track
                ? LmsJson.ReadString(item, "artist")
                : null,
            kind == BrowseItemKind.Track
                ? LmsJson.ReadString(item, "album")
                : null);
    }

    private static string ReadRequiredString(JsonElement item, string name)
    {
        var value = LmsJson.ReadString(item, name);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new LmsRequestException(
                $"LMS browse response contained an item without {name}.");
        }

        return value;
    }
}
