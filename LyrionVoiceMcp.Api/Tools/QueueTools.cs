using System.ComponentModel;
using System.Diagnostics;
using LyrionVoiceMcp.Abstractions;
using LyrionVoiceMcp.Contracts;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace LyrionVoiceMcp.Api.Tools;

[McpServerToolType]
public sealed class QueueTools(IQueueService queueService)
{
    [McpServerTool(
        Name = "get_queue",
        Title = "Get a Lyrion player's queue",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(GetQueueResponse))]
    [Description("Get the complete current queue for an explicitly selected Lyrion player.")]
    public async Task<CallToolResult> GetQueueAsync(
        [Description("The raw LMS player ID returned by get_player_status.")] string player,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var outcome = await queueService.GetQueueAsync(player, cancellationToken);
            return outcome switch
            {
                QueueSucceeded succeeded => SuccessResult(Map(succeeded.Queue)),
                QueueRejected rejected => ErrorResult(rejected.Message),
                _ => throw new UnreachableException(
                    $"Unsupported queue outcome {outcome.GetType().Name}.")
            };
        }
        catch (LmsRequestException exception)
        {
            return ErrorResult(exception.Message);
        }
    }

    private static GetQueueResponse Map(LmsPlayerQueue queue) =>
        new(
            queue.PlayerId,
            queue.CurrentIndex,
            queue.Items.Select(item => new QueueItem(
                item.Index,
                item.Title,
                item.Artist,
                item.Album,
                item.DurationSeconds)).ToArray());

    private static CallToolResult SuccessResult(GetQueueResponse response)
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
