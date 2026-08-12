using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using LyrionVoiceMcp.Abstractions;
using LyrionVoiceMcp.Contracts;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using ContractPlayerPlaybackMode = LyrionVoiceMcp.Contracts.PlayerPlaybackMode;

namespace LyrionVoiceMcp.Api.Tools;

[McpServerToolType]
public sealed class PlaybackTools(IPlaybackService playbackService)
{
    [McpServerTool(
        Name = "play",
        Title = "Play media on a Lyrion player",
        ReadOnly = false,
        Destructive = true,
        Idempotent = false,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(PlayResponse))]
    [Description("Play or queue one or more search results on an explicitly selected Lyrion player.")]
    public async Task<CallToolResult> PlayAsync(
        [Description("The raw LMS player ID returned by get_player_status.")] string player,
        [Description("One or more opaque result references returned by search, in playback order.")] IReadOnlyList<string> items,
        [Description("Replace the current queue or append to it.")] PlayQueueMode mode = PlayQueueMode.Replace,
        CancellationToken cancellationToken = default)
    {
        var playbackMode = MapMode(mode);
        if (playbackMode is null)
        {
            return ErrorResult("The playback queue mode is invalid.");
        }

        try
        {
            var outcome = await playbackService.PlayAsync(
                player,
                items,
                playbackMode.Value,
                cancellationToken);
            return outcome switch
            {
                PlaybackSucceeded succeeded =>
                    SuccessResult(new PlayResponse(MapPlayer(succeeded.Player))),
                PlaybackRejected rejected => ErrorResult(rejected.Message),
                _ => throw new UnreachableException(
                    $"Unsupported playback outcome {outcome.GetType().Name}.")
            };
        }
        catch (LmsRequestException exception)
        {
            return ErrorResult(exception.Message);
        }
    }

    private static PlaybackQueueMode? MapMode(PlayQueueMode mode) =>
        mode switch
        {
            PlayQueueMode.Replace => PlaybackQueueMode.Replace,
            PlayQueueMode.Append => PlaybackQueueMode.Append,
            _ => null
        };

    private static CallToolResult SuccessResult(PlayResponse response)
    {
        var structuredContent = JsonSerializer.SerializeToElement(
            response,
            McpJsonUtilities.DefaultOptions);
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
