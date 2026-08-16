using LyrionVoiceMcp.Abstractions;
using Microsoft.Extensions.Logging;

namespace LyrionVoiceMcp.Services;

public sealed class QueueManagementService(
    ILmsPlayerClient lmsPlayerClient,
    ILmsPlaybackClient lmsPlaybackClient,
    IPlayerSelectorResolver playerSelectorResolver,
    IPlayableReferenceResolver referenceResolver,
    ISearchObservationStore observationStore,
    TimeProvider timeProvider,
    ILogger<QueueManagementService> logger) : IQueueManagementService
{
    public async Task<QueueManagementOutcome> ManageAsync(
        string playerSelector,
        QueueManagementCommand command,
        IReadOnlyList<string>? references,
        CancellationToken cancellationToken)
    {
        var validation = ValidateRequest(playerSelector, command, references);
        if (validation is not null)
        {
            return validation;
        }

        if (command == QueueManagementCommand.Clear)
        {
            var players = await lmsPlayerClient.GetPlayersAsync(cancellationToken);
            var playerOutcome = playerSelectorResolver.Resolve(players, playerSelector);
            if (playerOutcome is PlayerSelectorRejected rejectedPlayer)
            {
                return PlayerRejected(rejectedPlayer);
            }

            var player = ((PlayerSelectorResolved)playerOutcome).Player;

            await lmsPlaybackClient.ClearAsync(player.Id, cancellationToken);
            var clearedQueueCount = await lmsPlaybackClient.GetQueueCountAsync(
                player.Id,
                cancellationToken);
            if (clearedQueueCount != 0)
            {
                throw new LmsRequestException(
                    "LMS queue was not empty after the clear command.");
            }

            return new QueueManagementSucceeded(
                player.Id,
                0,
                0,
                0,
                [],
                null);
        }

        var preparedReferences = PrepareReferences(references!);
        if (preparedReferences.Items.Count == 0)
        {
            return NoUsableItems(references!.Count, preparedReferences.SkippedItems);
        }

        var playersTask = lmsPlayerClient.GetPlayersAsync(cancellationToken);
        var itemCountsTask = Task.WhenAll(preparedReferences.Items.Select(item =>
            lmsPlaybackClient.GetPlayableItemCountAsync(
                item.Value.Media,
                cancellationToken)));
        await Task.WhenAll(playersTask, itemCountsTask);

        var playerResolution = playerSelectorResolver.Resolve(
            await playersTask,
            playerSelector);
        if (playerResolution is PlayerSelectorRejected rejectedResolution)
        {
            return PlayerRejected(rejectedResolution);
        }

        var resolvedPlayer = ((PlayerSelectorResolved)playerResolution).Player;

        var skippedItems = preparedReferences.SkippedItems.ToList();
        var itemCounts = await itemCountsTask;
        var availableItems = new List<PlayableItem>(preparedReferences.Items.Count);
        for (var index = 0; index < preparedReferences.Items.Count; index++)
        {
            var item = preparedReferences.Items[index];
            if (itemCounts[index] == 0)
            {
                skippedItems.Add(SkippedItem(
                    item.Index,
                    MediaItemSkipReason.MediaUnavailable,
                    "The media is no longer available."));
                continue;
            }

            availableItems.Add(new PlayableItem(
                item.Index,
                item.Value,
                itemCounts[index]));
        }

        if (availableItems.Count == 0)
        {
            return NoUsableItems(references!.Count, skippedItems);
        }

        var queueCount = await lmsPlaybackClient.GetQueueCountAsync(
            resolvedPlayer.Id,
            cancellationToken);
        if (queueCount > QueueLimits.MaximumItems)
        {
            return new QueueManagementRejected(
                QueueManagementRejectionReason.QueueLimitExceeded,
                $"The LMS queue already exceeds the supported {QueueLimits.MaximumItems}-item limit.");
        }

        var remainingCapacity = QueueLimits.MaximumItems - queueCount;
        var plannedItems = new List<PlayableItem>(availableItems.Count);
        foreach (var item in availableItems)
        {
            if (item.PlayableTrackCount > remainingCapacity)
            {
                skippedItems.Add(SkippedItem(
                    item.Index,
                    MediaItemSkipReason.QueueCapacity,
                    $"The item needs {item.PlayableTrackCount} queue places but only {remainingCapacity} remain."));
                continue;
            }

            plannedItems.Add(item);
            remainingCapacity -= item.PlayableTrackCount;
        }

        if (plannedItems.Count == 0)
        {
            return NoUsableItems(references!.Count, skippedItems);
        }

        var submissionItems = (command == QueueManagementCommand.Append
            ? plannedItems
            : plannedItems.AsEnumerable().Reverse()).ToArray();
        var completedItems = new List<PlayableItem>(plannedItems.Count);
        for (var index = 0; index < submissionItems.Length; index++)
        {
            var item = submissionItems[index];
            try
            {
                if (command == QueueManagementCommand.Append)
                {
                    await lmsPlaybackClient.AddAsync(
                        resolvedPlayer.Id,
                        item.Value.Media,
                        cancellationToken);
                }
                else
                {
                    await lmsPlaybackClient.InsertAsync(
                        resolvedPlayer.Id,
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
                foreach (var remainingItem in submissionItems.Skip(index + 1))
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
            var failedRefresh = await TryRefreshQueueAsync(
                resolvedPlayer.Id,
                cancellationToken);
            return new QueueManagementFailed(
                resolvedPlayer.Id,
                failedRefresh.QueueLength,
                references!.Count,
                skippedItems.OrderBy(item => item.Index).ToArray(),
                failedRefresh.Error,
                BuildFailureMessage(
                    "Queue management failed before any media item completed.",
                    skippedItems));
        }

        var refresh = await TryRefreshQueueAsync(
            resolvedPlayer.Id,
            cancellationToken);
        await TryMarkSelectedAsync(
            completedItems
                .Select(item => item.Value.SearchCorrelationId)
                .OfType<string>()
                .ToArray(),
            cancellationToken);
        return new QueueManagementSucceeded(
            resolvedPlayer.Id,
            refresh.QueueLength,
            references!.Count,
            completedItems.Count,
            skippedItems.OrderBy(item => item.Index).ToArray(),
            refresh.Error);
    }

    private static QueueManagementRejected? ValidateRequest(
        string playerSelector,
        QueueManagementCommand command,
        IReadOnlyList<string>? references)
    {
        if (string.IsNullOrWhiteSpace(playerSelector))
        {
            return new QueueManagementRejected(
                QueueManagementRejectionReason.InvalidPlayer,
                "The player must not be empty.");
        }

        if (!Enum.IsDefined(command))
        {
            return new QueueManagementRejected(
                QueueManagementRejectionReason.InvalidAction,
                "The queue management action is invalid.");
        }

        if (command == QueueManagementCommand.Clear)
        {
            return references is { Count: > 0 }
                ? new QueueManagementRejected(
                    QueueManagementRejectionReason.ItemsNotAllowed,
                    "The clear action does not accept items.")
                : null;
        }

        return references is null or { Count: 0 }
            ? new QueueManagementRejected(
                QueueManagementRejectionReason.EmptyItems,
                "At least one media reference is required.")
            : null;
    }

    private PreparedReferences PrepareReferences(IReadOnlyList<string> references)
    {
        var items = new List<PreparedItem>(references.Count);
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

            items.Add(new PreparedItem(index, value));
        }

        return new PreparedReferences(items, skippedItems);
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
                "Could not mark search-result correlations as selected after successful queue management.");
        }
    }

    private static QueueManagementRejected PlayerRejected(
        PlayerSelectorRejected rejection) => new(
            rejection.Reason switch
            {
                PlayerSelectorRejectionReason.InvalidSelector => QueueManagementRejectionReason.InvalidPlayer,
                PlayerSelectorRejectionReason.PlayerNotFound => QueueManagementRejectionReason.PlayerNotFound,
                PlayerSelectorRejectionReason.AmbiguousPlayer => QueueManagementRejectionReason.AmbiguousPlayer,
                _ => throw new ArgumentOutOfRangeException(
                    nameof(rejection),
                    rejection.Reason,
                    null)
            },
            rejection.Message);

    private async Task<QueueRefresh> TryRefreshQueueAsync(
        string playerId,
        CancellationToken cancellationToken)
    {
        try
        {
            var queueLength = await lmsPlaybackClient.GetQueueCountAsync(
                playerId,
                cancellationToken);
            return new QueueRefresh(queueLength, null);
        }
        catch (LmsRequestException exception)
        {
            return new QueueRefresh(null, exception.Message);
        }
    }

    private static QueueManagementRejected NoUsableItems(
        int requestedItemCount,
        IReadOnlyCollection<SkippedMediaItem> skippedItems) =>
        new(
            QueueManagementRejectionReason.NoUsableItems,
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

    private sealed record PreparedReferences(
        IReadOnlyList<PreparedItem> Items,
        IReadOnlyList<SkippedMediaItem> SkippedItems);

    private sealed record PreparedItem(
        int Index,
        PlayableReferenceValue Value);

    private sealed record PlayableItem(
        int Index,
        PlayableReferenceValue Value,
        int PlayableTrackCount);

    private sealed record QueueRefresh(
        int? QueueLength,
        string? Error);
}
