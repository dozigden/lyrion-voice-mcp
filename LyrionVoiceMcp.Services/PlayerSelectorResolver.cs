using LyrionVoiceMcp.Abstractions;

namespace LyrionVoiceMcp.Services;

public sealed class PlayerSelectorResolver : IPlayerSelectorResolver
{
    public PlayerSelectorOutcome Resolve(
        IReadOnlyList<LmsPlayerStatus> players,
        string selector)
    {
        ArgumentNullException.ThrowIfNull(players);

        if (string.IsNullOrWhiteSpace(selector))
        {
            return new PlayerSelectorRejected(
                PlayerSelectorRejectionReason.InvalidSelector,
                "The player must not be empty.");
        }

        var trimmedSelector = selector.Trim();
        var idMatch = players.FirstOrDefault(player =>
            string.Equals(
                player.Id,
                trimmedSelector,
                StringComparison.OrdinalIgnoreCase));
        if (idMatch is not null)
        {
            return new PlayerSelectorResolved(idMatch);
        }

        var nameMatches = players
            .Where(player => string.Equals(
                player.Name,
                trimmedSelector,
                StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (nameMatches.Length == 1)
        {
            return new PlayerSelectorResolved(nameMatches[0]);
        }

        if (nameMatches.Length == 0)
        {
            return new PlayerSelectorRejected(
                PlayerSelectorRejectionReason.PlayerNotFound,
                $"LMS player '{trimmedSelector}' was not found. Call get_player_status to discover available players.");
        }

        var candidateIds = string.Join(
            ", ",
            nameMatches.Select(player => $"'{player.Id}'"));
        return new PlayerSelectorRejected(
            PlayerSelectorRejectionReason.AmbiguousPlayer,
            $"LMS player name '{trimmedSelector}' is ambiguous. Call get_player_status and retry with one of these player IDs: {candidateIds}.");
    }
}
