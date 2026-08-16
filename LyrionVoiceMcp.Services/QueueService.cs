using LyrionVoiceMcp.Abstractions;

namespace LyrionVoiceMcp.Services;

public sealed class QueueService(
    ILmsPlayerClient lmsPlayerClient,
    ILmsQueueClient lmsQueueClient,
    IPlayerSelectorResolver playerSelectorResolver) : IQueueService
{
    public async Task<QueueOutcome> GetQueueAsync(
        string playerSelector,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(playerSelector))
        {
            return new QueueRejected(
                QueueRejectionReason.InvalidPlayer,
                "The player must not be empty.");
        }

        var players = await lmsPlayerClient.GetPlayersAsync(cancellationToken);
        var playerOutcome = playerSelectorResolver.Resolve(players, playerSelector);
        if (playerOutcome is PlayerSelectorRejected rejectedPlayer)
        {
            return new QueueRejected(
                MapRejectionReason(rejectedPlayer.Reason),
                rejectedPlayer.Message);
        }

        var player = ((PlayerSelectorResolved)playerOutcome).Player;

        var queue = await lmsQueueClient.GetQueueAsync(
            player.Id,
            cancellationToken);
        return new QueueSucceeded(queue);
    }

    private static QueueRejectionReason MapRejectionReason(
        PlayerSelectorRejectionReason reason) => reason switch
        {
            PlayerSelectorRejectionReason.InvalidSelector => QueueRejectionReason.InvalidPlayer,
            PlayerSelectorRejectionReason.PlayerNotFound => QueueRejectionReason.PlayerNotFound,
            PlayerSelectorRejectionReason.AmbiguousPlayer => QueueRejectionReason.AmbiguousPlayer,
            _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, null)
        };
}
