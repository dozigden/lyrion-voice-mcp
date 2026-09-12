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
        var entries = shows.Select(show => new Entry(show, PhuzzyTextForms.Create(show.Title))).ToArray();
        Volatile.Write(ref snapshot, new Snapshot(subscriptions.Available, Array.AsReadOnly(shows), entries));
    }

    public IReadOnlyList<BbcShowMatch> Search(string name, CancellationToken cancellationToken)
    {
        var current = Volatile.Read(ref snapshot);
        if (!current.Available) return [];
        cancellationToken.ThrowIfCancellationRequested();
        var query = BbcProgrammeQuery.Create(name);
        if (query.Forms.Tokens.Count == 0) return [];
        var matches = new List<(Entry Entry, int Score, string Signal)>();
        var titleForms = new Dictionary<string, PhuzzyTextForms>(StringComparer.Ordinal);
        foreach (var entry in current.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var match = Match(entry, query, titleForms, cancellationToken);
            if (match.Score > 0) matches.Add((entry, match.Score, match.Signal));
        }
        return matches.OrderByDescending(x => x.Score)
            .ThenBy(x => x.Entry.Show.Title, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Entry.Show.Id, StringComparer.Ordinal).Take(5)
            .Select(x => new BbcShowMatch(x.Entry.Show, x.Signal)).ToArray();
    }

    private static (int Score, string Signal) Match(Entry entry, BbcProgrammeQuery query,
        Dictionary<string, PhuzzyTextForms> titleForms, CancellationToken cancellationToken)
    {
        if (entry.Forms.Normalised == query.Forms.Normalised) return (1300, "exact_normalised");
        if (query.NameInterpretation is { Forms.Tokens.Count: 0 }) return (0, string.Empty);
        var best = ScoreName(entry, query, titleForms, cancellationToken);
        if (query.NameInterpretation is { } name)
        {
            var alternative = ScoreName(entry, name, titleForms, cancellationToken);
            if (alternative.Score - 80 > best.Score)
                best = (alternative.Score - 80, $"programme_context_{alternative.Signal}");
        }
        return best;
    }

    private static (int Score, string Signal) ScoreName(Entry entry, BbcProgrammeQuery query,
        Dictionary<string, PhuzzyTextForms> titleForms, CancellationToken cancellationToken)
    {
        var tokens = entry.Forms.Tokens;
        var queryTokens = query.Forms.Tokens;
        (int Score, string Signal) best = (0, string.Empty);
        if (entry.Forms.Normalised == query.Forms.Normalised) return (1300, "exact_normalised");
        var remaining = tokens.ToList();
        if (queryTokens.All(remaining.Remove)) best = (1120, "complete_title_tokens");

        // Compare the complete name with equally sized title spans. Forms are
        // cached only for this search, keeping the published index linear in titles.
        for (var start = 0; queryTokens.Count < tokens.Count && start <= tokens.Count - queryTokens.Count; start++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var text = string.Join(' ', tokens.Skip(start).Take(queryTokens.Count));
            if (!titleForms.TryGetValue(text, out var forms))
            {
                // A title fragment must not manufacture an equivalence that was
                // ineligible in the original title's full context.
                forms = PhuzzyTextForms.Create(text) with { IndexedEquivalenceForms = [] };
                if (titleForms.Count < 4096) titleForms.Add(text, forms);
            }
            var span = TextMatchScorer.SpanScore(forms, query.Forms);
            if (span is null || span.Value.Score - 160 <= best.Score) continue;
            var signal = span.Value.Name == "exact_normalised" ? "complete_title_span" : $"title_span_{span.Value.Name}";
            best = (span.Value.Score - 160, signal);
        }
        var fullTitle = TextMatchScorer.FieldScore("title", entry.Forms, query.Spans, queryTokens.Count, 0);
        if (fullTitle is { } match && match.FinalScore > best.Score) best = (match.FinalScore, match.Signal);
        return best;
    }

    private sealed record Entry(BbcShow Show, PhuzzyTextForms Forms);
    private sealed record Snapshot(bool Available, IReadOnlyList<BbcShow> Shows, Entry[] Entries);
}

internal static class BbcSoundsSearchRegistration
{
    public static IServiceCollection AddBbcSoundsSearch(this IServiceCollection services) =>
        services.AddSingleton<IBbcSubscriptionIndex, BbcSubscriptionIndex>();
}
