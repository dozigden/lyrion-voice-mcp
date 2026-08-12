using System.ComponentModel;
using LyrionVoiceMcp.Abstractions;
using LyrionVoiceMcp.Contracts;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using ContractPlayerPlaybackMode = LyrionVoiceMcp.Contracts.PlayerPlaybackMode;

namespace LyrionVoiceMcp.Api.Tools;

[McpServerToolType]
public sealed class PlayerTools(IPlayerStatusService playerStatusService)
{
    [McpServerTool(
        Name = "get_player_status",
        Title = "Get Lyrion player status",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true)]
    [Description("Discover every player known to the configured Lyrion Music Server and return its basic power and playback state.")]
    public async Task<GetPlayerStatusResponse> GetPlayerStatusAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            var players = await playerStatusService.GetPlayersAsync(cancellationToken);
            return new GetPlayerStatusResponse(players.Select(MapPlayer).ToArray());
        }
        catch (LmsRequestException exception)
        {
            throw new McpException(exception.Message);
        }
    }

    private static PlayerStatus MapPlayer(LmsPlayerStatus player) =>
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
            });
}
