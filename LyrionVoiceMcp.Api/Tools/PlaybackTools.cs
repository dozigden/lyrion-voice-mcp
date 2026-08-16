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
    [Description("Replace a Lyrion player's queue with usable search or browse results, report skipped items, and start playback.")]
    public async Task<CallToolResult> PlayAsync(
        [Description("A raw LMS player ID or exact unique player name returned by get_player_status.")] string player,
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
                    SuccessResult(MapResponse(
                        succeeded.Player,
                        succeeded.RequestedItemCount,
                        succeeded.CompletedItemCount,
                        succeeded.SkippedItems,
                        succeeded.StateRefreshError)),
                PlaybackFailed failed =>
                    ErrorResult(
                        MapResponse(
                            failed.Player,
                            failed.RequestedItemCount,
                            0,
                            failed.SkippedItems,
                            failed.StateRefreshError),
                        failed.Message),
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

    private static PlayResponse MapResponse(
        LmsPlayerStatus? player,
        int requestedItemCount,
        int completedItemCount,
        IReadOnlyList<SkippedMediaItem> skippedItems,
        string? stateRefreshError) =>
        new(
            player is null ? null : PlayerStatusMapper.Map(player),
            requestedItemCount,
            completedItemCount,
            SkippedItemMapper.Map(skippedItems),
            stateRefreshError);

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

    private static CallToolResult ErrorResult(
        PlayResponse response,
        string message)
    {
        var structuredContent = McpToolJson.Serialize(response);
        return new CallToolResult
        {
            Content = [new TextContentBlock { Text = message }],
            StructuredContent = structuredContent,
            IsError = true
        };
    }
}
