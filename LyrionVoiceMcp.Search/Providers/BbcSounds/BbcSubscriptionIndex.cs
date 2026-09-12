using LyrionVoiceMcp.Abstractions.Providers.BbcSounds;
using Microsoft.Extensions.DependencyInjection;

namespace LyrionVoiceMcp.Search.Providers.BbcSounds;

internal sealed class BbcSubscriptionIndex : IBbcSubscriptionIndex
{
    private Snapshot snapshot = new(false, [], []);
    public bool Available => Volatile.Read(ref snapshot).Available;
    public IReadOnlyList<BbcShow> Shows => Volatile.Read(ref snapshot).Shows;

    public void Publish(BbcSubscriptions subscriptions)
    {
        if (subscriptions.Shows.Count > BbcSoundsProvider.MaximumShows)
            throw new InvalidOperationException("The subscription index exceeds its limit.");
        var shows = subscriptions.Shows.OrderBy(x => x.Title, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Id, StringComparer.Ordinal).ToArray();
        var entries = shows.Select(show =>
        {
            var title = CatalogueSearchText.Normalise(show.Title);
            return new Entry(show, title, CatalogueSearchText.SplitTokens(title)
                .Select(token => PhuzzyTextForms.Create(token)).ToArray());
        }).ToArray();
        Volatile.Write(ref snapshot, new Snapshot(subscriptions.Available, Array.AsReadOnly(shows), entries));
    }

    public IReadOnlyList<BbcShowMatch> Search(string name, CancellationToken cancellationToken)
    {
        var current = Volatile.Read(ref snapshot);
        if (!current.Available) return [];
        var normalised = CatalogueSearchText.Normalise(name);
        var query = CatalogueSearchText.SplitTokens(normalised).Select(PhuzzyTextForms.Create).ToArray();
        if (query.Length == 0) return [];
        var matches = new List<(Entry Entry, int Score, string Signal)>();
        foreach (var entry in current.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var (score, signal) = Score(entry, normalised, query);
            if (score > 0) matches.Add((entry, score, signal));
        }
        return matches.OrderByDescending(x => x.Score)
            .ThenBy(x => x.Entry.Show.Title, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Entry.Show.Id, StringComparer.Ordinal).Take(5)
            .Select(x => new BbcShowMatch(x.Entry.Show, x.Signal)).ToArray();
    }

    private static (int Score, string Signal) Score(Entry entry, string normalised, PhuzzyTextForms[] query)
    {
        if (entry.Title == normalised) return (4000, "exact_normalised");
        for (var start = 0; start <= entry.Tokens.Length - query.Length; start++)
        {
            if (query.Select((token, i) => token.Normalised == entry.Tokens[start + i].Normalised).All(x => x))
                return (3000, "complete_title_span");
        }
        var remaining = entry.Tokens.Select(x => x.Normalised).ToList();
        if (query.All(token => remaining.Remove(token.Normalised))) return (2000, "complete_title_tokens");

        // Every query word must match a contiguous title window. Extra query words
        // cannot disappear merely because one name has strong phonetic evidence.
        var best = 0;
        for (var start = 0; start <= entry.Tokens.Length - query.Length; start++)
        {
            var score = 1000;
            for (var i = 0; i < query.Length; i++)
            {
                var left = query[i];
                var right = entry.Tokens[start + i];
                if (left.Normalised == right.Normalised) continue;
                var limit = left.Normalised.Length < 5 ? 1 : 2;
                if (left.Normalised.Length >= 3 && right.Normalised.Length >= 3
                    && CatalogueSearchRanker.BoundedEditDistance(left.Normalised, right.Normalised, limit) <= limit)
                {
                    score -= 40;
                    continue;
                }
                if (left.Normalised.Length >= 4 && right.Normalised.Length >= 4
                    && left.DoubleMetaphoneCodes.Overlaps(right.DoubleMetaphoneCodes))
                {
                    score -= 80;
                    continue;
                }
                score = 0;
                break;
            }
            best = Math.Max(best, score);
        }
        return (best, "tolerant_title_span");
    }

    private sealed record Entry(BbcShow Show, string Title, PhuzzyTextForms[] Tokens);
    private sealed record Snapshot(bool Available, IReadOnlyList<BbcShow> Shows, Entry[] Entries);
}

internal static class BbcSoundsSearchRegistration
{
    public static IServiceCollection AddBbcSoundsSearch(this IServiceCollection services) =>
        services.AddSingleton<IBbcSubscriptionIndex, BbcSubscriptionIndex>();
}
