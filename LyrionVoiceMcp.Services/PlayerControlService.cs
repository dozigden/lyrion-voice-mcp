using LyrionVoiceMcp.Abstractions;

namespace LyrionVoiceMcp.Services;

public sealed class PlayerControlService(
    ILmsPlayerClient lmsPlayerClient,
    ILmsPlayerControlClient lmsPlayerControlClient,
    IPlayerSelectorResolver playerSelectorResolver) : IPlayerControlService
{
    public async Task<PlayerControlOutcome> ControlAsync(
        string playerSelector,
        PlayerControlCommand command,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(playerSelector))
        {
            return new PlayerControlRejected(
                PlayerControlRejectionReason.InvalidPlayer,
                "The player must not be empty.");
        }

        if (!Enum.IsDefined(command))
        {
            return new PlayerControlRejected(
                PlayerControlRejectionReason.InvalidAction,
                "The player control action is invalid.");
        }

        var players = await lmsPlayerClient.GetPlayersAsync(cancellationToken);
        var playerOutcome = playerSelectorResolver.Resolve(players, playerSelector);
        if (playerOutcome is PlayerSelectorRejected rejectedPlayer)
        {
            return new PlayerControlRejected(
                MapRejectionReason(rejectedPlayer.Reason),
                rejectedPlayer.Message);
        }

        var player = ((PlayerSelectorResolved)playerOutcome).Player;

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

    private static PlayerControlRejectionReason MapRejectionReason(
        PlayerSelectorRejectionReason reason) => reason switch
        {
            PlayerSelectorRejectionReason.InvalidSelector => PlayerControlRejectionReason.InvalidPlayer,
            PlayerSelectorRejectionReason.PlayerNotFound => PlayerControlRejectionReason.PlayerNotFound,
            PlayerSelectorRejectionReason.AmbiguousPlayer => PlayerControlRejectionReason.AmbiguousPlayer,
            _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, null)
        };
}
