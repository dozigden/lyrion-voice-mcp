using LyrionVoiceMcp.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace LyrionVoiceMcp.Services;

public sealed class QueueManagementService(
    ILmsPlayerClient lmsPlayerClient,
    ILmsPlaybackClient lmsPlaybackClient,
    ISearchResultReferenceCodec referenceCodec,
    ISearchObservationStore observationStore,
    TimeProvider timeProvider,
    ILogger<QueueManagementService> logger) : IQueueManagementService
{
    public QueueManagementService(
        ILmsPlayerClient lmsPlayerClient,
        ILmsPlaybackClient lmsPlaybackClient,
        ISearchResultReferenceCodec referenceCodec)
        : this(
            lmsPlayerClient,
            lmsPlaybackClient,
            referenceCodec,
            NullSearchObservationStore.Instance,
            TimeProvider.System,
            NullLogger<QueueManagementService>.Instance)
    {
    }

    public async Task<QueueManagementOutcome> ManageAsync(
        string playerId,
        QueueManagementCommand command,
        IReadOnlyList<string>? references,
        CancellationToken cancellationToken)
    {
        var validation = ValidateRequest(playerId, command, references);
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
            var player = FindPlayer(players, playerId);
            if (player is null)
            {
                return PlayerNotFound(playerId);
            }

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
                value.Identity,
                cancellationToken)));
        await Task.WhenAll(playersTask, itemCountsTask);

        var resolvedPlayer = FindPlayer(await playersTask, playerId);
        if (resolvedPlayer is null)
        {
            return PlayerNotFound(playerId);
        }

        var itemCounts = await itemCountsTask;
        var missingItemIndex = Array.FindIndex(itemCounts, count => count == 0);
        if (missingItemIndex >= 0)
        {
            return new QueueManagementRejected(
                QueueManagementRejectionReason.MediaNotFound,
                $"Search-result item {missingItemIndex + 1} no longer resolves to playable media.");
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
                    value.Identity,
                    cancellationToken);
            }
        }
        else
        {
            foreach (var value in values.Reverse())
            {
                await lmsPlaybackClient.InsertAsync(
                    resolvedPlayer.Id,
                    value.Identity,
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
            values.Select(value => value.CorrelationId).ToArray(),
            cancellationToken);
        return new QueueManagementSucceeded(
            resolvedPlayer.Id,
            updatedQueueCount);
    }

    private static QueueManagementRejected? ValidateRequest(
        string playerId,
        QueueManagementCommand command,
        IReadOnlyList<string>? references)
    {
        if (string.IsNullOrWhiteSpace(playerId))
        {
            return new QueueManagementRejected(
                QueueManagementRejectionReason.InvalidPlayer,
                "The player ID must not be empty.");
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
                "At least one search-result reference is required.")
            : null;
    }

    private DecodedReferences DecodeReferences(IReadOnlyList<string> references)
    {
        var values = new SearchResultReferenceValue[references.Count];
        for (var index = 0; index < references.Count; index++)
        {
            var value = referenceCodec.TryDecode(references[index]);
            if (value is null)
            {
                return new DecodedReferences(
                    [],
                    new QueueManagementRejected(
                        QueueManagementRejectionReason.InvalidReference,
                        $"Search-result item {index + 1} has an invalid reference."));
            }

            values[index] = value;
        }

        return new DecodedReferences(values, null);
    }

    private async Task TryMarkSelectedAsync(
        IReadOnlyCollection<string> correlationIds,
        CancellationToken cancellationToken)
    {
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

    private static LmsPlayerStatus? FindPlayer(
        IReadOnlyList<LmsPlayerStatus> players,
        string playerId) =>
        players.FirstOrDefault(player =>
            string.Equals(player.Id, playerId, StringComparison.OrdinalIgnoreCase));

    private static QueueManagementRejected PlayerNotFound(string playerId) =>
        new(
            QueueManagementRejectionReason.PlayerNotFound,
            $"LMS player '{playerId}' was not found.");

    private sealed record DecodedReferences(
        IReadOnlyList<SearchResultReferenceValue> Values,
        QueueManagementRejected? Rejection);
}
