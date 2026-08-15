using LyrionVoiceMcp.Abstractions;

namespace LyrionVoiceMcp.Evaluation;

public sealed class CatalogueLexicalSearchResolver : IEvaluationSearchResolver
{
    private const int ResultLimit = 20;
    private readonly IReadOnlyList<CatalogueEvaluationCandidate> candidates;

    private CatalogueLexicalSearchResolver(CatalogueEvaluationIndex index)
    {
        candidates = index.Candidates;
        Metrics = new EvaluationResolverMetrics(
            candidates.Count,
            index.PreparationDurationMilliseconds,
            null);
    }

    public string Name => "catalogue-lexical-fuzzy";
    public string Version => "1";
    public EvaluationResolverMetrics Metrics { get; }

    public static async Task<CatalogueLexicalSearchResolver> CreateAsync(
        string databasePath,
        CancellationToken cancellationToken) =>
        new(await CatalogueEvaluationIndex.LoadAsync(databasePath, cancellationToken));

    public Task<EvaluationSearchResponse> SearchAsync(
        string query,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var normalisedQuery = CatalogueEvaluationText.Normalise(query);
        if (normalisedQuery.Length == 0)
        {
            return Task.FromResult(new EvaluationSearchResponse([], null));
        }

        var queryTokens = CatalogueEvaluationText.SplitTokens(normalisedQuery);
        var scored = new List<ScoredCandidate>();
        for (var index = 0; index < candidates.Count; index++)
        {
            if ((index & 255) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            var candidate = candidates[index];
            var score = Score(candidate, normalisedQuery, queryTokens);
            if (score > 0)
            {
                scored.Add(new ScoredCandidate(candidate, score));
            }
        }

        var results = scored
            .OrderByDescending(item => item.Score)
            .ThenBy(item => KindOrder(item.Candidate.Value.Kind))
            .ThenBy(item => item.Candidate.Value.Title, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Candidate.StableKey, StringComparer.Ordinal)
            .Take(ResultLimit)
            .Select(item => item.Candidate.Value)
            .ToArray();
        return Task.FromResult(new EvaluationSearchResponse(results, null));
    }

    private static int Score(
        CatalogueEvaluationCandidate candidate,
        string query,
        IReadOnlyList<string> queryTokens)
    {
        var score = FieldScore(query, queryTokens, candidate.Title);
        score = Math.Max(score, FieldScore(query, queryTokens, candidate.Artist) - 180);
        score = Math.Max(score, FieldScore(query, queryTokens, candidate.Album) - 240);
        if (ContainsTokens(candidate.Combined, queryTokens))
        {
            score = Math.Max(score, 760);
        }

        return Math.Max(0, score);
    }

    private static int FieldScore(
        string query,
        IReadOnlyList<string> queryTokens,
        string field)
    {
        if (field.Length == 0)
        {
            return 0;
        }

        if (string.Equals(query, field, StringComparison.Ordinal))
        {
            return 1_000;
        }

        var fieldTokens = CatalogueEvaluationText.SplitTokens(field);
        if (SameTokens(queryTokens, fieldTokens))
        {
            return 950;
        }

        if (field.StartsWith(query, StringComparison.Ordinal))
        {
            return 900;
        }

        if (ContainsTokens(field, queryTokens))
        {
            return 820;
        }

        var threshold = EditDistanceThreshold(Math.Max(query.Length, field.Length));
        if (Math.Abs(query.Length - field.Length) > threshold)
        {
            return 0;
        }

        var distance = BoundedEditDistance(query, field, threshold);
        return distance <= threshold ? 760 - (distance * 30) : 0;
    }

    private static bool SameTokens(
        IReadOnlyList<string> left,
        IReadOnlyList<string> right) =>
        left.Count == right.Count
        && left.Order(StringComparer.Ordinal).SequenceEqual(
            right.Order(StringComparer.Ordinal),
            StringComparer.Ordinal);

    private static bool ContainsTokens(
        string field,
        IReadOnlyList<string> queryTokens)
    {
        var fieldTokens = CatalogueEvaluationText.SplitTokens(field);
        return queryTokens.Count > 0
            && queryTokens.All(queryToken => fieldTokens.Any(fieldToken =>
                string.Equals(queryToken, fieldToken, StringComparison.Ordinal)
                || fieldToken.StartsWith(queryToken, StringComparison.Ordinal)));
    }

    private static int EditDistanceThreshold(int length) => length switch
    {
        <= 4 => 1,
        <= 8 => 2,
        <= 16 => 3,
        _ => 4
    };

    private static int BoundedEditDistance(string left, string right, int limit)
    {
        var previous = Enumerable.Range(0, right.Length + 1).ToArray();
        var current = new int[right.Length + 1];
        for (var leftIndex = 1; leftIndex <= left.Length; leftIndex++)
        {
            current[0] = leftIndex;
            var rowMinimum = current[0];
            for (var rightIndex = 1; rightIndex <= right.Length; rightIndex++)
            {
                var substitution = previous[rightIndex - 1]
                    + (left[leftIndex - 1] == right[rightIndex - 1] ? 0 : 1);
                current[rightIndex] = Math.Min(
                    Math.Min(previous[rightIndex] + 1, current[rightIndex - 1] + 1),
                    substitution);
                rowMinimum = Math.Min(rowMinimum, current[rightIndex]);
            }

            if (rowMinimum > limit)
            {
                return limit + 1;
            }

            (previous, current) = (current, previous);
        }

        return previous[right.Length];
    }

    private static int KindOrder(MediaEntityKind kind) => kind switch
    {
        MediaEntityKind.Artist => 0,
        MediaEntityKind.Album => 1,
        MediaEntityKind.Track => 2,
        MediaEntityKind.Playlist => 3,
        _ => 4
    };

    private sealed record ScoredCandidate(CatalogueEvaluationCandidate Candidate, int Score);
}
