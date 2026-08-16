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

        var decodedReferences = references is null
            ? new DecodedReferences([], null)
            : DecodeReferences(references);
        if (decodedReferences.Rejection is not null)
        {
            return decodedReferences.Rejection;
        }

        var playersTask = lmsPlayerClient.GetPlayersAsync(cancellationToken);
        if (command == QueueManagementCommand.Clear)
        {
            var players = await playersTask;
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

            return new QueueManagementSucceeded(player.Id, 0);
        }

        var values = decodedReferences.Values;
        var itemCountsTask = Task.WhenAll(values.Select(value =>
            lmsPlaybackClient.GetPlayableItemCountAsync(
                value.Media,
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

        var itemCounts = await itemCountsTask;
        var missingItemIndex = Array.FindIndex(itemCounts, count => count == 0);
        if (missingItemIndex >= 0)
        {
            return new QueueManagementRejected(
                QueueManagementRejectionReason.MediaNotFound,
                $"Media item {missingItemIndex + 1} no longer resolves to playable media.");
        }

        var queueCount = await lmsPlaybackClient.GetQueueCountAsync(
            resolvedPlayer.Id,
            cancellationToken);
        var requestedCount = itemCounts.Aggregate(0L, (total, count) => total + count);
        if (queueCount > QueueLimits.MaximumItems
            || requestedCount > QueueLimits.MaximumItems - queueCount)
        {
            return new QueueManagementRejected(
                QueueManagementRejectionReason.QueueLimitExceeded,
                $"The requested items would exceed the supported {QueueLimits.MaximumItems}-item queue limit.");
        }

        if (command == QueueManagementCommand.Append)
        {
            foreach (var value in values)
            {
                await lmsPlaybackClient.AddAsync(
                    resolvedPlayer.Id,
                    value.Media,
                    cancellationToken);
            }
        }
        else
        {
            foreach (var value in values.Reverse())
            {
                await lmsPlaybackClient.InsertAsync(
                    resolvedPlayer.Id,
                    value.Media,
                    cancellationToken);
            }
        }

        var updatedQueueCount = await lmsPlaybackClient.GetQueueCountAsync(
            resolvedPlayer.Id,
            cancellationToken);
        if (updatedQueueCount > QueueLimits.MaximumItems)
        {
            throw new LmsRequestException(
                $"LMS queue exceeds the supported {QueueLimits.MaximumItems}-item limit after queue management.");
        }

        await TryMarkSelectedAsync(
            values
                .Select(value => value.SearchCorrelationId)
                .OfType<string>()
                .ToArray(),
            cancellationToken);
        return new QueueManagementSucceeded(
            resolvedPlayer.Id,
            updatedQueueCount);
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

    private DecodedReferences DecodeReferences(IReadOnlyList<string> references)
    {
        var values = new PlayableReferenceValue[references.Count];
        for (var index = 0; index < references.Count; index++)
        {
            var value = referenceResolver.Resolve(references[index]);
            if (value is null)
            {
                return new DecodedReferences(
                    [],
                    new QueueManagementRejected(
                        QueueManagementRejectionReason.InvalidReference,
                        $"Media item {index + 1} has an invalid reference."));
            }

            values[index] = value;
        }

        return new DecodedReferences(values, null);
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

    private sealed record DecodedReferences(
        IReadOnlyList<PlayableReferenceValue> Values,
        QueueManagementRejected? Rejection);
}
