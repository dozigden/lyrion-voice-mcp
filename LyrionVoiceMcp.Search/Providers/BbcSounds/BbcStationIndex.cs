using LyrionVoiceMcp.Abstractions.Providers.BbcSounds;

namespace LyrionVoiceMcp.Search.Providers.BbcSounds;

internal sealed class BbcStationIndex : IBbcStationIndex
{
    private Snapshot snapshot = new(false, [], []);
    public bool Available => Volatile.Read(ref snapshot).Available;
    public IReadOnlyList<BbcStation> Stations => Volatile.Read(ref snapshot).Stations;

    public void Publish(BbcStations stations)
    {
        if (stations.Stations.Count > BbcSoundsProvider.MaximumStations)
            throw new InvalidOperationException("The BBC station index exceeds its limit.");
        var ordered = stations.Stations.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Id, StringComparer.Ordinal).ToArray();
        var entries = ordered.Select(station => new Entry(station, PhuzzyTextForms.Create(station.Name))).ToArray();
        Volatile.Write(ref snapshot, new Snapshot(stations.Available, Array.AsReadOnly(ordered), entries));
    }

    public IReadOnlyList<BbcStationMatch> Search(string name, CancellationToken cancellationToken)
    {
        var current = Volatile.Read(ref snapshot);
        if (!current.Available || string.IsNullOrWhiteSpace(name)) return [];
        cancellationToken.ThrowIfCancellationRequested();
        var query = PhuzzyTextForms.Create(name);
        if (query.Tokens.Count == 0) return [];
        var spans = TextMatchScorer.CreateQuerySpans(name, query.Tokens);
        var matches = new List<(Entry Entry, int Score, string Signal)>();
        var titleForms = new Dictionary<string, PhuzzyTextForms>(StringComparer.Ordinal);
        foreach (var entry in current.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var match = Score(entry, query, spans, titleForms, cancellationToken);
            if (match.Score > 0) matches.Add((entry, match.Score, match.Signal));
        }
        return matches.OrderByDescending(x => x.Score)
            .ThenBy(x => x.Entry.Station.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Entry.Station.Id, StringComparer.Ordinal)
            .Take(BbcSoundsProvider.StationSearchLimit)
            .Select(x => new BbcStationMatch(x.Entry.Station, x.Signal)).ToArray();
    }

    private static (int Score, string Signal) Score(
        Entry entry,
        PhuzzyTextForms query,
        IReadOnlyList<TextMatchScorer.QuerySpan> spans,
        Dictionary<string, PhuzzyTextForms> titleForms,
        CancellationToken cancellationToken)
    {
        if (entry.Forms.Normalised == query.Normalised) return (1300, "exact_normalised");
        (int Score, string Signal) best = (0, string.Empty);
        var remaining = entry.Forms.Tokens.ToList();
        if (query.Tokens.All(remaining.Remove)) best = (1120, "complete_title_tokens");
        for (var start = 0; query.Tokens.Count < entry.Forms.Tokens.Count
             && start <= entry.Forms.Tokens.Count - query.Tokens.Count; start++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var text = string.Join(' ', entry.Forms.Tokens.Skip(start).Take(query.Tokens.Count));
            if (!titleForms.TryGetValue(text, out var forms))
            {
                forms = PhuzzyTextForms.Create(text) with { IndexedEquivalenceForms = [] };
                if (titleForms.Count < 4096) titleForms.Add(text, forms);
            }
            var span = TextMatchScorer.SpanScore(forms, query);
            if (span is null || span.Value.Score - 160 <= best.Score) continue;
            var signal = span.Value.Name == "exact_normalised"
                ? "complete_title_span"
                : $"title_span_{span.Value.Name}";
            best = (span.Value.Score - 160, signal);
        }
        var fullTitle = TextMatchScorer.FieldScore(
            "title", entry.Forms, spans, query.Tokens.Count, 0);
        if (fullTitle is { } match && match.FinalScore > best.Score)
            best = (match.FinalScore, match.Signal);
        return best;
    }

    private sealed record Entry(BbcStation Station, PhuzzyTextForms Forms);
    private sealed record Snapshot(bool Available, IReadOnlyList<BbcStation> Stations, Entry[] Entries);
}
