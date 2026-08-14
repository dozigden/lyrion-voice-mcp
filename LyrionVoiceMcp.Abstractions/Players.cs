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
    PlayerPlaybackState PlaybackState,
    int? Volume = null,
    bool? Muted = null,
    LmsNowPlaying? NowPlaying = null);

public sealed record LmsNowPlaying(
    string Title,
    string? Artist,
    string? Album,
    double? DurationSeconds,
    double? ElapsedSeconds);

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
