using System.Text.Json;
using System.Text.Json.Nodes;
using LyrionVoiceMcp.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace LyrionVoiceMcp.Api.Tools;

public static class SearchToolRegistration
{
    public static McpServerTool Create()
    {
        var method = typeof(SearchTools)
            .GetMethod(nameof(SearchTools.SearchAsync))
            ?? throw new InvalidOperationException(
                "The search MCP tool method could not be found.");
        var tool = McpServerTool.Create(
            method,
            request =>
            {
                var services = request.Services
                    ?? throw new InvalidOperationException(
                        "The search MCP request did not provide application services.");
                return new SearchTools(services.GetRequiredService<ISearchService>());
            },
            new McpServerToolCreateOptions
            {
                SerializerOptions = McpToolJson.Options
            });

        RestoreRatingSchema(tool);
        return tool;
    }

    private static void RestoreRatingSchema(McpServerTool tool)
    {
        var inputSchema = JsonNode.Parse(tool.ProtocolTool.InputSchema.GetRawText())
            ?.AsObject()
            ?? throw new InvalidOperationException(
                "The search MCP tool input schema was not an object.");
        var properties = inputSchema["properties"]?.AsObject()
            ?? throw new InvalidOperationException(
                "The search MCP tool input schema did not contain properties.");
        var rating = properties["rating"]?.AsObject()
            ?? throw new InvalidOperationException(
                "The search MCP tool input schema did not contain rating.");
        var ratingMatch = properties["ratingMatch"]?.AsObject()
            ?? throw new InvalidOperationException(
                "The search MCP tool input schema did not contain ratingMatch.");

        rating["minimum"] = 0;
        rating["maximum"] = 5;
        ratingMatch["enum"] = new JsonArray("exact", "at_least");
        tool.ProtocolTool.InputSchema = JsonSerializer.SerializeToElement(
            inputSchema,
            McpJsonUtilities.DefaultOptions);
    }
}
