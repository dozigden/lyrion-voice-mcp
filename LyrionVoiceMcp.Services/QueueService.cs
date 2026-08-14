using LyrionVoiceMcp.Abstractions;

namespace LyrionVoiceMcp.Services;

public sealed class QueueService(
    ILmsPlayerClient lmsPlayerClient,
    ILmsQueueClient lmsQueueClient) : IQueueService
{
    public async Task<QueueOutcome> GetQueueAsync(
        string playerId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(playerId))
        {
            return new QueueRejected(
                QueueRejectionReason.InvalidPlayer,
                "The player ID must not be empty.");
        }

        var players = await lmsPlayerClient.GetPlayersAsync(cancellationToken);
        var player = players.FirstOrDefault(candidate =>
            string.Equals(candidate.Id, playerId, StringComparison.OrdinalIgnoreCase));
        if (player is null)
        {
            return new QueueRejected(
                QueueRejectionReason.PlayerNotFound,
                $"LMS player '{playerId}' was not found.");
        }

        var queue = await lmsQueueClient.GetQueueAsync(
            player.Id,
            cancellationToken);
        return new QueueSucceeded(queue);
    }
}
