using System.Text.Json;
using System.Text.Json.Serialization;
using LyrionVoiceMcp.Abstractions;

namespace LyrionVoiceMcp.Services;

internal static class ToolCallReferenceSnapshots
{
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    public static IReadOnlyList<ToolCallReferenceSnapshot>? Capture(
        string toolName,
        string argumentsJson,
        Func<string, ReferenceDisplayMetadata?> resolve)
    {
        if (toolName is not ("browse" or "play" or "manage_queue"))
        {
            return null;
        }

        using var document = JsonDocument.Parse(argumentsJson);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            return [];
        }

        if (toolName == "browse")
        {
            return Text(document.RootElement, "browseRef") is { } reference
                ? [Snapshot("browseRef", reference, resolve)]
                : [];
        }

        if (!document.RootElement.TryGetProperty("items", out var items)
            || items.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var snapshots = new List<ToolCallReferenceSnapshot>();
        var index = 0;
        foreach (var item in items.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String)
            {
                var reference = item.GetString()!;
                snapshots.Add(Snapshot($"items[{index}]", reference, resolve));
            }
            index++;
        }
        return snapshots;
    }

    public static string Serialise(IReadOnlyList<ToolCallReferenceSnapshot> snapshots) =>
        JsonSerializer.Serialize(snapshots, JsonOptions);

    public static IReadOnlyList<ToolCallReferenceSnapshot>? Deserialise(
        string? json,
        bool truncated)
    {
        if (json is null || truncated)
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<ToolCallReferenceSnapshot[]>(json, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static ToolCallReferenceSnapshot Snapshot(
        string path,
        string reference,
        Func<string, ReferenceDisplayMetadata?> resolve) => new(
        path,
        reference,
        resolve(reference));

    private static string? Text(JsonElement value, string propertyName) =>
        value.TryGetProperty(propertyName, out var property)
        && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower));
        return options;
    }
}
