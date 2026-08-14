using LyrionVoiceMcp.Abstractions;

namespace LyrionVoiceMcp.Services;

public sealed class PlayerControlService(
    ILmsPlayerClient lmsPlayerClient,
    ILmsPlayerControlClient lmsPlayerControlClient) : IPlayerControlService
{
    public async Task<PlayerControlOutcome> ControlAsync(
        string playerId,
        PlayerControlCommand command,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(playerId))
        {
            return new PlayerControlRejected(
                PlayerControlRejectionReason.InvalidPlayer,
                "The player ID must not be empty.");
        }

        if (!Enum.IsDefined(command))
        {
            return new PlayerControlRejected(
                PlayerControlRejectionReason.InvalidAction,
                "The player control action is invalid.");
        }

        var players = await lmsPlayerClient.GetPlayersAsync(cancellationToken);
        var player = FindPlayer(players, playerId);
        if (player is null)
        {
            return new PlayerControlRejected(
                PlayerControlRejectionReason.PlayerNotFound,
                $"LMS player '{playerId}' was not found.");
        }

        await lmsPlayerControlClient.ControlAsync(
            player.Id,
            command,
            cancellationToken);

        var updatedPlayers = await lmsPlayerClient.GetPlayersAsync(cancellationToken);
        var updatedPlayer = FindPlayer(updatedPlayers, player.Id)
            ?? throw new LmsRequestException(
                "The selected LMS player was no longer available after it was controlled.");
        return new PlayerControlSucceeded(updatedPlayer);
    }

    private static LmsPlayerStatus? FindPlayer(
        IReadOnlyList<LmsPlayerStatus> players,
        string playerId) =>
        players.FirstOrDefault(player =>
            string.Equals(player.Id, playerId, StringComparison.OrdinalIgnoreCase));
}
