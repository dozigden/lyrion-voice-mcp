using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using LyrionVoiceMcp.Abstractions;

namespace LyrionVoiceMcp.Lms.Providers.BbcSounds;

internal sealed partial class BbcSoundsMenuClient(LmsJsonRpcClient rpc)
{
    private const int PageSize = 50;
    private const int MaximumItems = 10_001;

    public async Task<bool> IsInstalledAsync(CancellationToken cancellationToken)
    {
        foreach (var command in new[] { "apps", "radios" })
        {
            var offset = 0;
            int? expected = null;
            do
            {
                var response = await rpc.SendAsync([command, offset, PageSize], cancellationToken);
                var count = ReadCount(response);
                if (expected is not null && expected != count) throw InvalidMenu();
                expected = count;
                var items = Items(response, command + "s_loop", count);
                if (offset + items.Count > count) throw InvalidMenu();
                if (items.Any(x => Text(x, "cmd") == "bbcsounds")) return true;
                if (items.Count == 0 && offset < count) throw InvalidMenu();
                offset += items.Count;
            } while (offset < expected);
        }
        return false;
    }

    public async Task<string> SelectPlayerAsync(CancellationToken cancellationToken)
    {
        var players = new List<string>();
        var offset = 0;
        int? expected = null;
        do
        {
            var response = await rpc.SendAsync(["players", offset, PageSize], cancellationToken);
            var count = ReadCount(response);
            if (expected is not null && expected != count) throw InvalidMenu();
            expected = count;
            var items = Items(response, "players_loop", count);
            if (offset + items.Count > count) throw InvalidMenu();
            foreach (var item in items)
            {
                if (Text(item, "connected") == "1" && Text(item, "playerid") is { Length: > 0 } id) players.Add(id);
            }
            if (items.Count == 0 && offset < count) throw InvalidMenu();
            offset += items.Count;
        } while (offset < expected);
        return players.Order(StringComparer.Ordinal).FirstOrDefault()
            ?? throw new LmsRequestException("BBC Sounds browsing requires a connected LMS player.");
    }

    public async Task<IReadOnlyList<JsonElement>> ReadMenuAsync(string player, string? path, CancellationToken cancellationToken)
    {
        var result = new List<JsonElement>();
        int? expected = null;
        do
        {
            object[] command = path is null
                ? ["bbcsounds", "items", result.Count, PageSize, "menu:1"]
                : ["bbcsounds", "items", result.Count, PageSize, "menu:1", $"item_id:{path}"];
            var response = await rpc.SendAsync(player, command, cancellationToken);
            var count = ReadCount(response);
            if (expected is not null && expected != count) throw InvalidMenu();
            expected = count;
            var items = Items(response, "item_loop", count);
            if (items.Count == 0 && result.Count < count) throw InvalidMenu();
            result.AddRange(items);
            if (result.Count > count || result.Count > MaximumItems) throw InvalidMenu();
        } while (result.Count < expected);
        return result;
    }

    public async IAsyncEnumerable<JsonElement> ReadCollectionAsync(string player, string path,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var total = 0;
        int? expectedTotal = null;
        int? expectedEnd = null;
        while (true)
        {
            if (!visited.Add(path) || visited.Count > 500) throw InvalidMenu();
            var items = await ReadMenuAsync(player, path, cancellationToken);
            Continuation? next = null;
            var page = new List<JsonElement>();
            foreach (var item in items)
            {
                var continuation = NextPage().Match(Title(item));
                if (continuation.Success)
                {
                    if (next is not null) throw InvalidMenu();
                    next = new Continuation(BrowsePath(item),
                        ReadPageNumber(continuation, "start"), ReadPageNumber(continuation, "end"),
                        ReadPageNumber(continuation, "total"));
                    continue;
                }
                page.Add(item);
            }
            total += page.Count;
            if (total > MaximumItems || (expectedEnd is not null && total != expectedEnd)) throw InvalidMenu();
            if (next is null)
            {
                if (expectedTotal is not null && total != expectedTotal) throw InvalidMenu();
            }
            else
            {
                // The plugin labels the next page with zero-based start/end offsets
                // and the full collection size. Validate those promises before yielding.
                if (page.Count == 0 || next.Start != total || next.End <= next.Start
                    || next.End > next.Total || next.Total > MaximumItems
                    || (expectedTotal is not null && next.Total != expectedTotal)) throw InvalidMenu();
                expectedTotal = next.Total;
                expectedEnd = next.End;
            }
            foreach (var item in page) yield return item;
            if (next is null) yield break;
            path = next.Path;
        }
    }

    private static int ReadPageNumber(Match match, string group) =>
        int.TryParse(match.Groups[group].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var value)
            ? value : throw InvalidMenu();

    private sealed record Continuation(string Path, int Start, int End, int Total);

    public static string BrowsePath(JsonElement item)
    {
        JsonElement parameters;
        if (item.TryGetProperty("actions", out var actions) && actions.TryGetProperty("go", out var go))
        {
            if (!go.TryGetProperty("cmd", out var cmd) || cmd.ValueKind != JsonValueKind.Array
                || !cmd.EnumerateArray().Select(x => x.ValueKind == JsonValueKind.String ? x.GetString() : null).SequenceEqual(new[] { "bbcsounds", "items" }))
                throw InvalidMenu();
            if (!go.TryGetProperty("params", out parameters)) throw InvalidMenu();
        }
        else if (!item.TryGetProperty("params", out parameters)) throw InvalidMenu();
        // Deliberately copy only the navigation locator, never touchToPlay, account
        // actions or arbitrary command parameters supplied by a plugin menu.
        return Text(parameters, "item_id") is { Length: > 0 } path ? path : throw InvalidMenu();
    }

    public static string? FavouriteUrl(JsonElement item) =>
        item.TryGetProperty("presetParams", out var preset) ? Text(preset, "favorites_url") : null;
    public static string Title(JsonElement item) => (Text(item, "text") ?? Text(item, "name") ?? string.Empty).Trim();
    public static string? Text(JsonElement item, string key) =>
        item.ValueKind == JsonValueKind.Object && item.TryGetProperty(key, out var value)
            ? ReadText(value) : null;

    private static string? ReadText(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString(),
        JsonValueKind.Number => value.ToString(),
        _ => null
    };

    private static int ReadCount(JsonElement response) =>
        LmsJson.ReadInt(response, "count") is { } count && count >= 0 && count <= MaximumItems
            ? count : throw InvalidMenu();
    private static IReadOnlyList<JsonElement> Items(JsonElement response, string key, int count)
    {
        if (!response.TryGetProperty(key, out var items)) return count == 0 ? [] : throw InvalidMenu();
        if (items.ValueKind != JsonValueKind.Array) throw InvalidMenu();
        var values = items.EnumerateArray().ToArray();
        if (values.Any(x => x.ValueKind != JsonValueKind.Object) || values.Length > PageSize || values.Length > count)
            throw InvalidMenu();
        return values;
    }
    public static LmsRequestException InvalidMenu() => new("BBC Sounds returned an incomplete or unsupported menu. Check the plugin and account in LMS.");
    [GeneratedRegex(@"^Next - (?<start>[0-9]+) to (?<end>[0-9]+) of (?<total>[0-9]+)$", RegexOptions.CultureInvariant)]
    private static partial Regex NextPage();
}
