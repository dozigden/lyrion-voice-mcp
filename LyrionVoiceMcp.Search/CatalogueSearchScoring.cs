using static LyrionVoiceMcp.Search.TextMatchScorer;
using LyrionVoiceMcp.Abstractions;

namespace LyrionVoiceMcp.Search;

internal static class CatalogueSearchRanker
{
    internal static IReadOnlyList<RankedPhuzzyCandidate> RankCandidates(
        string query,
        IReadOnlyList<PhuzzyCandidate> candidates,
        bool includeUnmatched,
        bool captureEvidence,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var queryForms = PhuzzyTextForms.Create(query);
        if (queryForms.Normalised.Length == 0)
        {
            return [];
        }

        var spans = CreateQuerySpans(query, queryForms.Tokens);
        var ranked = new List<RankedPhuzzyCandidate>(candidates.Count);
        for (var index = 0; index < candidates.Count; index++)
        {
            if ((index & 255) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            var candidate = candidates[index];
            var score = Score(
                candidate,
                spans,
                queryForms.Tokens.Count,
                captureEvidence);
            if (score.Score > 0 || includeUnmatched)
            {
                ranked.Add(new RankedPhuzzyCandidate(
                    candidate,
                    score.Score,
                    score.Evidence,
                    score.MatchSignal));
            }
        }

        return ranked
            .OrderByDescending(item => item.Score)
            .ThenBy(item => KindOrder(item.Candidate.Source.Value.Kind))
            .ThenBy(item => item.Candidate.Source.Value.Title, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Candidate.Source.StableKey, StringComparer.Ordinal)
            .ToArray();
    }

    internal static PhuzzyCandidate CreateCandidate(
        CatalogueIndexCandidate candidate) =>
        CreateCandidate(candidate, new Dictionary<string, PhuzzyTextForms>(StringComparer.Ordinal));

    private static PhuzzyCandidate CreateCandidate(
        CatalogueIndexCandidate candidate,
        Dictionary<string, PhuzzyTextForms> cache)
    {
        var title = GetForms(candidate.Value.Title, cache);
        var artist = GetForms(candidate.Value.Artist, cache);
        var album = GetForms(candidate.Value.Album, cache);
        var combined = PhuzzyTextForms.Create(CatalogueSearchText.Join(
            candidate.Value.Title,
            candidate.Value.Artist ?? string.Empty,
            candidate.Value.Album ?? string.Empty));
        return new PhuzzyCandidate(candidate, title, artist, album, combined);
    }

    private static PhuzzyTextForms GetForms(
        string? value,
        Dictionary<string, PhuzzyTextForms> cache)
    {
        var key = value ?? string.Empty;
        if (!cache.TryGetValue(key, out var forms))
        {
            forms = PhuzzyTextForms.Create(value);
            cache.Add(key, forms);
        }

        return forms;
    }

    private static CandidateScore Score(
        PhuzzyCandidate candidate,
        IReadOnlyList<QuerySpan> spans,
        int queryTokenCount,
        bool captureEvidence)
    {
        FieldScoreResult? best = null;
        SelectBest(FieldScore("title", candidate.Title, spans, queryTokenCount, 0), ref best);
        SelectBest(FieldScore("artist", candidate.Artist, spans, queryTokenCount, 180), ref best);
        SelectBest(FieldScore("album", candidate.Album, spans, queryTokenCount, 240), ref best);
        SelectBest(FieldScore("combined", candidate.Combined, spans, queryTokenCount, 100), ref best);
        if (best is null)
        {
            return new CandidateScore(0, null, null);
        }

        var value = best.Value;
        var evidence = captureEvidence
            ? new SearchScoreEvidence(
                value.Field,
                value.Signal,
                value.QuerySpan,
                value.MatchedTokenCount,
                value.IgnoredTokenCount,
                value.SignalScore,
                value.FieldPenalty,
                value.CoveragePenalty,
                value.FinalScore)
            : null;
        return new CandidateScore(value.FinalScore, evidence, value.Signal);
    }

    private static int KindOrder(MediaEntityKind kind) => kind switch
    {
        MediaEntityKind.Artist => 0,
        MediaEntityKind.Album => 1,
        MediaEntityKind.Track => 2,
        MediaEntityKind.Playlist => 3,
        _ => 4
    };

    private readonly record struct CandidateScore(
        int Score,
        SearchScoreEvidence? Evidence,
        string? MatchSignal);


}

internal sealed record PhuzzyCandidate(
    CatalogueIndexCandidate Source,
    PhuzzyTextForms Title,
    PhuzzyTextForms Artist,
    PhuzzyTextForms Album,
    PhuzzyTextForms Combined);
