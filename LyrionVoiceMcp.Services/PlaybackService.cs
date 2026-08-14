using LyrionVoiceMcp.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace LyrionVoiceMcp.Services;

public sealed class PlaybackService(
    ILmsPlayerClient lmsPlayerClient,
    ILmsPlaybackClient lmsPlaybackClient,
    ISearchResultReferenceCodec referenceCodec,
    ISearchObservationStore observationStore,
    TimeProvider timeProvider,
    ILogger<PlaybackService> logger) : IPlaybackService
{
    public PlaybackService(
        ILmsPlayerClient lmsPlayerClient,
        ILmsPlaybackClient lmsPlaybackClient,
        ISearchResultReferenceCodec referenceCodec)
        : this(
            lmsPlayerClient,
            lmsPlaybackClient,
            referenceCodec,
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
                "At least one search-result reference is required.");
        }

        var decodedReferences = new SearchResultReferenceValue[references.Count];
        for (var index = 0; index < references.Count; index++)
        {
            var value = referenceCodec.TryDecode(references[index]);
            if (value is null)
            {
                return new PlaybackRejected(
                    PlaybackRejectionReason.InvalidReference,
                    $"Search-result item {index + 1} has an invalid reference.");
            }

            decodedReferences[index] = value;
        }

        var identities = decodedReferences.Select(value => value.Identity).ToArray();

        var playersTask = lmsPlayerClient.GetPlayersAsync(cancellationToken);
        var playableItemsTask = Task.WhenAll(identities.Select(identity =>
            lmsPlaybackClient.GetPlayableItemCountAsync(identity, cancellationToken)));
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
                $"Search-result item {missingItemIndex + 1} no longer resolves to playable media.");
        }

        if (!player.PoweredOn)
        {
            await lmsPlaybackClient.PowerOnAsync(player.Id, cancellationToken);
        }

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

        var updatedPlayers = await lmsPlayerClient.GetPlayersAsync(cancellationToken);
        await TryMarkSelectedAsync(
            decodedReferences.Select(value => value.CorrelationId).ToArray(),
            cancellationToken);
        return new PlaybackSucceeded(FindUpdatedPlayer(updatedPlayers, player.Id));
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
