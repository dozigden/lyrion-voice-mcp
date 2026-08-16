using LyrionVoiceMcp.Abstractions;
using Microsoft.Extensions.Logging;

namespace LyrionVoiceMcp.Services;

public sealed class PlaybackService(
    ILmsPlayerClient lmsPlayerClient,
    ILmsPlaybackClient lmsPlaybackClient,
    IPlayerSelectorResolver playerSelectorResolver,
    IPlayableReferenceResolver referenceResolver,
    ISearchObservationStore observationStore,
    TimeProvider timeProvider,
    ILogger<PlaybackService> logger) : IPlaybackService
{
    public async Task<PlaybackOutcome> PlayAsync(
        string playerSelector,
        IReadOnlyList<string> references,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(playerSelector))
        {
            return new PlaybackRejected(
                PlaybackRejectionReason.InvalidPlayer,
                "The player must not be empty.");
        }

        if (references is null || references.Count == 0)
        {
            return new PlaybackRejected(
                PlaybackRejectionReason.EmptyItems,
                "At least one media reference is required.");
        }

        var decodedReferences = new PlayableReferenceValue[references.Count];
        for (var index = 0; index < references.Count; index++)
        {
            var value = referenceResolver.Resolve(references[index]);
            if (value is null)
            {
                return new PlaybackRejected(
                    PlaybackRejectionReason.InvalidReference,
                    $"Media item {index + 1} has an invalid reference.");
            }

            decodedReferences[index] = value;
        }

        var media = decodedReferences.Select(value => value.Media).ToArray();

        var playersTask = lmsPlayerClient.GetPlayersAsync(cancellationToken);
        var playableItemsTask = Task.WhenAll(media.Select(item =>
            lmsPlaybackClient.GetPlayableItemCountAsync(item, cancellationToken)));
        await Task.WhenAll(playersTask, playableItemsTask);

        var playerOutcome = playerSelectorResolver.Resolve(
            await playersTask,
            playerSelector);
        if (playerOutcome is PlayerSelectorRejected rejectedPlayer)
        {
            return new PlaybackRejected(
                MapRejectionReason(rejectedPlayer.Reason),
                rejectedPlayer.Message);
        }

        var player = ((PlayerSelectorResolved)playerOutcome).Player;

        var playableItems = await playableItemsTask;
        var missingItemIndex = Array.FindIndex(playableItems, count => count == 0);
        if (missingItemIndex >= 0)
        {
            return new PlaybackRejected(
                PlaybackRejectionReason.MediaNotFound,
                $"Media item {missingItemIndex + 1} no longer resolves to playable media.");
        }

        if (!player.PoweredOn)
        {
            await lmsPlaybackClient.PowerOnAsync(player.Id, cancellationToken);
        }

        await lmsPlaybackClient.LoadAsync(
            player.Id,
            media[0],
            cancellationToken);
        foreach (var item in media.Skip(1))
        {
            await lmsPlaybackClient.AddAsync(
                player.Id,
                item,
                cancellationToken);
        }

        var updatedPlayers = await lmsPlayerClient.GetPlayersAsync(cancellationToken);
        await TryMarkSelectedAsync(
            decodedReferences
                .Select(value => value.SearchCorrelationId)
                .OfType<string>()
                .ToArray(),
            cancellationToken);
        return new PlaybackSucceeded(FindUpdatedPlayer(updatedPlayers, player.Id));
    }

    private async Task TryMarkSelectedAsync(
        IReadOnlyCollection<string> correlationIds,
        CancellationToken cancellationToken)
    {
        if (correlationIds.Count == 0)
        {
            return;
        }

        try
        {
            await observationStore.MarkSelectedAsync(
                correlationIds,
                timeProvider.GetUtcNow(),
                cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(
                exception,
                "Could not mark search-result correlations as selected after successful playback.");
        }
    }

    private static PlaybackRejectionReason MapRejectionReason(
        PlayerSelectorRejectionReason reason) => reason switch
        {
            PlayerSelectorRejectionReason.InvalidSelector => PlaybackRejectionReason.InvalidPlayer,
            PlayerSelectorRejectionReason.PlayerNotFound => PlaybackRejectionReason.PlayerNotFound,
            PlayerSelectorRejectionReason.AmbiguousPlayer => PlaybackRejectionReason.AmbiguousPlayer,
            _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, null)
        };

    private static LmsPlayerStatus FindUpdatedPlayer(
        IReadOnlyList<LmsPlayerStatus> players,
        string playerId) =>
        players.FirstOrDefault(player =>
            string.Equals(player.Id, playerId, StringComparison.OrdinalIgnoreCase))
        ?? throw new LmsRequestException(
            "The selected LMS player was no longer available after playback changed.");
}
