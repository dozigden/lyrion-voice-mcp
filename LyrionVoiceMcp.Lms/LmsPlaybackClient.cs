using LyrionVoiceMcp.Abstractions;

namespace LyrionVoiceMcp.Lms;

public sealed class LmsPlaybackClient(LmsJsonRpcClient jsonRpcClient) : ILmsPlaybackClient
{
    public async Task<int> GetPlayableItemCountAsync(
        PlayableMedia media,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(media);

        var result = await jsonRpcClient.SendAsync(
            BuildPlayableItemQuery(media),
            cancellationToken);
        var count = LmsJson.ReadInt(result, "count");
        if (count is null)
        {
            throw new LmsRequestException(
                "LMS playable-item response did not include a valid count.");
        }

        if (count < 0)
        {
            throw new LmsRequestException(
                "LMS playable-item response included an invalid count.");
        }

        return count.Value;
    }

    public async Task PowerOnAsync(
        string playerId,
        CancellationToken cancellationToken)
    {
        await jsonRpcClient.SendAsync(
            playerId,
            ["power", 1, 1],
            cancellationToken);
        var result = await jsonRpcClient.SendAsync(
            playerId,
            ["power", "?"],
            cancellationToken);
        try
        {
            if (!LmsJson.ReadRequiredBoolean(result, "_power", "power"))
            {
                throw new LmsRequestException("LMS did not power on the selected player.");
            }
        }
        catch (InvalidOperationException exception)
        {
            throw new LmsRequestException(exception.Message, exception);
        }
    }

    public async Task<int> GetQueueCountAsync(
        string playerId,
        CancellationToken cancellationToken)
    {
        var result = await jsonRpcClient.SendAsync(
            playerId,
            ["playlist", "tracks", "?"],
            cancellationToken);
        var count = LmsJson.ReadInt(result, "_tracks");
        if (count is null or < 0)
        {
            throw new LmsRequestException(
                "LMS queue response did not include a valid track count.");
        }

        return count.Value;
    }

    public Task LoadAsync(
        string playerId,
        PlayableMedia media,
        CancellationToken cancellationToken) =>
        SubmitAsync(playerId, media, "load", cancellationToken);

    public Task AddAsync(
        string playerId,
        PlayableMedia media,
        CancellationToken cancellationToken) =>
        SubmitAsync(playerId, media, "add", cancellationToken);

    public Task InsertAsync(
        string playerId,
        PlayableMedia media,
        CancellationToken cancellationToken) =>
        SubmitAsync(playerId, media, "insert", cancellationToken);

    public async Task ClearAsync(
        string playerId,
        CancellationToken cancellationToken)
    {
        await jsonRpcClient.SendAsync(
            playerId,
            ["playlist", "clear"],
            cancellationToken);
    }

    private async Task SubmitAsync(
        string playerId,
        PlayableMedia media,
        string command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(media);

        var result = await jsonRpcClient.SendAsync(
            playerId,
            ["playlistcontrol", $"cmd:{command}", .. BuildSelectionParameters(media)],
            cancellationToken);
        var count = LmsJson.ReadInt(result, "count");
        if (count is null or < 1)
        {
            throw new LmsRequestException(
                "LMS did not resolve the submitted item to any playable tracks.");
        }
    }

    private static object[] BuildPlayableItemQuery(PlayableMedia media)
    {
        var selectionParameters = BuildSelectionParameters(media);
        return media.Identity.Kind == MediaEntityKind.Playlist
            ? ["playlists", "tracks", 0, 1, .. selectionParameters, "tags:i"]
            : ["titles", 0, 1, .. selectionParameters, "tags:i"];
    }

    private static IReadOnlyList<string> BuildSelectionParameters(PlayableMedia media)
    {
        var identityParameter = media.Identity.Kind switch
        {
            MediaEntityKind.Artist => $"artist_id:{media.Identity.Id}",
            MediaEntityKind.Album => $"album_id:{media.Identity.Id}",
            MediaEntityKind.Track => $"track_id:{media.Identity.Id}",
            MediaEntityKind.Playlist => $"playlist_id:{media.Identity.Id}",
            _ => throw new ArgumentOutOfRangeException(
                nameof(media),
                $"Unsupported media entity kind {media.Identity.Kind}.")
        };

        return media.ArtistScope switch
        {
            null => [identityParameter],
            ArtistSelectionScope.AlbumArtist
                when media.Identity.Kind == MediaEntityKind.Artist =>
                [identityParameter, "role_id:ALBUMARTIST"],
            _ => throw new ArgumentException(
                "The artist selection scope is not valid for this media identity.",
                nameof(media))
        };
    }
}
