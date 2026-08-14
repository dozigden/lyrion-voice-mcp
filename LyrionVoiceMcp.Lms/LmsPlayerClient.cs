using System.Text.Json;
using LyrionVoiceMcp.Abstractions;

namespace LyrionVoiceMcp.Lms;

public sealed class LmsPlayerClient(LmsJsonRpcClient jsonRpcClient) : ILmsPlayerClient
{
    // Request display metadata for the current local track or plugin stream.
    private const string StatusTags = "cgAABbehldiqtyrSuoKLNJ";

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
        var statusRequest = jsonRpcClient.SendAsync(
            player.Id,
            ["status", "-", 1, $"tags:{StatusTags}"],
            cancellationToken);
        var mutingRequest = jsonRpcClient.SendAsync(
            player.Id,
            ["mixer", "muting", "?"],
            cancellationToken);
        await Task.WhenAll(statusRequest, mutingRequest);

        try
        {
            var status = await statusRequest;
            var muting = await mutingRequest;
            var mode = LmsJson.ReadRequiredString(status, "mode", "player status");
            return new LmsPlayerStatus(
                player.Id,
                player.Name,
                LmsJson.ReadRequiredBoolean(status, "power", "player status"),
                MapPlaybackState(mode),
                LmsJson.ReadInt(status, "mixer volume"),
                ReadNullableBoolean(muting, "_muting", "player mute"),
                ReadNowPlaying(status));
        }
        catch (InvalidOperationException exception)
        {
            throw new LmsRequestException(exception.Message, exception);
        }
    }

    private static LmsNowPlaying? ReadNowPlaying(JsonElement status)
    {
        var currentTitle = LmsJson.ReadString(status, "current_title");
        if (string.IsNullOrWhiteSpace(currentTitle))
        {
            currentTitle = null;
        }

        if (!status.TryGetProperty("playlist_loop", out var loop))
        {
            return CreateRemoteNowPlaying(status, currentTitle);
        }

        if (loop.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException(
                "LMS player status response did not include a valid playlist_loop array.");
        }

        var items = loop.EnumerateArray();
        if (!items.MoveNext())
        {
            return CreateRemoteNowPlaying(status, currentTitle);
        }

        var item = items.Current;
        if (item.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException(
                "LMS player status response contained an invalid playlist_loop item.");
        }

        var title = currentTitle
            ?? LmsJson.ReadRequiredString(item, "title", "player status");
        var artist = LmsJson.ReadString(item, "trackartist")
            ?? LmsJson.ReadString(item, "artist")
            ?? LmsJson.ReadString(item, "albumartist");
        var duration = LmsJson.ReadDouble(status, "duration")
            ?? LmsJson.ReadDouble(item, "duration");
        return new LmsNowPlaying(
            title,
            artist,
            LmsJson.ReadString(item, "album"),
            duration,
            LmsJson.ReadDouble(status, "time"));
    }

    private static LmsNowPlaying? CreateRemoteNowPlaying(
        JsonElement status,
        string? currentTitle) =>
        string.IsNullOrWhiteSpace(currentTitle)
            ? null
            : new LmsNowPlaying(
                currentTitle,
                null,
                null,
                LmsJson.ReadDouble(status, "duration"),
                LmsJson.ReadDouble(status, "time"));

    private static bool? ReadNullableBoolean(
        JsonElement element,
        string name,
        string responseName)
    {
        if (!element.TryGetProperty(name, out var property)
            || property.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (property.ValueKind == JsonValueKind.True)
        {
            return true;
        }

        if (property.ValueKind == JsonValueKind.False)
        {
            return false;
        }

        var value = LmsJson.ReadString(element, name);
        return value switch
        {
            "1" => true,
            "0" => false,
            _ => throw new InvalidOperationException(
                $"LMS {responseName} response contained an invalid {name} value.")
        };
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
                    LmsJson.ReadRequiredString(item, "name", "players"));
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
        string Name);
}
