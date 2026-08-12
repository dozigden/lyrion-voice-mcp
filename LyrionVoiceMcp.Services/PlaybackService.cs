using LyrionVoiceMcp.Abstractions;

namespace LyrionVoiceMcp.Services;

public sealed class PlaybackService(
    ILmsPlayerClient lmsPlayerClient,
    ILmsPlaybackClient lmsPlaybackClient,
    ISearchResultReferenceCodec referenceCodec) : IPlaybackService
{
    public async Task<PlaybackOutcome> PlayAsync(
        string playerId,
        IReadOnlyList<string> references,
        PlaybackQueueMode mode,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(playerId))
        {
            return new PlaybackRejected(
                PlaybackRejectionReason.InvalidPlayer,
                "The player ID must not be empty.");
        }

        if (references is null || references.Count == 0)
        {
            return new PlaybackRejected(
                PlaybackRejectionReason.EmptyItems,
                "At least one search-result reference is required.");
        }

        if (!Enum.IsDefined(mode))
        {
            return new PlaybackRejected(
                PlaybackRejectionReason.InvalidMode,
                "The playback queue mode is invalid.");
        }

        var identities = new MediaIdentity[references.Count];
        for (var index = 0; index < references.Count; index++)
        {
            var value = referenceCodec.TryDecode(references[index]);
            if (value is null)
            {
                return new PlaybackRejected(
                    PlaybackRejectionReason.InvalidReference,
                    $"Search-result item {index + 1} has an invalid reference.");
            }

            identities[index] = value.Identity;
        }

        var playersTask = lmsPlayerClient.GetPlayersAsync(cancellationToken);
        var playableItemsTask = Task.WhenAll(identities.Select(identity =>
            lmsPlaybackClient.HasPlayableItemAsync(identity, cancellationToken)));
        await Task.WhenAll(playersTask, playableItemsTask);

        var player = FindPlayer(await playersTask, playerId);
        if (player is null)
        {
            return new PlaybackRejected(
                PlaybackRejectionReason.PlayerNotFound,
                $"LMS player '{playerId}' was not found.");
        }

        var playableItems = await playableItemsTask;
        var missingItemIndex = Array.FindIndex(playableItems, playable => !playable);
        if (missingItemIndex >= 0)
        {
            return new PlaybackRejected(
                PlaybackRejectionReason.MediaNotFound,
                $"Search-result item {missingItemIndex + 1} no longer resolves to playable media.");
        }

        var shouldStartAppendedItems = mode == PlaybackQueueMode.Append
            && (!player.PoweredOn || player.PlaybackState == PlayerPlaybackState.Stopped);
        var firstAppendedQueueIndex = shouldStartAppendedItems
            ? await lmsPlaybackClient.GetQueueCountAsync(player.Id, cancellationToken)
            : 0;

        if (!player.PoweredOn)
        {
            await lmsPlaybackClient.PowerOnAsync(player.Id, cancellationToken);
        }

        if (mode == PlaybackQueueMode.Replace)
        {
            await lmsPlaybackClient.LoadAsync(
                player.Id,
                identities[0],
                cancellationToken);
            foreach (var identity in identities.Skip(1))
            {
                await lmsPlaybackClient.AddAsync(
                    player.Id,
                    identity,
                    cancellationToken);
            }
        }
        else
        {
            foreach (var identity in identities)
            {
                await lmsPlaybackClient.AddAsync(
                    player.Id,
                    identity,
                    cancellationToken);
            }

            if (shouldStartAppendedItems)
            {
                await lmsPlaybackClient.StartAtAsync(
                    player.Id,
                    firstAppendedQueueIndex,
                    cancellationToken);
            }
        }

        var updatedPlayers = await lmsPlayerClient.GetPlayersAsync(cancellationToken);
        return new PlaybackSucceeded(FindUpdatedPlayer(updatedPlayers, player.Id));
    }

    private static LmsPlayerStatus? FindPlayer(
        IReadOnlyList<LmsPlayerStatus> players,
        string playerId) =>
        players.FirstOrDefault(player =>
            string.Equals(player.Id, playerId, StringComparison.OrdinalIgnoreCase));

    private static LmsPlayerStatus FindUpdatedPlayer(
        IReadOnlyList<LmsPlayerStatus> players,
        string playerId) =>
        players.FirstOrDefault(player =>
            string.Equals(player.Id, playerId, StringComparison.OrdinalIgnoreCase))
        ?? throw new LmsRequestException(
            "The selected LMS player was no longer available after playback changed.");
}
