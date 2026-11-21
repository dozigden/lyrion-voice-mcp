using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LyrionVoiceMcp.Abstractions;
using LyrionVoiceMcp.Abstractions.Providers;
using LyrionVoiceMcp.Abstractions.Providers.Qobuz;

namespace LyrionVoiceMcp.Lms.Providers.Qobuz;

internal sealed partial class QobuzClient
{
    private const string GlobalSearch = "globalsearch";
    private const string ReleasesKey = "PLUGIN_QOBUZ_RELEASES";
    private static readonly (MediaEntityKind Kind, string Label, int Limit)[] SearchGroups =
    [
        (MediaEntityKind.Artist, "ARTISTS", SearchResultPolicy.ArtistLimit),
        (MediaEntityKind.Album, ReleasesKey, SearchResultPolicy.AlbumLimit),
        (MediaEntityKind.Track, "SONGS", SearchResultPolicy.TrackLimit),
        (MediaEntityKind.Playlist, "PLAYLISTS", SearchResultPolicy.PlaylistLimit)
    ];

    public string ProviderId => PluginCommand;

    public async Task<IReadOnlyList<ProviderSearchCandidate>> SearchAsync(string name, CancellationToken cancellationToken)
    {
        var status = await GetConfigurationStatusAsync(cancellationToken);
        if (status != QobuzConfigurationStatus.Configured)
            throw new LmsRequestException(status == QobuzConfigurationStatus.NotInstalled
                ? "The Qobuz plugin is not installed." : "Configure the Qobuz account in LMS before searching.");
        var player = await SelectPlayerAsync(cancellationToken);
        var groups = await FindSearchGroupsAsync(player, name, cancellationToken);
        var results = new List<ProviderSearchCandidate>();
        foreach (var (kind, _, limit) in SearchGroups)
        {
            var rows = await ReadMenuItemsAsync(player, groups[kind], cancellationToken, GlobalSearch, limit: limit);
            results.AddRange(Candidates(rows, name, kind));
        }
        return results;
    }

    private async Task<Dictionary<MediaEntityKind, string>> FindSearchGroupsAsync(string player, string query, CancellationToken token)
    {
        var roots = await ReadMenuItemsAsync(player, null, token, GlobalSearch, query);
        var provider = FindMenu(roots, "Qobuz");
        var groups = await ReadMenuItemsAsync(player, provider, token, GlobalSearch);
        var labels = await rpc.SendAsync(player, ["getstring", string.Join(',', SearchGroups.Select(group => group.Label))], token);
        return SearchGroups.ToDictionary(group => group.Kind, group => FindMenu(groups, LmsJson.ReadString(labels, group.Label)));
    }

    private static string FindMenu(IEnumerable<JsonElement> items, string? label)
    {
        var matches = items.Where(item => label is not null && LmsJson.ReadString(item, "text") == label).Take(2).ToArray();
        if (matches.Length != 1) throw InvalidBrowseMenu();
        return BrowsePath(matches[0], GlobalSearch);
    }

    private static IEnumerable<ProviderSearchCandidate> Candidates(IEnumerable<JsonElement> rows, string query,
        MediaEntityKind kind, QobuzSearchLocation? parent = null)
    {
        foreach (var row in rows)
        {
            if (LmsJson.ReadString(row, "type") is "text" or "header") continue;
            if (!HasText(row, "text")) throw InvalidBrowseMenu();
            var label = row.GetProperty("text").GetString()!;
            var lines = label.Split('\n', 2);
            var title = lines[0].Trim();
            var detail = lines.Length == 2 ? lines[1].Trim() : null;
            if (kind == MediaEntityKind.Track)
            {
                if (!row.TryGetProperty("presetParams", out var preset) || preset.ValueKind != JsonValueKind.Object
                    || LmsJson.ReadString(preset, "favorites_type") != "audio") continue;
                var url = LmsJson.ReadString(preset, "favorites_url");
                if (TrackIdentity(url) is not { } id) throw InvalidBrowseMenu();
                var credits = detail?.Split(" - ", 2);
                yield return new ProviderSearchCandidate(new MediaIdentity(kind, id, PluginCommand), title, "provider_order",
                    null, new QobuzAudioTarget(url!), credits?.FirstOrDefault(), credits?.ElementAtOrDefault(1));
                continue;
            }
            // Release group headings and unavailable releases have no playable collection.
            if (kind is MediaEntityKind.Album or MediaEntityKind.Playlist && LmsJson.ReadString(row, "type") != "playlist") continue;
            _ = BrowsePath(row, GlobalSearch);
            var location = new QobuzSearchLocation(query, kind, label, LmsJson.ReadString(row, "icon"), parent);
            var key = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(location))));
            var artist = kind == MediaEntityKind.Album && detail is not null
                ? System.Text.RegularExpressions.Regex.Replace(detail, @" ?\([0-9]{4}\)$", "").Trim() : null;
            yield return new ProviderSearchCandidate(new MediaIdentity(kind, key, PluginCommand), title, "provider_order",
                location, kind == MediaEntityKind.Artist ? null : new QobuzCollectionTarget(location), artist);
        }
    }

    public async Task<IReadOnlyList<ProviderSearchCandidate>> BrowseAsync(QobuzSearchLocation location, CancellationToken token)
    {
        var player = await SelectPlayerAsync(token);
        var path = await ResolveSearchPathAsync(player, location, token);
        if (location.Kind == MediaEntityKind.Artist)
        {
            var releases = await ArtistReleasesAsync(player, path, token);
            return Candidates(releases, location.Query, MediaEntityKind.Album, location).ToArray();
        }
        return await ReadCollectionTracksAsync(player, location, path, token);
    }

    private async Task<List<JsonElement>> ArtistReleasesAsync(string player, string artistPath, CancellationToken token)
    {
        var menu = await ReadMenuItemsAsync(player, artistPath, token, GlobalSearch);
        var labels = await rpc.SendAsync(player, ["getstring", ReleasesKey], token);
        return await ReadMenuItemsAsync(player, FindMenu(menu, LmsJson.ReadString(labels, ReleasesKey)), token, GlobalSearch);
    }

    private async Task<string> ResolveSearchPathAsync(string player, QobuzSearchLocation location, CancellationToken token)
    {
        List<JsonElement> items;
        if (location.Parent is { } artist)
        {
            items = await ArtistReleasesAsync(player, await ResolveSearchPathAsync(player, artist, token), token);
        }
        else
        {
            var groups = await FindSearchGroupsAsync(player, location.Query, token);
            items = await ReadMenuItemsAsync(player, groups[location.Kind], token, GlobalSearch);
        }
        var matches = items.Where(item => LmsJson.ReadString(item, "text") == location.Label
            && LmsJson.ReadString(item, "icon") == location.Artwork).Take(2).ToArray();
        if (matches.Length != 1) throw new LmsRequestException("This Qobuz result could not be identified. Search again.");
        return BrowsePath(matches[0], GlobalSearch);
    }

    private async Task<ProviderSearchCandidate[]> ReadCollectionTracksAsync(string player, QobuzSearchLocation location,
        string path, CancellationToken token) => Candidates(await ReadMenuItemsAsync(player, path, token, GlobalSearch),
            location.Query, MediaEntityKind.Track).ToArray();

    public async Task<int> GetPlayableCountAsync(ProviderMediaTarget target, CancellationToken token)
    {
        if (target is QobuzAudioTarget audio)
            return TrackIdentity(audio.Url) is not null && await GetConfigurationStatusAsync(token) == QobuzConfigurationStatus.Configured ? 1 : 0;
        if (target is not QobuzCollectionTarget collection) return 0;
        return (await BrowseAsync(collection.Location, token)).Count;
    }

    public async Task SubmitAsync(string player, ProviderMediaTarget target, ProviderPlaybackCommand command, CancellationToken token)
    {
        if (target is QobuzAudioTarget audio && TrackIdentity(audio.Url) is { } id)
        {
            await queue.SubmitAsync(player, audio.Url, id, command, token);
            return;
        }
        if (target is not QobuzCollectionTarget collection) throw InvalidBrowseMenu();
        var path = await ResolveSearchPathAsync(player, collection.Location, token);
        var tracks = await ReadCollectionTracksAsync(player, collection.Location, path, token);
        await queue.SubmitCollectionAsync(player, command, tracks.Select(track => track.Identity.Id).ToArray(),
            verb => [GlobalSearch, "playlist", verb, $"item_id:{path}", "menu:1"], token);
    }
}
