using System.Text.Json;
using System.Text.Json.Serialization;
using ModelContextProtocol;

namespace LyrionVoiceMcp.Api.Tools;

internal static class McpToolJson
{
    public static JsonSerializerOptions Options { get; } = CreateOptions();

    public static JsonElement Serialize<T>(T value) =>
        JsonSerializer.SerializeToElement(value, Options);

    private static JsonSerializerOptions CreateOptions() =>
        new(McpJsonUtilities.DefaultOptions)
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.Never
        };
}
