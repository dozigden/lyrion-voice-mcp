using LyrionVoiceMcp.Abstractions;
using LyrionVoiceMcp.Contracts;
using ContractPlayerPlaybackMode = LyrionVoiceMcp.Contracts.PlayerPlaybackMode;

namespace LyrionVoiceMcp.Api.Tools;

internal static class PlayerStatusMapper
{
    public static PlayerStatus Map(LmsPlayerStatus player) =>
        new(
            player.Id,
            player.Name,
            player.PoweredOn,
            player.PlaybackState switch
            {
                PlayerPlaybackState.Playing => ContractPlayerPlaybackMode.Playing,
                PlayerPlaybackState.Paused => ContractPlayerPlaybackMode.Paused,
                PlayerPlaybackState.Stopped => ContractPlayerPlaybackMode.Stopped,
                PlayerPlaybackState.Unknown => ContractPlayerPlaybackMode.Unknown,
                _ => ContractPlayerPlaybackMode.Unknown
            },
            player.Volume,
            player.Muted,
            MapNowPlaying(player.NowPlaying));

    private static NowPlaying? MapNowPlaying(LmsNowPlaying? item) =>
        item is null
            ? null
            : new NowPlaying(
                item.Title,
                item.Artist,
                item.Album,
                item.DurationSeconds,
                item.ElapsedSeconds);
}
