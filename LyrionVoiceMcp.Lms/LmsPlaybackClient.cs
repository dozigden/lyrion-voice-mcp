using LyrionVoiceMcp.Abstractions;

namespace LyrionVoiceMcp.Lms;

public sealed class LmsPlaybackClient(LmsJsonRpcClient jsonRpcClient) : ILmsPlaybackClient
{
    public async Task<int> GetPlayableItemCountAsync(
        MediaIdentity identity,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(identity);

        var result = await jsonRpcClient.SendAsync(
            BuildPlayableItemQuery(identity),
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
        MediaIdentity identity,
        CancellationToken cancellationToken) =>
        SubmitAsync(playerId, identity, "load", cancellationToken);

    public Task AddAsync(
        string playerId,
        MediaIdentity identity,
        CancellationToken cancellationToken) =>
        SubmitAsync(playerId, identity, "add", cancellationToken);

    public Task InsertAsync(
        string playerId,
        MediaIdentity identity,
        CancellationToken cancellationToken) =>
        SubmitAsync(playerId, identity, "insert", cancellationToken);

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
        MediaIdentity identity,
        string command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(identity);

        var result = await jsonRpcClient.SendAsync(
            playerId,
            ["playlistcontrol", $"cmd:{command}", BuildIdentityParameter(identity)],
            cancellationToken);
        var count = LmsJson.ReadInt(result, "count");
        if (count is null or < 1)
        {
            throw new LmsRequestException(
                "LMS did not resolve the submitted item to any playable tracks.");
        }
    }

    private static object[] BuildPlayableItemQuery(MediaIdentity identity)
    {
        var identityParameter = BuildIdentityParameter(identity);
        return identity.Kind == MediaEntityKind.Playlist
            ? ["playlists", "tracks", 0, 1, identityParameter, "tags:i"]
            : ["titles", 0, 1, identityParameter, "tags:i"];
    }

    private static string BuildIdentityParameter(MediaIdentity identity) =>
        identity.Kind switch
        {
            MediaEntityKind.Artist => $"artist_id:{identity.Id}",
            MediaEntityKind.Album => $"album_id:{identity.Id}",
            MediaEntityKind.Track => $"track_id:{identity.Id}",
            MediaEntityKind.Playlist => $"playlist_id:{identity.Id}",
            _ => throw new ArgumentOutOfRangeException(
                nameof(identity),
                $"Unsupported media entity kind {identity.Kind}.")
        };
}
