using System.Text.Json;
using System.Text.Json.Nodes;
using LyrionVoiceMcp.Abstractions;
using LyrionVoiceMcp.Contracts;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace LyrionVoiceMcp.Api.Tools;

public static class QueueManagementToolRegistration
{
    public static McpServerTool Create()
    {
        var serialiserOptions = new JsonSerializerOptions(McpToolJson.Options);
        for (var index = serialiserOptions.Converters.Count - 1; index >= 0; index--)
        {
            if (serialiserOptions.Converters[index]
                .CanConvert(typeof(ManageQueueAction)))
            {
                serialiserOptions.Converters.RemoveAt(index);
            }
        }

        var method = typeof(QueueManagementTools)
            .GetMethod(nameof(QueueManagementTools.ManageAsync))
            ?? throw new InvalidOperationException(
                "The manage_queue MCP tool method could not be found.");
        var tool = McpServerTool.Create(
            method,
            request =>
            {
                var services = request.Services
                    ?? throw new InvalidOperationException(
                        "The manage_queue MCP request did not provide application services.");
                return new QueueManagementTools(
                    services.GetRequiredService<IQueueManagementService>());
            },
            new McpServerToolCreateOptions
            {
                SerializerOptions = serialiserOptions
            });

        RestoreActionSchema(tool);
        return tool;
    }

    private static void RestoreActionSchema(McpServerTool tool)
    {
        var inputSchema = JsonNode.Parse(tool.ProtocolTool.InputSchema.GetRawText())
            ?.AsObject()
            ?? throw new InvalidOperationException(
                "The manage_queue MCP tool input schema was not an object.");
        var properties = inputSchema["properties"]?.AsObject()
            ?? throw new InvalidOperationException(
                "The manage_queue MCP tool input schema did not contain properties.");
        var action = properties["action"]?.AsObject()
            ?? throw new InvalidOperationException(
                "The manage_queue MCP tool input schema did not contain action.");

        action["type"] = "string";
        action["enum"] = new JsonArray("clear", "append", "insert_next");
        tool.ProtocolTool.InputSchema = JsonSerializer.SerializeToElement(
            inputSchema,
            McpJsonUtilities.DefaultOptions);
    }
}
