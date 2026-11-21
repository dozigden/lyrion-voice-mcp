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
        => await SubmitCollectionAsync(playerId, command, [audioIdentity], verb => ["playlist", verb, audioUrl], cancellationToken);

    public async Task SubmitCollectionAsync(string playerId, ProviderPlaybackCommand command,
        IReadOnlyList<string> identities, Func<string, object[]> createCommand, CancellationToken cancellationToken)
    {
        var verb = command switch
        {
            ProviderPlaybackCommand.Load => "play",
            ProviderPlaybackCommand.Add => "add",
            ProviderPlaybackCommand.Insert => "insert",
            _ => throw new ArgumentOutOfRangeException(nameof(command))
        };
        var before = await ReadQueueAsync(playerId, cancellationToken);
        var initialSize = command == ProviderPlaybackCommand.Load ? 0 : before.Items.Count;
        if (identities.Count == 0) throw new LmsRequestException("The selection contains no playable tracks.");
        if (initialSize + identities.Count > 300)
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
        await rpc.SendAsync(playerId, createCommand(verb), cancellationToken);
        // LMS may reshuffle an append. Confirm an additional occurrence even when
        // its position changes; an older occurrence alone is never sufficient.
        var after = await ReadQueueAsync(playerId, cancellationToken);
        var previous = command == ProviderPlaybackCommand.Load ? Array.Empty<string?>() : before.Items.Select(Identity).ToArray();
        var actual = after.Items.Select(Identity).ToArray();
        var shuffled = before.ShuffleMode > 0 && command != ProviderPlaybackCommand.Insert;
        var positionMatches = shuffled || actual.Skip(expectedIndex).Take(identities.Count).SequenceEqual(identities);
        var occurrencesMatch = identities.GroupBy(id => id).All(group =>
            actual.Count(id => id == group.Key) == previous.Count(id => id == group.Key) + group.Count());
        if (actual.Length != initialSize + identities.Count || !occurrencesMatch || !positionMatches
            || before.ShuffleMode != after.ShuffleMode)
            throw new LmsRequestException($"LMS did not confirm the submitted {providerName} item in the queue.");
    }

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
