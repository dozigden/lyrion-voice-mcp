using System.ComponentModel;
using System.Diagnostics;
using LyrionVoiceMcp.Abstractions;
using LyrionVoiceMcp.Contracts;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

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
    [Description("Replace a Lyrion player's queue with one or more search or browse results and start playback.")]
    public async Task<CallToolResult> PlayAsync(
        [Description("The raw LMS player ID returned by get_player_status.")] string player,
        [Description("One or more opaque playable references returned by search or browse, in playback order.")] IReadOnlyList<string> items,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var outcome = await playbackService.PlayAsync(
                player,
                items,
                cancellationToken);
            return outcome switch
            {
                PlaybackSucceeded succeeded =>
                    SuccessResult(new PlayResponse(
                        PlayerStatusMapper.Map(succeeded.Player))),
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

    private static CallToolResult SuccessResult(PlayResponse response)
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
