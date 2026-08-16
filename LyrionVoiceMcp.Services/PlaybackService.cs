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

        var preparedItems = new List<PreparedItem>(references.Count);
        var skippedItems = new List<SkippedMediaItem>();
        for (var index = 0; index < references.Count; index++)
        {
            var value = referenceResolver.Resolve(references[index]);
            if (value is null)
            {
                skippedItems.Add(SkippedItem(
                    index,
                    MediaItemSkipReason.InvalidReference,
                    "The reference is invalid or has expired."));
                continue;
            }

            preparedItems.Add(new PreparedItem(index, value));
        }

        if (preparedItems.Count == 0)
        {
            return NoUsableItems(references.Count, skippedItems);
        }

        var playersTask = lmsPlayerClient.GetPlayersAsync(cancellationToken);
        var playableItemsTask = Task.WhenAll(preparedItems.Select(item =>
            lmsPlaybackClient.GetPlayableItemCountAsync(
                item.Value.Media,
                cancellationToken)));
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

        var playableCounts = await playableItemsTask;
        var playableItems = new List<PreparedItem>(preparedItems.Count);
        for (var index = 0; index < preparedItems.Count; index++)
        {
            var item = preparedItems[index];
            if (playableCounts[index] == 0)
            {
                skippedItems.Add(SkippedItem(
                    item.Index,
                    MediaItemSkipReason.MediaUnavailable,
                    "The media is no longer available."));
                continue;
            }

            playableItems.Add(item);
        }

        if (playableItems.Count == 0)
        {
            return NoUsableItems(references.Count, skippedItems);
        }

        if (!player.PoweredOn)
        {
            try
            {
                await lmsPlaybackClient.PowerOnAsync(player.Id, cancellationToken);
            }
            catch (LmsRequestException exception)
            {
                foreach (var item in playableItems)
                {
                    skippedItems.Add(SkippedItem(
                        item.Index,
                        MediaItemSkipReason.NotAttempted,
                        "Not attempted because the player could not be powered on."));
                }

                return await FailedOutcomeAsync(
                    player.Id,
                    references.Count,
                    skippedItems,
                    $"Playback did not start because LMS could not confirm that the player was powered on: {exception.Message}",
                    cancellationToken);
            }
        }

        var completedItems = new List<PreparedItem>(playableItems.Count);
        for (var index = 0; index < playableItems.Count; index++)
        {
            var item = playableItems[index];
            try
            {
                if (index == 0)
                {
                    await lmsPlaybackClient.LoadAsync(
                        player.Id,
                        item.Value.Media,
                        cancellationToken);
                }
                else
                {
                    await lmsPlaybackClient.AddAsync(
                        player.Id,
                        item.Value.Media,
                        cancellationToken);
                }

                completedItems.Add(item);
            }
            catch (LmsRequestException exception)
            {
                skippedItems.Add(SkippedItem(
                    item.Index,
                    MediaItemSkipReason.LmsError,
                    exception.Message));
                foreach (var remainingItem in playableItems.Skip(index + 1))
                {
                    skippedItems.Add(SkippedItem(
                        remainingItem.Index,
                        MediaItemSkipReason.NotAttempted,
                        "Not attempted after an earlier LMS failure."));
                }

                break;
            }
        }

        if (completedItems.Count == 0)
        {
            return await FailedOutcomeAsync(
                player.Id,
                references.Count,
                skippedItems,
                BuildFailureMessage(
                    "Playback failed before any media item completed.",
                    skippedItems),
                cancellationToken);
        }

        var refresh = await TryRefreshPlayerAsync(player.Id, cancellationToken);
        await TryMarkSelectedAsync(
            completedItems
                .Select(item => item.Value.SearchCorrelationId)
                .OfType<string>()
                .ToArray(),
            cancellationToken);
        return new PlaybackSucceeded(
            refresh.Player,
            references.Count,
            completedItems.Count,
            skippedItems.OrderBy(item => item.Index).ToArray(),
            refresh.Error);
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

    private async Task<PlayerRefresh> TryRefreshPlayerAsync(
        string playerId,
        CancellationToken cancellationToken)
    {
        try
        {
            var players = await lmsPlayerClient.GetPlayersAsync(cancellationToken);
            var player = players.FirstOrDefault(candidate =>
                string.Equals(
                    candidate.Id,
                    playerId,
                    StringComparison.OrdinalIgnoreCase));
            return player is null
                ? new PlayerRefresh(
                    null,
                    "The selected LMS player was no longer available after playback changed.")
                : new PlayerRefresh(player, null);
        }
        catch (LmsRequestException exception)
        {
            return new PlayerRefresh(null, exception.Message);
        }
    }

    private async Task<PlaybackFailed> FailedOutcomeAsync(
        string playerId,
        int requestedItemCount,
        IReadOnlyCollection<SkippedMediaItem> skippedItems,
        string message,
        CancellationToken cancellationToken)
    {
        var refresh = await TryRefreshPlayerAsync(playerId, cancellationToken);
        return new PlaybackFailed(
            refresh.Player,
            requestedItemCount,
            skippedItems.OrderBy(item => item.Index).ToArray(),
            refresh.Error,
            message);
    }

    private static PlaybackRejected NoUsableItems(
        int requestedItemCount,
        IReadOnlyCollection<SkippedMediaItem> skippedItems) =>
        new(
            PlaybackRejectionReason.NoUsableItems,
            BuildFailureMessage(
                $"None of the {requestedItemCount} requested media items was usable.",
                skippedItems));

    private static SkippedMediaItem SkippedItem(
        int zeroBasedIndex,
        MediaItemSkipReason reason,
        string message) =>
        new(zeroBasedIndex + 1, reason, message);

    private static string BuildFailureMessage(
        string summary,
        IEnumerable<SkippedMediaItem> skippedItems)
    {
        var details = string.Join(
            " ",
            skippedItems
                .OrderBy(item => item.Index)
                .Select(item =>
                    $"Item {item.Index}: {item.Reason.ToStableName()} ({item.Message})"));
        return string.IsNullOrEmpty(details)
            ? summary
            : $"{summary} {details}";
    }

    private sealed record PreparedItem(
        int Index,
        PlayableReferenceValue Value);

    private sealed record PlayerRefresh(
        LmsPlayerStatus? Player,
        string? Error);
}
