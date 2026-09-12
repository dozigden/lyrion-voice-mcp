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

    public async Task<BbcSubscriptions> ReadSubscriptionsAsync(CancellationToken cancellationToken)
    {
        if (!await menus.IsInstalledAsync(cancellationToken)) return new BbcSubscriptions(false, []);
        var player = await menus.SelectPlayerAsync(cancellationToken);
        var shows = await ReadShowsAsync(player, cancellationToken);
        return new BbcSubscriptions(true, shows.Select(x => x.Show).ToArray());
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
        await rpc.SendAsync(playerId, ["playlist", verb, episode.AudioUrl], cancellationToken);
        // LMS may reshuffle an append. Confirm an additional occurrence even when
        // its position changes; an older occurrence alone is never sufficient.
        var after = await ReadQueueAsync(playerId, cancellationToken);
        var expectedCount = command == ProviderPlaybackCommand.Load ? 1 : before.Items.Count + 1;
        var expectedOccurrences = command == ProviderPlaybackCommand.Load ? 1 : EpisodeOccurrences(before, target.EpisodeId) + 1;
        var shuffledAppend = command == ProviderPlaybackCommand.Add && before.ShuffleMode > 0;
        var positionMatches = expectedIndex >= 0 && expectedIndex < after.Items.Count
            && AudioIdentity(BbcSoundsMenuClient.Text(after.Items[expectedIndex], "url")) == target.EpisodeId;
        if (after.Items.Count != expectedCount || after.ShuffleMode != before.ShuffleMode
            || EpisodeOccurrences(after, target.EpisodeId) != expectedOccurrences
            || (!shuffledAppend && !positionMatches))
            throw new LmsRequestException("LMS did not confirm the submitted BBC Sounds episode in the queue.");
    }

    private static int EpisodeOccurrences(QueueSnapshot queue, string episodeId) =>
        queue.Items.Count(item => AudioIdentity(BbcSoundsMenuClient.Text(item, "url")) == episodeId);

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
        && AudioIdentity(target.AudioUrl) == target.EpisodeId;
    private static string? AudioIdentity(string? url)
    {
        var match = AudioUrl().Match(url ?? string.Empty);
        return match.Success ? match.Groups[2].Value : null;
    }
    [GeneratedRegex(@"^[A-Za-z0-9]+$", RegexOptions.CultureInvariant)]
    private static partial Regex Identifier();
    [GeneratedRegex(@"^sounds://_([A-Za-z0-9]+)_([A-Za-z0-9]+)(?:\?offset=\d+(?:\.\d+)?)?$", RegexOptions.CultureInvariant)]
    private static partial Regex AudioUrl();
    private sealed record ShowLocation(BbcShow Show, string Path);
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
