using System.ComponentModel;
using System.Diagnostics;
using LyrionVoiceMcp.Abstractions;
using LyrionVoiceMcp.Contracts;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace LyrionVoiceMcp.Api.Tools;

[McpServerToolType]
public sealed class PlayerControlTools(IPlayerControlService playerControlService)
{
    [McpServerTool(
        Name = "control_player",
        Title = "Control a Lyrion player",
        ReadOnly = false,
        Destructive = false,
        Idempotent = false,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(ControlPlayerResponse))]
    [Description("Control playback or power state on an explicitly selected Lyrion player.")]
    public async Task<CallToolResult> ControlAsync(
        [Description("A raw LMS player ID or exact unique player name returned by get_player_status.")] string player,
        [Description("The playback or power action to perform.")] PlayerControlAction action,
        CancellationToken cancellationToken = default)
    {
        var command = MapAction(action);
        if (command is null)
        {
            return ErrorResult("The player control action is invalid.");
        }

        try
        {
            var outcome = await playerControlService.ControlAsync(
                player,
                command.Value,
                cancellationToken);
            return outcome switch
            {
                PlayerControlSucceeded succeeded =>
                    SuccessResult(new ControlPlayerResponse(
                        PlayerStatusMapper.Map(succeeded.Player))),
                PlayerControlRejected rejected => ErrorResult(rejected.Message),
                _ => throw new UnreachableException(
                    $"Unsupported player-control outcome {outcome.GetType().Name}.")
            };
        }
        catch (LmsRequestException exception)
        {
            return ErrorResult(exception.Message);
        }
    }

    private static PlayerControlCommand? MapAction(PlayerControlAction action) =>
        action switch
        {
            PlayerControlAction.Resume => PlayerControlCommand.Resume,
            PlayerControlAction.Pause => PlayerControlCommand.Pause,
            PlayerControlAction.Stop => PlayerControlCommand.Stop,
            PlayerControlAction.Next => PlayerControlCommand.Next,
            PlayerControlAction.Previous => PlayerControlCommand.Previous,
            PlayerControlAction.PowerOn => PlayerControlCommand.PowerOn,
            PlayerControlAction.PowerOff => PlayerControlCommand.PowerOff,
            _ => null
        };

    private static CallToolResult SuccessResult(ControlPlayerResponse response)
    {
        var structuredContent = McpToolJson.Serialize(response);
        return new CallToolResult
        {
            Content = [new TextContentBlock { Text = structuredContent.GetRawText() }],
            StructuredContent = structuredContent
        };
    }

    private static CallToolResult ErrorResult(string message) =>
        new()
        {
            Content = [new TextContentBlock { Text = message }],
            IsError = true
        };
}
