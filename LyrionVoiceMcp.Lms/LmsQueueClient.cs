using System.Text.Json;
using LyrionVoiceMcp.Abstractions;

namespace LyrionVoiceMcp.Lms;

public sealed class LmsQueueClient(LmsJsonRpcClient jsonRpcClient) : ILmsQueueClient
{
    private const int MaximumQueueItems = 300;
    private const string QueueTags = "aAld";

    public async Task<LmsPlayerQueue> GetQueueAsync(
        string playerId,
        CancellationToken cancellationToken)
    {
        var result = await jsonRpcClient.SendAsync(
            playerId,
            ["status", 0, MaximumQueueItems, $"tags:{QueueTags}"],
            cancellationToken);

        try
        {
            return ReadQueue(playerId, result);
        }
        catch (InvalidOperationException exception)
        {
            throw new LmsRequestException(exception.Message, exception);
        }
    }

    private static LmsPlayerQueue ReadQueue(string playerId, JsonElement result)
    {
        var trackCount = LmsJson.ReadInt(result, "playlist_tracks");
        if (trackCount is null or < 0)
        {
            throw new InvalidOperationException(
                "LMS queue response did not include a valid playlist_tracks value.");
        }

        if (trackCount > MaximumQueueItems)
        {
            throw new InvalidOperationException(
                $"LMS queue contains more than the supported {MaximumQueueItems} items.");
        }

        if (trackCount == 0)
        {
            return new LmsPlayerQueue(playerId, null, []);
        }

        var currentIndex = LmsJson.ReadInt(result, "playlist_cur_index");
        if (currentIndex is null or < 0 || currentIndex >= trackCount)
        {
            throw new InvalidOperationException(
                "LMS queue response did not include a valid playlist_cur_index value.");
        }

        if (!result.TryGetProperty("playlist_loop", out var loop)
            || loop.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException(
                "LMS queue response did not include a valid playlist_loop array.");
        }

        var currentTitle = LmsJson.ReadString(result, "current_title");
        if (string.IsNullOrWhiteSpace(currentTitle))
        {
            currentTitle = null;
        }

        var items = loop.EnumerateArray()
            .Select(item => ReadItem(item, currentIndex.Value, currentTitle, trackCount.Value))
            .ToArray();
        if (items.Length != trackCount
            || items.Where((item, expectedIndex) => item.Index != expectedIndex).Any())
        {
            throw new InvalidOperationException(
                "LMS queue response did not include every queued item.");
        }

        return new LmsPlayerQueue(playerId, currentIndex, items);
    }

    private static LmsQueueItem ReadItem(
        JsonElement item,
        int currentIndex,
        string? currentTitle,
        int trackCount)
    {
        if (item.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException(
                "LMS queue response contained an invalid playlist_loop item.");
        }

        var index = LmsJson.ReadInt(item, "playlist index");
        if (index is null or < 0 || index >= trackCount)
        {
            throw new InvalidOperationException(
                "LMS queue item did not include a valid playlist index.");
        }

        var title = index == currentIndex && currentTitle is not null
            ? currentTitle
            : LmsJson.ReadRequiredString(item, "title", "queue item");
        var artist = LmsJson.ReadString(item, "trackartist")
            ?? LmsJson.ReadString(item, "artist")
            ?? LmsJson.ReadString(item, "albumartist");
        return new LmsQueueItem(
            index.Value,
            title,
            artist,
            LmsJson.ReadString(item, "album"),
            LmsJson.ReadDouble(item, "duration"));
    }
}
