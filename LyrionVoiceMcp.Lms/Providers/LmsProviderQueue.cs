using System.Text.Json;
using LyrionVoiceMcp.Abstractions;
using LyrionVoiceMcp.Abstractions.Providers;

namespace LyrionVoiceMcp.Lms.Providers;

internal sealed class LmsProviderQueue(LmsJsonRpcClient rpc, string providerName, Func<string?, string?> identify)
{
    private string? Identity(JsonElement item) => item.ValueKind == JsonValueKind.Object
        ? identify(LmsJson.ReadString(item, "url")) : null;

    public async Task SubmitAsync(string playerId, string audioUrl, string audioIdentity,
        ProviderPlaybackCommand command, CancellationToken cancellationToken)
    {
        var verb = command switch
        {
            ProviderPlaybackCommand.Load => "play",
            ProviderPlaybackCommand.Add => "add",
            ProviderPlaybackCommand.Insert => "insert",
            _ => throw new ArgumentOutOfRangeException(nameof(command))
        };
        var before = await ReadQueueAsync(playerId, cancellationToken);
        if (command != ProviderPlaybackCommand.Load && before.Items.Count >= 300)
            throw new LmsRequestException("The LMS queue is full.");
        if (command == ProviderPlaybackCommand.Insert && before.Items.Count > 0
            && (before.CurrentIndex is null or < 0 || before.CurrentIndex >= before.Items.Count))
            throw new LmsRequestException("LMS did not identify the current queue position.");
        var expectedIndex = command switch
        {
            ProviderPlaybackCommand.Load => 0,
            ProviderPlaybackCommand.Add => before.Items.Count,
            ProviderPlaybackCommand.Insert => before.Items.Count == 0 ? 0 : (before.CurrentIndex ?? -1) + 1,
            _ => throw new ArgumentOutOfRangeException(nameof(command))
        };
        await rpc.SendAsync(playerId, ["playlist", verb, audioUrl], cancellationToken);
        // LMS may reshuffle an append. Confirm an additional occurrence even when
        // its position changes; an older occurrence alone is never sufficient.
        var after = await ReadQueueAsync(playerId, cancellationToken);
        var expectedCount = command == ProviderPlaybackCommand.Load ? 1 : before.Items.Count + 1;
        var expectedOccurrences = command == ProviderPlaybackCommand.Load ? 1 : AudioOccurrences(before, audioIdentity) + 1;
        var shuffledAppend = command == ProviderPlaybackCommand.Add && before.ShuffleMode > 0;
        var positionMatches = expectedIndex >= 0 && expectedIndex < after.Items.Count
            && Identity(after.Items[expectedIndex]) == audioIdentity;
        if (after.Items.Count != expectedCount || after.ShuffleMode != before.ShuffleMode
            || AudioOccurrences(after, audioIdentity) != expectedOccurrences
            || (!shuffledAppend && !positionMatches))
            throw new LmsRequestException($"LMS did not confirm the submitted {providerName} item in the queue.");
    }

    private int AudioOccurrences(QueueSnapshot queue, string identity) =>
        queue.Items.Count(item => Identity(item) == identity);

    private async Task<QueueSnapshot> ReadQueueAsync(string playerId, CancellationToken cancellationToken)
    {
        var queue = await rpc.SendAsync(playerId, ["status", 0, 300, "tags:u"], cancellationToken);
        var count = LmsJson.ReadInt(queue, "playlist_tracks");
        if (count is null or < 0 or > 300) throw new LmsRequestException("LMS returned an invalid queue count.");
        var shuffle = LmsJson.ReadInt(queue, "playlist shuffle");
        if (shuffle is null or < 0 or > 2) throw new LmsRequestException("LMS returned an invalid shuffle mode.");
        if (!queue.TryGetProperty("playlist_loop", out var items))
            return count == 0 ? new QueueSnapshot([], null, shuffle.Value) : throw new LmsRequestException("LMS returned an incomplete queue.");
        if (items.ValueKind != JsonValueKind.Array || items.GetArrayLength() != count)
            throw new LmsRequestException("LMS returned an incomplete queue.");
        var index = LmsJson.ReadInt(queue, "playlist_cur_index");
        return new QueueSnapshot(items.EnumerateArray().ToArray(), index, shuffle.Value);
    }

    private sealed record QueueSnapshot(IReadOnlyList<JsonElement> Items, int? CurrentIndex, int ShuffleMode);
}
