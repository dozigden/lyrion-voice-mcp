using System.Text.Json;
using LyrionVoiceMcp.Abstractions;

namespace LyrionVoiceMcp.Lms;

public sealed class LmsSearchClient(LmsJsonRpcClient jsonRpcClient) : ILmsSearchClient
{
    private const int ItemsPerCategory = 20;

    public async Task<IReadOnlyList<LmsSearchCandidate>> SearchAsync(
        string query,
        CancellationToken cancellationToken)
    {
        var librarySearch = jsonRpcClient.SendAsync(
            ["search", 0, ItemsPerCategory, $"term:{query}"],
            cancellationToken);
        var playlistSearch = jsonRpcClient.SendAsync(
            ["playlists", 0, ItemsPerCategory, $"search:{query}"],
            cancellationToken);

        await Task.WhenAll(librarySearch, playlistSearch);

        var candidates = new List<LmsSearchCandidate>();
        AppendLibraryCandidates(candidates, await librarySearch);
        AppendPlaylistCandidates(candidates, await playlistSearch);
        return candidates;
    }

    private static void AppendLibraryCandidates(
        List<LmsSearchCandidate> candidates,
        JsonElement result)
    {
        AppendCandidates(
            candidates,
            result,
            "contributors_loop",
            MediaEntityKind.Artist,
            "contributor_id",
            "contributor");
        AppendCandidates(
            candidates,
            result,
            "albums_loop",
            MediaEntityKind.Album,
            "album_id",
            "album");
        AppendCandidates(
            candidates,
            result,
            "tracks_loop",
            MediaEntityKind.Track,
            "track_id",
            "track");
    }

    private static void AppendPlaylistCandidates(
        List<LmsSearchCandidate> candidates,
        JsonElement result) =>
        AppendCandidates(
            candidates,
            result,
            "playlists_loop",
            MediaEntityKind.Playlist,
            "id",
            "playlist");

    private static void AppendCandidates(
        List<LmsSearchCandidate> candidates,
        JsonElement result,
        string loopName,
        MediaEntityKind kind,
        string idName,
        string titleName)
    {
        if (!result.TryGetProperty(loopName, out var loop))
        {
            return;
        }

        if (loop.ValueKind != JsonValueKind.Array)
        {
            throw new LmsRequestException(
                $"LMS search response did not include a valid {loopName} array.");
        }

        foreach (var item in loop.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                throw new LmsRequestException(
                    $"LMS search response contained an invalid {loopName} item.");
            }

            var id = LmsJson.ReadRequiredString(item, idName, "search");
            var title = LmsJson.ReadRequiredString(item, titleName, "search");
            var artist = kind is MediaEntityKind.Album or MediaEntityKind.Track
                ? LmsJson.ReadString(item, "artist")
                : null;
            var album = kind == MediaEntityKind.Track
                ? LmsJson.ReadString(item, "album")
                : null;
            candidates.Add(new LmsSearchCandidate(
                new MediaIdentity(kind, id),
                title,
                artist,
                album));
        }
    }
}
