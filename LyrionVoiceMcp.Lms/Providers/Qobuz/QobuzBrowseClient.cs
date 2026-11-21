using System.Text.Json;
using System.Text.RegularExpressions;
using LyrionVoiceMcp.Abstractions;
using LyrionVoiceMcp.Abstractions.Providers;
using LyrionVoiceMcp.Abstractions.Providers.Qobuz;

namespace LyrionVoiceMcp.Lms.Providers.Qobuz;

internal sealed partial class QobuzClient
{
    private readonly LmsProviderQueue queue = new(rpc, "Qobuz", TrackIdentity);

    public async Task<IReadOnlyList<QobuzAlbum>> ReadReleasesAsync(CancellationToken cancellationToken)
    {
        var player = await SelectPlayerAsync(cancellationToken);
        var releases = await ReadReleaseLocationsAsync(player, cancellationToken);
        return releases.Select(entry => entry.Album).ToArray();
    }

    public async Task<IReadOnlyList<QobuzTrack>> ReadTracksAsync(QobuzAlbum album, CancellationToken cancellationToken) =>
        await ReadTracksAsync(await SelectPlayerAsync(cancellationToken), album, cancellationToken);

    public async Task SubmitAsync(string playerId, QobuzTrackTarget target, ProviderPlaybackCommand command,
        CancellationToken cancellationToken)
    {
        var tracks = await ReadTracksAsync(playerId, target.Album, cancellationToken);
        var track = tracks.FirstOrDefault(item => item.Id == target.TrackId)
            ?? throw new LmsRequestException("This Qobuz track is no longer available.");
        await queue.SubmitAsync(playerId, track.Url, track.Id, command, cancellationToken);
    }

    private async Task<string> SelectPlayerAsync(CancellationToken cancellationToken) =>
        await discovery.FindConnectedPlayerAsync(cancellationToken)
            ?? throw new LmsRequestException("Browsing Qobuz requires a connected LMS player.");

    private async Task<IReadOnlyList<QobuzTrack>> ReadTracksAsync(string player, QobuzAlbum album,
        CancellationToken cancellationToken)
    {
        // LMS item_id values are menu positions. Reopen the matching release on this player.
        var releases = await ReadReleaseLocationsAsync(player, cancellationToken);
        var matches = releases.Where(entry => entry.Album == album).Take(2).ToArray();
        if (matches.Length != 1) throw new LmsRequestException("This Qobuz release could not be identified. Browse New Releases again.");

        var tracks = new List<QobuzTrack>();
        foreach (var item in await ReadMenuItemsAsync(player, matches[0].Path, cancellationToken))
        {
            if (!item.TryGetProperty("presetParams", out var preset) || preset.ValueKind != JsonValueKind.Object
                || LmsJson.ReadString(preset, "favorites_type") != "audio") continue;
            var url = LmsJson.ReadString(preset, "favorites_url");
            if (TrackIdentity(url) is not { } id || !HasText(item, "text")) throw InvalidBrowseMenu();
            var lines = item.GetProperty("text").GetString()!.Split('\n', 2);
            var suffix = " - " + album.Title;
            var artist = lines.Length == 2 && lines[1].EndsWith(suffix, StringComparison.Ordinal)
                ? lines[1][..^suffix.Length].Trim() : null;
            tracks.Add(new QobuzTrack(id, url!, lines[0].Trim(), artist));
        }
        return tracks;
    }

    private async Task<List<(QobuzAlbum Album, string Path)>> ReadReleaseLocationsAsync(string player,
        CancellationToken cancellationToken)
    {
        const string labelKey = "PLUGIN_QOBUZ_NEW_RELEASES";
        var labels = await rpc.SendAsync(player, ["getstring", labelKey], cancellationToken);
        var root = await ReadMenuItemsAsync(player, null, cancellationToken);
        var label = LmsJson.ReadString(labels, labelKey);
        var matches = root.Where(item => label is not null && LmsJson.ReadString(item, "text") == label).Take(2).ToArray();
        if (matches.Length != 1) throw InvalidBrowseMenu();

        var entries = await ReadMenuItemsAsync(player, BrowsePath(matches[0]), cancellationToken);
        var releases = new List<(QobuzAlbum, string)>();
        foreach (var item in entries)
        {
            if (!HasText(item, "text")) throw InvalidBrowseMenu();
            releases.Add((new QobuzAlbum(item.GetProperty("text").GetString()!, LmsJson.ReadString(item, "icon")), BrowsePath(item)));
        }
        return releases;
    }

    private async Task<List<JsonElement>> ReadMenuItemsAsync(string player, string? path, CancellationToken cancellationToken,
        string menuCommand = PluginCommand, string? search = null, int limit = 10_000)
    {
        var items = new List<JsonElement>();
        int? total = null;
        do
        {
            var size = Math.Min(PageSize, limit - items.Count);
            var command = new List<object> { menuCommand, "items", items.Count, size, "menu:1" };
            if (path is not null) command.Add($"item_id:{path}");
            if (search is not null) command.Add($"search:{search}");
            var response = await rpc.SendAsync(player, command.ToArray(), cancellationToken);
            if (LmsJson.ReadInt(response, "count") is not int count || count < 0 || count > 10_000
                || (total.HasValue && count != total) || LmsJson.ReadInt(response, "offset") != items.Count)
                throw InvalidBrowseMenu();
            if (response.TryGetProperty("window", out var window) && HasText(window, "textarea"))
                throw InvalidBrowseMenu();
            total = count;
            if (!response.TryGetProperty("item_loop", out var loop) && count == 0) break;
            if (loop.ValueKind != JsonValueKind.Array || loop.GetArrayLength() != Math.Min(size, count - items.Count)
                || loop.EnumerateArray().Any(item => item.ValueKind != JsonValueKind.Object))
                throw InvalidBrowseMenu();
            items.AddRange(loop.EnumerateArray());
        } while (items.Count < total && items.Count < limit);
        return items;
    }

    private static string BrowsePath(JsonElement item, string command = PluginCommand)
    {
        var source = item;
        if (item.TryGetProperty("actions", out var actions))
        {
            if (actions.ValueKind != JsonValueKind.Object || !actions.TryGetProperty("go", out source)
                || source.ValueKind != JsonValueKind.Object || !source.TryGetProperty("cmd", out var cmd)
                || cmd.ValueKind != JsonValueKind.Array || cmd.GetArrayLength() != 2
                || cmd[0].ValueKind != JsonValueKind.String || cmd[1].ValueKind != JsonValueKind.String
                || !cmd[0].ValueEquals(command) || !cmd[1].ValueEquals("items")) throw InvalidBrowseMenu();
        }
        if (!source.TryGetProperty("params", out var parameters) || parameters.ValueKind != JsonValueKind.Object
            || LmsJson.ReadString(parameters, "item_id") is not { Length: > 0 and <= 8192 } path)
            throw InvalidBrowseMenu();
        return path;
    }

    private static string? TrackIdentity(string? url) =>
        TrackUrl().Match(url ?? string.Empty) is { Success: true } match ? match.Groups[1].Value : null;

    [GeneratedRegex(@"\Aqobuz://([0-9]+)\.(?:flac|mp3)\z")]
    private static partial Regex TrackUrl();

    private static LmsRequestException InvalidBrowseMenu() => new("Qobuz returned an incomplete or unsupported browse menu.");
}
