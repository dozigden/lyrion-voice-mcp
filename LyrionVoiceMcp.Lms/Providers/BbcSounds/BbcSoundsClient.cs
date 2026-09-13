using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using LyrionVoiceMcp.Abstractions;
using LyrionVoiceMcp.Abstractions.Providers;
using LyrionVoiceMcp.Abstractions.Providers.BbcSounds;
using Microsoft.Extensions.DependencyInjection;

namespace LyrionVoiceMcp.Lms.Providers.BbcSounds;

internal sealed partial class BbcSoundsClient(BbcSoundsMenuClient menus, LmsJsonRpcClient rpc) : IBbcSoundsClient
{
    private static JsonElement FindUnique(IReadOnlyList<JsonElement> items, Func<JsonElement, bool> predicate)
    {
        var matches = items.Where(predicate).Take(2).ToArray();
        return matches.Length == 1 ? matches[0] : throw BbcSoundsMenuClient.InvalidMenu();
    }

    private const string ContainerPrefix = "soundslist://_CONTAINER_";
    private const string StationsTitle = "Stations & Schedules";

    public async Task<BbcSubscriptions> ReadSubscriptionsAsync(CancellationToken cancellationToken)
    {
        if (!await menus.IsInstalledAsync(cancellationToken)) return new BbcSubscriptions(false, []);
        var player = await menus.SelectPlayerAsync(cancellationToken);
        var shows = await ReadShowsAsync(player, cancellationToken);
        return new BbcSubscriptions(true, shows.Select(x => x.Show).ToArray());
    }

    public async Task<BbcStations> ReadStationsAsync(CancellationToken cancellationToken)
    {
        if (!await menus.IsInstalledAsync(cancellationToken)) return new BbcStations(false, []);
        var player = await menus.SelectPlayerAsync(cancellationToken);
        var stations = await ReadStationLocationsAsync(player, cancellationToken);
        return stations is null
            ? new BbcStations(false, [])
            : new BbcStations(true, stations.Select(x => x.Station).ToArray());
    }

    private async Task<IReadOnlyList<ShowLocation>> ReadShowsAsync(string player, CancellationToken cancellationToken)
    {
        var root = await menus.ReadMenuAsync(player, null, cancellationToken);
        var mine = FindUnique(root, x => BbcSoundsMenuClient.FavouriteUrl(x) == "soundslist://_MYSOUNDS");
        if (mine.ValueKind == JsonValueKind.Undefined) throw BbcSoundsMenuClient.InvalidMenu();
        var myMenu = await menus.ReadMenuAsync(player, BbcSoundsMenuClient.BrowsePath(mine), cancellationToken);
        var subscribed = FindUnique(myMenu, x => BbcSoundsMenuClient.Title(x) == "Subscribed");
        if (subscribed.ValueKind == JsonValueKind.Undefined) throw BbcSoundsMenuClient.InvalidMenu();
        var shows = new List<ShowLocation>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        await foreach (var item in menus.ReadCollectionAsync(player, BbcSoundsMenuClient.BrowsePath(subscribed), cancellationToken))
        {
            var url = BbcSoundsMenuClient.FavouriteUrl(item);
            if (url is null || !url.StartsWith(ContainerPrefix, StringComparison.Ordinal)) throw BbcSoundsMenuClient.InvalidMenu();
            var id = url[ContainerPrefix.Length..];
            var title = BbcSoundsMenuClient.Title(item).Split('\n')[0].Trim();
            if (!Identifier().IsMatch(id) || title.Length is 0 or > 1024 || !ids.Add(id)
                || shows.Count >= BbcSoundsProvider.MaximumShows) throw BbcSoundsMenuClient.InvalidMenu();
            shows.Add(new ShowLocation(new BbcShow(id, title), BbcSoundsMenuClient.BrowsePath(item)));
        }
        return shows;
    }

    public async Task<BbcEpisodePage> BrowseEpisodesAsync(string showId, int offset, CancellationToken cancellationToken)
    {
        if (offset < 0 || offset > 10_000) throw BbcSoundsMenuClient.InvalidMenu();
        var episodes = new List<BbcEpisode>();
        var skipped = 0;
        await foreach (var episode in ReadEpisodesAsync(showId, cancellationToken))
        {
            if (skipped++ < offset) continue;
            episodes.Add(episode);
            if (episodes.Count == 51) break;
        }
        return new BbcEpisodePage(episodes.Take(50).ToArray(), episodes.Count > 50 ? offset + 50 : null);
    }

    public async Task<IReadOnlyList<BbcStationMenuItem>> BrowseStationAsync(
        string stationId, CancellationToken cancellationToken)
    {
        if (!StationIdentifier().IsMatch(stationId)) throw BbcSoundsMenuClient.InvalidMenu();
        if (!await menus.IsInstalledAsync(cancellationToken))
            throw new LmsRequestException("BBC Sounds stations are unavailable.");
        var player = await menus.SelectPlayerAsync(cancellationToken);
        var location = (await ReadStationLocationsAsync(player, cancellationToken))
            ?.SingleOrDefault(x => x.Station.Id == stationId)
            ?? throw new LmsRequestException("The BBC Sounds station is no longer available.");
        if (location.LiveOnly)
        {
            return [new BbcStationMenuItem(location.Station.Name, BrowseItemKind.Station, MediaTarget: new BbcAudioTarget(
                BbcAudioKind.Station, location.Station.Id, location.Station.LiveAudioUrl))];
        }
        if (location.Path is null) throw BbcSoundsMenuClient.InvalidMenu();
        var items = await menus.ReadMenuAsync(player, location.Path, cancellationToken);
        return MapStationMenu(stationId, items);
    }

    public async Task<IReadOnlyList<BbcStationMenuItem>> BrowseStationMenuAsync(
        BbcStationMenuTarget target, CancellationToken cancellationToken)
    {
        if (!StationIdentifier().IsMatch(target.StationId) || string.IsNullOrWhiteSpace(target.Path)
            || target.Path.Length > 4096 || !Enum.IsDefined(target.Level))
            throw BbcSoundsMenuClient.InvalidMenu();
        if (!await menus.IsInstalledAsync(cancellationToken))
            throw new LmsRequestException("BBC Sounds stations are unavailable.");
        var player = await menus.SelectPlayerAsync(cancellationToken);
        var items = await menus.ReadMenuAsync(player, target.Path, cancellationToken);
        return MapStationMenu(target.StationId, items, target);
    }

    private async Task<IReadOnlyList<StationLocation>?> ReadStationLocationsAsync(
        string player, CancellationToken cancellationToken)
    {
        var root = await menus.ReadMenuAsync(player, null, cancellationToken);
        var stationRoots = root.Where(x => BbcSoundsMenuClient.Title(x) == StationsTitle).Take(2).ToArray();
        if (stationRoots.Length == 1)
        {
            var locations = new List<StationLocation>();
            var ids = new HashSet<string>(StringComparer.Ordinal);
            await foreach (var item in menus.ReadCollectionAsync(
                               player, BbcSoundsMenuClient.BrowsePath(stationRoots[0]), cancellationToken))
            {
                if (locations.Count >= BbcSoundsProvider.MaximumStations) throw BbcSoundsMenuClient.InvalidMenu();
                var name = BbcSoundsMenuClient.Title(item).Split('\n')[0].Trim();
                var path = BbcSoundsMenuClient.BrowsePath(item);
                var stationMenu = await menus.ReadMenuAsync(player, path, cancellationToken);
                var liveUrls = stationMenu.Select(BbcSoundsMenuClient.FavouriteUrl)
                    .Where(url => LiveAudioIdentity(url) is not null).Take(2).ToArray();
                if (string.IsNullOrWhiteSpace(name) || name.Length > 1024 || liveUrls.Length != 1)
                    throw BbcSoundsMenuClient.InvalidMenu();
                var id = LiveAudioIdentity(liveUrls[0])!;
                if (!ids.Add(id)) throw BbcSoundsMenuClient.InvalidMenu();
                locations.Add(new StationLocation(new BbcStation(id, name, liveUrls[0]!), path, false));
            }
            return locations;
        }
        if (stationRoots.Length > 1) throw BbcSoundsMenuClient.InvalidMenu();

        // Outside the full Sounds experience the plugin can expose a live-only
        // root. Identify it structurally instead of assuming its editorial title.
        var liveCollections = new List<IReadOnlyList<StationLocation>>();
        foreach (var item in root.Where(x => BbcSoundsMenuClient.Text(x, "type") == "link"))
        {
            var child = await menus.ReadMenuAsync(
                player, BbcSoundsMenuClient.BrowsePath(item), cancellationToken);
            if (child.Count == 0 || child.Any(x => LiveAudioIdentity(BbcSoundsMenuClient.FavouriteUrl(x)) is null))
                continue;
            var ids = new HashSet<string>(StringComparer.Ordinal);
            var stations = child.Select(x =>
            {
                var name = BbcSoundsMenuClient.Title(x);
                var url = BbcSoundsMenuClient.FavouriteUrl(x)!;
                var id = LiveAudioIdentity(url)!;
                if (string.IsNullOrWhiteSpace(name) || name.Length > 1024 || !ids.Add(id))
                    throw BbcSoundsMenuClient.InvalidMenu();
                return new StationLocation(new BbcStation(id, name, url), null, true);
            }).ToArray();
            if (stations.Length > BbcSoundsProvider.MaximumStations) throw BbcSoundsMenuClient.InvalidMenu();
            liveCollections.Add(stations);
        }
        return liveCollections.Count switch
        {
            0 => null,
            1 => liveCollections[0],
            _ => throw BbcSoundsMenuClient.InvalidMenu()
        };
    }

    private static IReadOnlyList<BbcStationMenuItem> MapStationMenu(
        string stationId,
        IReadOnlyList<JsonElement> items,
        BbcStationMenuTarget? parent = null)
    {
        var level = parent?.Level ?? BbcStationMenuLevel.Station;
        var result = new List<BbcStationMenuItem>();
        foreach (var item in items)
        {
            var title = BbcSoundsMenuClient.Title(item);
            if (string.IsNullOrWhiteSpace(title) || title.Length > 4096) throw BbcSoundsMenuClient.InvalidMenu();
            var type = BbcSoundsMenuClient.Text(item, "type");
            if (type == "audio")
            {
                var target = CreateAudioTarget(BbcSoundsMenuClient.FavouriteUrl(item), parent?.Path);
                if (target is null) throw BbcSoundsMenuClient.InvalidMenu();
                var kind = target.Kind == BbcAudioKind.Station ? BrowseItemKind.Station : BrowseItemKind.Episode;
                result.Add(new BbcStationMenuItem(title, kind, MediaTarget: target));
                continue;
            }
            if (type is not ("link" or "playlist")) continue;
            if (level == BbcStationMenuLevel.PlayableActions)
            {
                var nextLevel = title switch
                {
                    "All Episodes" => BbcStationMenuLevel.Programmes,
                    "Tracklist" => BbcStationMenuLevel.AudioItems,
                    _ => (BbcStationMenuLevel?)null
                };
                if (nextLevel is null) continue;
                result.Add(LinkItem(title, BrowseItemKind.Category, stationId, item, nextLevel.Value));
                continue;
            }
            var itemKind = level == BbcStationMenuLevel.Programmes
                ? BrowseItemKind.Programme
                : BrowseItemKind.Category;
            var next = level switch
            {
                BbcStationMenuLevel.Station when title == "Full 30 Day Schedule" =>
                    BbcStationMenuLevel.ScheduleDays,
                BbcStationMenuLevel.Station => BbcStationMenuLevel.Programmes,
                BbcStationMenuLevel.ScheduleDays => BbcStationMenuLevel.Programmes,
                BbcStationMenuLevel.Programmes when BbcSoundsMenuClient.IsContinuation(title) =>
                    BbcStationMenuLevel.Programmes,
                BbcStationMenuLevel.Programmes => BbcStationMenuLevel.PlayableActions,
                _ => (BbcStationMenuLevel?)null
            };
            if (next is null) continue;
            if (BbcSoundsMenuClient.IsContinuation(title)) itemKind = BrowseItemKind.Category;
            var browseItem = LinkItem(title, itemKind, stationId, item, next.Value);
            var media = type == "playlist"
                ? CreateAudioTarget(BbcSoundsMenuClient.FavouriteUrl(item), browseItem.BrowseTarget?.Path)
                : null;
            result.Add(browseItem with { MediaTarget = media });
        }
        return result;
    }

    private static BbcStationMenuItem LinkItem(
        string title, BrowseItemKind kind, string stationId, JsonElement item, BbcStationMenuLevel level) =>
        new(title, kind, new BbcStationMenuTarget(stationId, BbcSoundsMenuClient.BrowsePath(item), level));

    private static BbcAudioTarget? CreateAudioTarget(string? url, string? validationPath)
    {
        if (LiveAudioIdentity(url) is { } stationId)
            return new BbcAudioTarget(BbcAudioKind.Station, stationId, url!, validationPath);
        if (RewindAudioIdentity(url) is { } rewindId)
            return new BbcAudioTarget(BbcAudioKind.Rewind, rewindId, url!, validationPath);
        var identity = EpisodeAudioIdentity(url);
        return identity is null ? null : new BbcAudioTarget(BbcAudioKind.Episode, identity, url!, validationPath);
    }

    private async IAsyncEnumerable<BbcEpisode> ReadEpisodesAsync(string showId,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var player = await menus.SelectPlayerAsync(cancellationToken);
        var shows = await ReadShowsAsync(player, cancellationToken);
        var show = shows.SingleOrDefault(x => x.Show.Id == showId)
            ?? throw new LmsRequestException("The BBC Sounds show is no longer available in subscriptions.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        await foreach (var item in menus.ReadCollectionAsync(player, show.Path, cancellationToken))
        {
            var title = BbcSoundsMenuClient.Title(item);
            var url = BbcSoundsMenuClient.FavouriteUrl(item);
            if (url is null || !AudioUrl().IsMatch(url))
            {
                if (BbcSoundsMenuClient.Text(item, "type") != "link") throw BbcSoundsMenuClient.InvalidMenu();
                var detail = await menus.ReadMenuAsync(player, BbcSoundsMenuClient.BrowsePath(item), cancellationToken);
                // Opening the episode reveals actions. Inspect only audio leaves;
                // never navigate any links within an episode's action menu.
                var audio = detail.FirstOrDefault(x => BbcSoundsMenuClient.Text(x, "type") == "audio"
                    && AudioUrl().IsMatch(BbcSoundsMenuClient.FavouriteUrl(x) ?? string.Empty));
                if (audio.ValueKind == JsonValueKind.Undefined) continue;
                url = BbcSoundsMenuClient.FavouriteUrl(audio)!;
            }
            var match = AudioUrl().Match(url);
            var id = match.Groups[2].Value;
            if (!ids.Add(id)) throw BbcSoundsMenuClient.InvalidMenu();
            if (string.IsNullOrWhiteSpace(title) || title.Length > 4096) throw BbcSoundsMenuClient.InvalidMenu();
            yield return new BbcEpisode(id, title, url);
        }
    }

    public async Task<bool> IsAvailableAsync(BbcEpisodeTarget target, CancellationToken cancellationToken)
    {
        if (!ValidTarget(target)) return false;
        return await ResolveAsync(target, cancellationToken) is not null;
    }

    public async Task<bool> IsAvailableAsync(BbcAudioTarget target, CancellationToken cancellationToken)
    {
        if (!ValidTarget(target)) return false;
        if (target.Kind == BbcAudioKind.Station)
        {
            var stations = await ReadStationsAsync(cancellationToken);
            return stations.Available && stations.Stations.Any(x => x.Id == target.Id && x.LiveAudioUrl == target.AudioUrl);
        }
        if (target.ValidationPath is null) return false;
        var player = await menus.SelectPlayerAsync(cancellationToken);
        var items = await menus.ReadMenuAsync(player, target.ValidationPath, cancellationToken);
        return items.Select(BbcSoundsMenuClient.FavouriteUrl)
            .Any(url => AudioIdentity(url) == AudioIdentity(target.AudioUrl));
    }

    private async Task<BbcEpisode?> ResolveAsync(BbcEpisodeTarget target, CancellationToken cancellationToken)
    {
        await foreach (var episode in ReadEpisodesAsync(target.ShowId, cancellationToken))
        {
            if (episode.Id == target.EpisodeId) return episode;
        }
        return null;
    }

    public async Task SubmitAsync(string playerId, BbcEpisodeTarget target,
        ProviderPlaybackCommand command, CancellationToken cancellationToken)
    {
        if (!ValidTarget(target)) throw new LmsRequestException("The BBC Sounds audio reference is invalid.");
        var episode = await ResolveAsync(target, cancellationToken)
            ?? throw new LmsRequestException("The BBC Sounds episode is no longer available.");
        await SubmitAsync(playerId, episode.AudioUrl, AudioIdentity(episode.AudioUrl)!, command, cancellationToken);
    }

    public async Task SubmitAsync(string playerId, BbcAudioTarget target,
        ProviderPlaybackCommand command, CancellationToken cancellationToken)
    {
        if (!await IsAvailableAsync(target, cancellationToken))
            throw new LmsRequestException("The BBC Sounds audio item is no longer available.");
        await SubmitAsync(playerId, target.AudioUrl, AudioIdentity(target.AudioUrl)!, command, cancellationToken);
    }

    private async Task SubmitAsync(string playerId, string audioUrl, string audioIdentity,
        ProviderPlaybackCommand command, CancellationToken cancellationToken)
    {
        var verb = command switch
        {
            ProviderPlaybackCommand.Load => "play",
            ProviderPlaybackCommand.Add => "add",
            ProviderPlaybackCommand.Insert => "insert",
            _ => throw new ArgumentOutOfRangeException(nameof(command))
        };
        var before = await ReadQueueAsync(playerId, cancellationToken);
        if (command != ProviderPlaybackCommand.Load && before.Items.Count >= 300)
            throw new LmsRequestException("The LMS queue is full.");
        if (command == ProviderPlaybackCommand.Insert && before.Items.Count > 0
            && (before.CurrentIndex is null or < 0 || before.CurrentIndex >= before.Items.Count))
            throw new LmsRequestException("LMS did not identify the current queue position.");
        var expectedIndex = command switch
        {
            ProviderPlaybackCommand.Load => 0,
            ProviderPlaybackCommand.Add => before.Items.Count,
            ProviderPlaybackCommand.Insert => before.Items.Count == 0 ? 0 : (before.CurrentIndex ?? -1) + 1,
            _ => throw new ArgumentOutOfRangeException(nameof(command))
        };
        await rpc.SendAsync(playerId, ["playlist", verb, audioUrl], cancellationToken);
        // LMS may reshuffle an append. Confirm an additional occurrence even when
        // its position changes; an older occurrence alone is never sufficient.
        var after = await ReadQueueAsync(playerId, cancellationToken);
        var expectedCount = command == ProviderPlaybackCommand.Load ? 1 : before.Items.Count + 1;
        var expectedOccurrences = command == ProviderPlaybackCommand.Load ? 1 : AudioOccurrences(before, audioIdentity) + 1;
        var shuffledAppend = command == ProviderPlaybackCommand.Add && before.ShuffleMode > 0;
        var positionMatches = expectedIndex >= 0 && expectedIndex < after.Items.Count
            && AudioIdentity(BbcSoundsMenuClient.Text(after.Items[expectedIndex], "url")) == audioIdentity;
        if (after.Items.Count != expectedCount || after.ShuffleMode != before.ShuffleMode
            || AudioOccurrences(after, audioIdentity) != expectedOccurrences
            || (!shuffledAppend && !positionMatches))
            throw new LmsRequestException("LMS did not confirm the submitted BBC Sounds item in the queue.");
    }

    private static int AudioOccurrences(QueueSnapshot queue, string identity) =>
        queue.Items.Count(item => AudioIdentity(BbcSoundsMenuClient.Text(item, "url")) == identity);

    private async Task<QueueSnapshot> ReadQueueAsync(string playerId, CancellationToken cancellationToken)
    {
        var queue = await rpc.SendAsync(playerId, ["status", 0, 300, "tags:u"], cancellationToken);
        var count = LmsJson.ReadInt(queue, "playlist_tracks");
        if (count is null or < 0 or > 300) throw new LmsRequestException("LMS returned an invalid queue count.");
        var shuffle = LmsJson.ReadInt(queue, "playlist shuffle");
        if (shuffle is null or < 0 or > 2) throw new LmsRequestException("LMS returned an invalid shuffle mode.");
        if (!queue.TryGetProperty("playlist_loop", out var items))
            return count == 0 ? new QueueSnapshot([], null, shuffle.Value) : throw BbcSoundsMenuClient.InvalidMenu();
        if (items.ValueKind != JsonValueKind.Array || items.GetArrayLength() != count)
            throw new LmsRequestException("LMS returned an incomplete queue.");
        var index = LmsJson.ReadInt(queue, "playlist_cur_index");
        return new QueueSnapshot(items.EnumerateArray().ToArray(), index, shuffle.Value);
    }

    private sealed record QueueSnapshot(IReadOnlyList<JsonElement> Items, int? CurrentIndex, int ShuffleMode);

    private static bool ValidTarget(BbcEpisodeTarget target) => Identifier().IsMatch(target.ShowId)
        && EpisodeAudioIdentity(target.AudioUrl) == target.EpisodeId;
    private static bool ValidTarget(BbcAudioTarget target) => Enum.IsDefined(target.Kind)
        && !string.IsNullOrWhiteSpace(target.Id)
        && target.Id == (target.Kind switch
        {
            BbcAudioKind.Station => LiveAudioIdentity(target.AudioUrl),
            BbcAudioKind.Episode => EpisodeAudioIdentity(target.AudioUrl),
            BbcAudioKind.Rewind => RewindAudioIdentity(target.AudioUrl),
            _ => null
        });
    private static string? AudioIdentity(string? url)
    {
        if (LiveAudioIdentity(url) is { } station) return $"station:{station}";
        if (RewindAudioUrl().Match(url ?? string.Empty) is { Success: true } rewind)
            return $"rewind:{rewind.Groups[1].Value}:{rewind.Groups[2].Value}";
        return EpisodeAudioIdentity(url) is { } episode ? $"episode:{episode}" : null;
    }
    private static string? LiveAudioIdentity(string? url)
    {
        var match = LiveAudioUrl().Match(url ?? string.Empty);
        return match.Success ? match.Groups[1].Value : null;
    }
    private static string? RewindAudioIdentity(string? url)
    {
        var match = RewindAudioUrl().Match(url ?? string.Empty);
        return match.Success ? $"{match.Groups[1].Value}:{match.Groups[2].Value}" : null;
    }
    private static string? EpisodeAudioIdentity(string? url)
    {
        var match = AudioUrl().Match(url ?? string.Empty);
        return match.Success ? match.Groups[2].Value : null;
    }
    [GeneratedRegex(@"^[A-Za-z0-9]+$", RegexOptions.CultureInvariant)]
    private static partial Regex Identifier();
    [GeneratedRegex(@"^sounds://_([A-Za-z0-9]+)_([A-Za-z0-9]+)(?:\?offset=\d+(?:\.\d+)?)?$", RegexOptions.CultureInvariant)]
    private static partial Regex AudioUrl();
    [GeneratedRegex(@"^sounds://_LIVE_([A-Za-z0-9_-]+)$", RegexOptions.CultureInvariant)]
    private static partial Regex LiveAudioUrl();
    [GeneratedRegex(@"^sounds://_REWIND_([0-9]+)_LIVE_([A-Za-z0-9_-]+)$", RegexOptions.CultureInvariant)]
    private static partial Regex RewindAudioUrl();
    [GeneratedRegex(@"^[A-Za-z0-9_-]+$", RegexOptions.CultureInvariant)]
    private static partial Regex StationIdentifier();
    private sealed record ShowLocation(BbcShow Show, string Path);
    private sealed record StationLocation(BbcStation Station, string? Path, bool LiveOnly);
}

internal static class BbcSoundsLmsRegistration
{
    public static IServiceCollection AddBbcSoundsLms(this IServiceCollection services)
    {
        services.AddTransient<BbcSoundsMenuClient>();
        services.AddTransient<IBbcSoundsClient, BbcSoundsClient>();
        return services;
    }
}
