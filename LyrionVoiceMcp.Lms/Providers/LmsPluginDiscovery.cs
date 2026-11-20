using System.Runtime.CompilerServices;
using System.Text.Json;
using LyrionVoiceMcp.Abstractions;

namespace LyrionVoiceMcp.Lms.Providers;

internal sealed class LmsPluginDiscovery(
    LmsJsonRpcClient rpc,
    Func<LmsRequestException> invalidResponse)
{
    private const int PageSize = 50;
    private const int MaximumItems = 10_001;

    public async Task<bool> IsInstalledAsync(string pluginCommand, CancellationToken cancellationToken)
    {
        foreach (var (command, loopKey) in new[] { ("apps", "appss_loop"), ("radios", "radioss_loop") })
        {
            await foreach (var item in ReadEntriesAsync(command, loopKey, cancellationToken))
            {
                if (LmsJson.ReadString(item, "cmd") == pluginCommand) return true;
            }
        }
        return false;
    }

    public async Task<string?> FindConnectedPlayerAsync(CancellationToken cancellationToken)
    {
        var players = new List<string>();
        await foreach (var item in ReadEntriesAsync("players", "players_loop", cancellationToken))
        {
            if (LmsJson.ReadString(item, "connected") == "1"
                && LmsJson.ReadString(item, "playerid") is { Length: > 0 } id)
            {
                players.Add(id);
            }
        }
        return players.Order(StringComparer.Ordinal).FirstOrDefault();
    }

    private async IAsyncEnumerable<JsonElement> ReadEntriesAsync(
        string command, string loopKey, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var offset = 0;
        int? expected = null;
        do
        {
            var response = await rpc.SendAsync([command, offset, PageSize], cancellationToken);
            if (LmsJson.ReadInt(response, "count") is not int count || count < 0 || count > MaximumItems
                || (expected is not null && count != expected))
            {
                throw invalidResponse();
            }

            expected = count;
            if (!response.TryGetProperty(loopKey, out var loop) && count == 0) yield break;
            if (loop.ValueKind != JsonValueKind.Array) throw invalidResponse();
            var items = loop.EnumerateArray().ToArray();
            if (items.Any(item => item.ValueKind != JsonValueKind.Object)
                || items.Length > PageSize || offset + items.Length > count
                || (items.Length == 0 && offset < count))
            {
                throw invalidResponse();
            }

            foreach (var item in items) yield return item;
            offset += items.Length;
        } while (offset < expected);
    }
}
