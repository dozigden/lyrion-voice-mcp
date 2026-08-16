using System.ComponentModel;
using System.Diagnostics;
using LyrionVoiceMcp.Abstractions;
using LyrionVoiceMcp.Contracts;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace LyrionVoiceMcp.Api.Tools;

[McpServerToolType]
public sealed class QueueManagementTools(
    IQueueManagementService queueManagementService)
{
    [McpServerTool(
        Name = "manage_queue",
        Title = "Manage a Lyrion player's queue",
        ReadOnly = false,
        Destructive = true,
        Idempotent = false,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(ManageQueueResponse))]
    [Description("Clear a player's queue, append media, or insert media to play next.")]
    public async Task<CallToolResult> ManageAsync(
        [Description("A raw LMS player ID or exact unique player name returned by get_player_status.")] string player,
        [Description("Clear, append, or insert items to play next.")] ManageQueueAction action,
        [Description("Opaque playable references returned by search or browse; required for append and insert_next.")] IReadOnlyList<string>? items = null,
        CancellationToken cancellationToken = default)
    {
        var command = MapAction(action);
        if (command is null)
        {
            return ErrorResult("The queue management action is invalid.");
        }

        try
        {
            var outcome = await queueManagementService.ManageAsync(
                player,
                command.Value,
                items,
                cancellationToken);
            return outcome switch
            {
                QueueManagementSucceeded succeeded =>
                    SuccessResult(new ManageQueueResponse(
                        succeeded.PlayerId,
                        succeeded.QueueLength)),
                QueueManagementRejected rejected => ErrorResult(rejected.Message),
                _ => throw new UnreachableException(
                    $"Unsupported queue-management outcome {outcome.GetType().Name}.")
            };
        }
        catch (LmsRequestException exception)
        {
            return ErrorResult(exception.Message);
        }
    }

    private static QueueManagementCommand? MapAction(ManageQueueAction action) =>
        action switch
        {
            ManageQueueAction.Clear => QueueManagementCommand.Clear,
            ManageQueueAction.Append => QueueManagementCommand.Append,
            ManageQueueAction.InsertNext => QueueManagementCommand.InsertNext,
            _ => null
        };

    private static CallToolResult SuccessResult(ManageQueueResponse response)
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
