using System.Text.Json;
using LyrionVoiceMcp.Abstractions;

namespace LyrionVoiceMcp.Lms;

public sealed class LmsPlayerClient(LmsJsonRpcClient jsonRpcClient) : ILmsPlayerClient
{
    public async Task<IReadOnlyList<LmsPlayerStatus>> GetPlayersAsync(
        CancellationToken cancellationToken)
    {
        var result = await jsonRpcClient.SendAsync(
            ["players", 0],
            cancellationToken);
        var players = ReadPlayers(result);
        if (players.Count == 0)
        {
            return [];
        }

        var statuses = await Task.WhenAll(players.Select(player =>
            ReadStatusAsync(player, cancellationToken)));
        return statuses;
    }

    private async Task<LmsPlayerStatus> ReadStatusAsync(
        DiscoveredPlayer player,
        CancellationToken cancellationToken)
    {
        var result = await jsonRpcClient.SendAsync(
            player.Id,
            ["mode", "?"],
            cancellationToken);
        string mode;
        try
        {
            mode = LmsJson.ReadRequiredString(result, "_mode", "player mode");
        }
        catch (InvalidOperationException exception)
        {
            throw new LmsRequestException(exception.Message, exception);
        }

        return new LmsPlayerStatus(
            player.Id,
            player.Name,
            player.PoweredOn,
            MapPlaybackState(mode));
    }

    private static IReadOnlyList<DiscoveredPlayer> ReadPlayers(JsonElement result)
    {
        if (!result.TryGetProperty("players_loop", out var loop))
        {
            var count = LmsJson.ReadInt(result, "count");
            if (count is null or 0)
            {
                return [];
            }

            throw new LmsRequestException(
                "LMS players response did not include a players_loop array.");
        }

        if (loop.ValueKind != JsonValueKind.Array)
        {
            throw new LmsRequestException(
                "LMS players response did not include a valid players_loop array.");
        }

        return loop.EnumerateArray().Select(item =>
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                throw new LmsRequestException(
                    "LMS players response contained an invalid players_loop item.");
            }

            try
            {
                return new DiscoveredPlayer(
                    LmsJson.ReadRequiredString(item, "playerid", "players"),
                    LmsJson.ReadRequiredString(item, "name", "players"),
                    LmsJson.ReadRequiredBoolean(item, "power", "players"));
            }
            catch (InvalidOperationException exception)
            {
                throw new LmsRequestException(exception.Message, exception);
            }
        }).ToArray();
    }

    private static PlayerPlaybackState MapPlaybackState(string mode) =>
        mode.ToLowerInvariant() switch
        {
            "play" => PlayerPlaybackState.Playing,
            "pause" => PlayerPlaybackState.Paused,
            "stop" => PlayerPlaybackState.Stopped,
            _ => PlayerPlaybackState.Unknown
        };

    private sealed record DiscoveredPlayer(
        string Id,
        string Name,
        bool PoweredOn);
}
