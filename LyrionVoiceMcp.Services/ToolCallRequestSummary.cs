using System.Globalization;
using System.Text.Json;
using LyrionVoiceMcp.Abstractions;

namespace LyrionVoiceMcp.Services;

internal static class ToolCallRequestSummary
{
    internal const int MaximumLength = 240;

    public static string? Create(
        string toolName,
        string argumentsJson,
        bool argumentsTruncated,
        IReadOnlyList<ToolCallReferenceSnapshot>? referenceSnapshots = null)
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
                "browse" => Browse(arguments, referenceSnapshots),
                "get_player_status" => EmptyRequest(arguments, "All players"),
                "get_queue" => Text(arguments, "player"),
                "control_player" => PlayerRequest(arguments, includeItems: false),
                "manage_queue" or "play" => PlayerRequest(
                    arguments,
                    includeItems: true,
                    referenceSnapshots),
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

    private static string? Browse(
        JsonElement arguments,
        IReadOnlyList<ToolCallReferenceSnapshot>? referenceSnapshots)
    {
        var snapshot = referenceSnapshots?.FirstOrDefault(
            item => item.ArgumentPath == "browseRef");
        return snapshot is null
            ? Text(arguments, "browseRef") ?? EmptyRequest(arguments, "Library roots")
            : ReferenceLabel(snapshot);
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

    private static string? PlayerRequest(
        JsonElement arguments,
        bool includeItems,
        IReadOnlyList<ToolCallReferenceSnapshot>? referenceSnapshots = null)
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
            var itemSnapshots = referenceSnapshots?
                .Where(item => item.ArgumentPath.StartsWith("items[", StringComparison.Ordinal))
                .ToArray();
            if (itemSnapshots is { Length: > 0 })
            {
                var itemSummary = ReferenceLabel(itemSnapshots[0]);
                if (itemSnapshots.Length > 1)
                {
                    itemSummary += $" + {itemSnapshots.Length - 1} more";
                }
                parts.Add(itemSummary);
            }
            else
            {
                var noun = count == 1 ? "item" : "items";
                parts.Add($"{count.ToString(CultureInfo.InvariantCulture)} requested {noun}");
            }
        }
        return Join(parts);
    }

    private static string ReferenceLabel(ToolCallReferenceSnapshot snapshot)
    {
        if (snapshot.DisplayMetadata is not { } metadata)
        {
            return SingleLine(snapshot.Reference);
        }

        var parts = new List<string> { SingleLine(metadata.Title) };
        if (!string.IsNullOrWhiteSpace(metadata.Artist)
            && !string.Equals(metadata.Artist, metadata.Title, StringComparison.OrdinalIgnoreCase))
        {
            parts.Add(SingleLine(metadata.Artist));
        }
        if (!string.IsNullOrWhiteSpace(metadata.Album)
            && !string.Equals(metadata.Album, metadata.Title, StringComparison.OrdinalIgnoreCase))
        {
            parts.Add(SingleLine(metadata.Album));
        }

        var label = string.Join(" · ", parts);
        return metadata.IsContinuation ? label + " · Continued" : label;
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
        var singleLine = SingleLine(text);
        return singleLine.Length == 0 ? null : singleLine;
    }

    private static string SingleLine(string text)
    {
        var normalised = new string(text.Select(character => char.IsControl(character) ? ' ' : character).ToArray());
        return string.Join(' ', normalised.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
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
