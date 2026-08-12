using LyrionVoiceMcp.Abstractions;

namespace LyrionVoiceMcp.Services;

public sealed class PlayerStatusService(ILmsPlayerClient lmsPlayerClient)
    : IPlayerStatusService
{
    public Task<IReadOnlyList<LmsPlayerStatus>> GetPlayersAsync(
        CancellationToken cancellationToken) =>
        lmsPlayerClient.GetPlayersAsync(cancellationToken);
}
