using System.Globalization;
using System.Text.Json;

namespace LyrionVoiceMcp.Services;

internal static class ToolCallRequestSummary
{
    internal const int MaximumLength = 240;

    public static string? Create(string toolName, string argumentsJson, bool argumentsTruncated)
    {
        if (argumentsTruncated)
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(argumentsJson);
            var arguments = document.RootElement;
            if (arguments.ValueKind is not (JsonValueKind.Object or JsonValueKind.Null))
            {
                return null;
            }

            var summary = toolName switch
            {
                "search" => Search(arguments),
                "browse" => Text(arguments, "browseRef") ?? EmptyRequest(arguments, "Library roots"),
                "get_player_status" => EmptyRequest(arguments, "All players"),
                "get_queue" => Text(arguments, "player"),
                "control_player" => PlayerRequest(arguments, includeItems: false),
                "manage_queue" or "play" => PlayerRequest(arguments, includeItems: true),
                _ => null
            };
            return Bound(summary);
        }
        catch (JsonException)
        {
            // A malformed or incomplete recording must not prevent the history page from loading.
            return null;
        }
    }

    private static string? Search(JsonElement arguments)
    {
        var parts = new List<string>();
        Add(parts, Text(arguments, "name"));
        Add(parts, Text(arguments, "genre"), "Genre: ");
        var from = Number(arguments, "fromYear");
        var to = Number(arguments, "toYear");
        if (from is not null && to is not null)
        {
            parts.Add($"Years: {from}–{to}");
        }
        else
        {
            Add(parts, from, "From year: ");
            Add(parts, to, "To year: ");
        }

        var rating = Number(arguments, "rating");
        var match = Text(arguments, "ratingMatch");
        if (rating is not null)
        {
            var prefix = match switch
            {
                "at_least" => "Rating: ≥ ",
                "exact" => "Rating: = ",
                _ => "Rating: "
            };
            Add(parts, rating, prefix);
        }
        if (match is not null && (rating is null || match is not ("at_least" or "exact")))
        {
            Add(parts, match, "Rating match: ");
        }

        return Join(parts) ?? EmptyRequest(arguments, "Broad search");
    }

    private static string? PlayerRequest(JsonElement arguments, bool includeItems)
    {
        var parts = new List<string>();
        Add(parts, Text(arguments, "player"));
        var action = Text(arguments, "action");
        if (action is not null)
        {
            Add(parts, char.ToUpperInvariant(action[0]) + action[1..].Replace('_', ' '));
        }
        if (includeItems && Property(arguments, "items") is { ValueKind: JsonValueKind.Array } items)
        {
            var count = items.GetArrayLength();
            var noun = count == 1 ? "item" : "items";
            parts.Add($"{count.ToString(CultureInfo.InvariantCulture)} requested {noun}");
        }
        return Join(parts);
    }

    private static JsonElement? Property(JsonElement arguments, string name) =>
        arguments.ValueKind == JsonValueKind.Object && arguments.TryGetProperty(name, out var value)
            ? value
            : null;

    private static string? Text(JsonElement arguments, string name)
    {
        if (Property(arguments, name) is not { ValueKind: JsonValueKind.String } value)
        {
            return null;
        }
        var text = value.GetString()!;
        var normalised = new string(text.Select(character => char.IsControl(character) ? ' ' : character).ToArray());
        var singleLine = string.Join(' ', normalised.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return singleLine.Length == 0 ? null : singleLine;
    }

    private static string? Number(JsonElement arguments, string name) =>
        Property(arguments, name) is { ValueKind: JsonValueKind.Number } value && value.TryGetDecimal(out var number)
            ? number.ToString(CultureInfo.InvariantCulture)
            : null;

    private static string? EmptyRequest(JsonElement arguments, string summary)
    {
        if (arguments.ValueKind == JsonValueKind.Null)
        {
            return summary;
        }
        if (arguments.EnumerateObject().All(property => property.Value.ValueKind == JsonValueKind.Null))
        {
            return summary;
        }
        return null;
    }

    private static void Add(List<string> parts, string? value, string prefix = "")
    {
        if (value is not null)
        {
            parts.Add(prefix + value);
        }
    }

    private static string? Join(List<string> parts) => parts.Count == 0 ? null : string.Join(" · ", parts);

    private static string? Bound(string? summary)
    {
        if (summary is null || summary.Length <= MaximumLength)
        {
            return summary;
        }
        var length = MaximumLength - 1;
        if (char.IsHighSurrogate(summary[length - 1]))
        {
            length--;
        }
        return summary[..length] + "…";
    }
}
