using LyrionVoiceMcp.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace LyrionVoiceMcp.Services;

public sealed class PlaybackService(
    ILmsPlayerClient lmsPlayerClient,
    ILmsPlaybackClient lmsPlaybackClient,
    IPlayableReferenceResolver referenceResolver,
    ISearchObservationStore observationStore,
    TimeProvider timeProvider,
    ILogger<PlaybackService> logger) : IPlaybackService
{
    internal PlaybackService(
        ILmsPlayerClient lmsPlayerClient,
        ILmsPlaybackClient lmsPlaybackClient,
        ISearchResultReferenceCodec referenceCodec)
        : this(
            lmsPlayerClient,
            lmsPlaybackClient,
            new PlayableReferenceResolver(referenceCodec, new BrowseReferenceCodec()),
            NullSearchObservationStore.Instance,
            TimeProvider.System,
            NullLogger<PlaybackService>.Instance)
    {
    }

    public async Task<PlaybackOutcome> PlayAsync(
        string playerId,
        IReadOnlyList<string> references,
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

        var player = FindPlayer(await playersTask, playerId);
        if (player is null)
        {
            return new PlaybackRejected(
                PlaybackRejectionReason.PlayerNotFound,
                $"LMS player '{playerId}' was not found.");
        }

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
