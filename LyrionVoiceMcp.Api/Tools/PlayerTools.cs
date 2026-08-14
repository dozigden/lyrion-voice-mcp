using System.ComponentModel;
using LyrionVoiceMcp.Abstractions;
using LyrionVoiceMcp.Contracts;
using ModelContextProtocol;
using ModelContextProtocol.Server;

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
    [Description("Discover every player known to the configured Lyrion Music Server and return its full voice-relevant power, playback, volume, mute, and now-playing state.")]
    public async Task<GetPlayerStatusResponse> GetPlayerStatusAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            var players = await playerStatusService.GetPlayersAsync(cancellationToken);
            return new GetPlayerStatusResponse(
                players.Select(PlayerStatusMapper.Map).ToArray());
        }
        catch (LmsRequestException exception)
        {
            throw new McpException(exception.Message);
        }
    }

}
