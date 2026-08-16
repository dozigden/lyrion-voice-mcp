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
    [Description("Clear a player's queue, or add the usable subset of a media batch and report skipped items.")]
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
                    SuccessResult(MapResponse(
                        succeeded.PlayerId,
                        succeeded.QueueLength,
                        succeeded.RequestedItemCount,
                        succeeded.CompletedItemCount,
                        succeeded.SkippedItems,
                        succeeded.StateRefreshError)),
                QueueManagementFailed failed =>
                    ErrorResult(
                        MapResponse(
                            failed.PlayerId,
                            failed.QueueLength,
                            failed.RequestedItemCount,
                            0,
                            failed.SkippedItems,
                            failed.StateRefreshError),
                        failed.Message),
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

    private static ManageQueueResponse MapResponse(
        string playerId,
        int? queueLength,
        int requestedItemCount,
        int completedItemCount,
        IReadOnlyList<SkippedMediaItem> skippedItems,
        string? stateRefreshError) =>
        new(
            playerId,
            queueLength,
            requestedItemCount,
            completedItemCount,
            SkippedItemMapper.Map(skippedItems),
            stateRefreshError);

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

    private static CallToolResult ErrorResult(
        ManageQueueResponse response,
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
