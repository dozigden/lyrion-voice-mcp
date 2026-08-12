using System.Text.Json;
using System.Text.Json.Nodes;
using LyrionVoiceMcp.Abstractions;
using LyrionVoiceMcp.Contracts;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace LyrionVoiceMcp.Api.Tools;

public static class PlaybackToolRegistration
{
    public static McpServerTool Create()
    {
        var serialiserOptions = new JsonSerializerOptions(
            McpJsonUtilities.DefaultOptions);
        for (var index = serialiserOptions.Converters.Count - 1; index >= 0; index--)
        {
            if (serialiserOptions.Converters[index]
                .CanConvert(typeof(PlayQueueMode)))
            {
                serialiserOptions.Converters.RemoveAt(index);
            }
        }

        var method = typeof(PlaybackTools).GetMethod(nameof(PlaybackTools.PlayAsync))
            ?? throw new InvalidOperationException(
                "The play MCP tool method could not be found.");
        var tool = McpServerTool.Create(
            method,
            request =>
            {
                var services = request.Services
                    ?? throw new InvalidOperationException(
                        "The play MCP request did not provide application services.");
                return new PlaybackTools(
                    services.GetRequiredService<IPlaybackService>());
            },
            new McpServerToolCreateOptions
            {
                SerializerOptions = serialiserOptions
            });

        RestoreModeSchema(tool);
        return tool;
    }

    private static void RestoreModeSchema(McpServerTool tool)
    {
        var inputSchema = JsonNode.Parse(tool.ProtocolTool.InputSchema.GetRawText())
            ?.AsObject()
            ?? throw new InvalidOperationException(
                "The play MCP tool input schema was not an object.");
        var properties = inputSchema["properties"]?.AsObject()
            ?? throw new InvalidOperationException(
                "The play MCP tool input schema did not contain properties.");
        var mode = properties["mode"]?.AsObject()
            ?? throw new InvalidOperationException(
                "The play MCP tool input schema did not contain mode.");

        mode["type"] = "string";
        mode["enum"] = new JsonArray("replace", "append");
        tool.ProtocolTool.InputSchema = JsonSerializer.SerializeToElement(
            inputSchema,
            McpJsonUtilities.DefaultOptions);
    }
}
