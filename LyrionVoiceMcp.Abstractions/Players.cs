namespace LyrionVoiceMcp.Abstractions;

public enum PlayerPlaybackState
{
    Playing,
    Paused,
    Stopped,
    Unknown
}

public sealed record LmsPlayerStatus(
    string Id,
    string Name,
    bool PoweredOn,
    PlayerPlaybackState PlaybackState);

public interface ILmsPlayerClient
{
    Task<IReadOnlyList<LmsPlayerStatus>> GetPlayersAsync(
        CancellationToken cancellationToken);
}

public interface IPlayerStatusService
{
    Task<IReadOnlyList<LmsPlayerStatus>> GetPlayersAsync(
        CancellationToken cancellationToken);
}
