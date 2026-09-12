namespace LyrionVoiceMcp.Search;

internal static class TextMatchScorer
{
    private const int CoveragePenaltyPerIgnoredToken = 320;

    internal static IReadOnlyList<QuerySpan> CreateQuerySpans(
        string query,
        IReadOnlyList<string> tokens)
        => SearchTextEquivalences.CreateQuerySpans(query, tokens)
            .Select(span => new QuerySpan(
                PhuzzyTextForms.Create(span.Text) with
                {
                    QueryEquivalenceForms = span.Forms
                },
                span.TokenCount))
            .ToArray();

    internal static void SelectBest(
        FieldScoreResult? candidate,
        ref FieldScoreResult? best)
    {
        if (candidate is not null
            && candidate.Value.FinalScore > (best?.FinalScore ?? 0))
        {
            best = candidate;
        }
    }

    internal static FieldScoreResult? FieldScore(
        string fieldName,
        PhuzzyTextForms field,
        IReadOnlyList<QuerySpan> spans,
        int queryTokenCount,
        int fieldPenalty)
    {
        if (field.Normalised.Length == 0)
        {
            return null;
        }

        FieldScoreResult? best = null;
        foreach (var span in spans)
        {
            var signal = SpanScore(field, span.Forms);
            if (signal is null)
            {
                continue;
            }

            var signalValue = signal.Value;
            var ignoredTokenCount = queryTokenCount - span.TokenCount;
            var coveragePenalty = ignoredTokenCount * CoveragePenaltyPerIgnoredToken;
            var finalScore = signalValue.Score - coveragePenalty - fieldPenalty;
            if (finalScore <= 0 || finalScore <= (best?.FinalScore ?? 0))
            {
                continue;
            }

            best = new FieldScoreResult(
                fieldName,
                signalValue.Name,
                span.Forms.Normalised,
                span.TokenCount,
                ignoredTokenCount,
                signalValue.Score,
                fieldPenalty,
                coveragePenalty,
                finalScore);
        }

        return best;
    }

    internal static SignalScore? SpanScore(PhuzzyTextForms field, PhuzzyTextForms query)
    {
        if (string.Equals(query.Normalised, field.Normalised, StringComparison.Ordinal))
        {
            return new SignalScore("exact_normalised", 1_300);
        }

        if (SameTokens(query.Tokens, field.Tokens))
        {
            return new SignalScore("same_tokens", 1_260);
        }

        if (string.Equals(query.Compact, field.Compact, StringComparison.Ordinal))
        {
            return new SignalScore("exact_compact", 1_230);
        }

        var equivalence = EquivalenceScore(field, query);
        if (equivalence is not null)
        {
            return equivalence;
        }

        if (field.Normalised.StartsWith(query.Normalised, StringComparison.Ordinal))
        {
            return new SignalScore("prefix", 1_140);
        }

        if (field.Phonetic.Length >= 3
            && string.Equals(query.Phonetic, field.Phonetic, StringComparison.Ordinal))
        {
            return new SignalScore("consonant_skeleton", 1_080);
        }

        if (field.DoubleMetaphoneCodes.Overlaps(query.DoubleMetaphoneCodes))
        {
            return new SignalScore("double_metaphone", 1_040);
        }

        var threshold = EditDistanceThreshold(Math.Max(query.Compact.Length, field.Compact.Length));
        if (Math.Abs(query.Compact.Length - field.Compact.Length) <= threshold
            && query.Compact.Length > 0
            && field.Compact.Length > 0
            && query.Compact[0] == field.Compact[0])
        {
            var distance = BoundedEditDistance(query.Compact, field.Compact, threshold);
            if (distance <= threshold)
            {
                return new SignalScore("bounded_edit", 1_000 - (distance * 40));
            }
        }

        var similarity = TrigramDice(query.Trigrams, field.Trigrams);
        return similarity >= 0.45
            ? new SignalScore(
                "trigram",
                700 + (int)Math.Round(similarity * 200, MidpointRounding.AwayFromZero))
            : null;
    }

    private static bool SameTokens(
        IReadOnlyList<string> left,
        IReadOnlyList<string> right) =>
        left.Count == right.Count
        && left.Order(StringComparer.Ordinal).SequenceEqual(
            right.Order(StringComparer.Ordinal),
            StringComparer.Ordinal);

    private static SignalScore? EquivalenceScore(
        PhuzzyTextForms field,
        PhuzzyTextForms query)
    {
        SearchTextEquivalenceForm? best = null;
        foreach (var indexed in field.IndexedEquivalenceForms)
        {
            if (!query.QueryEquivalenceForms.Any(candidate =>
                string.Equals(candidate.Lane, indexed.Lane, StringComparison.Ordinal)
                && string.Equals(candidate.Key, indexed.Key, StringComparison.Ordinal)))
            {
                continue;
            }

            if (best is null || indexed.Score > best.Score)
            {
                best = indexed;
            }
        }

        return best is null ? null : new SignalScore(best.Signal, best.Score);
    }

    private static int EditDistanceThreshold(int length) => length switch
    {
        <= 4 => 1,
        <= 8 => 2,
        <= 16 => 3,
        _ => 4
    };

    internal static int BoundedEditDistance(string left, string right, int limit)
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

    private static double TrigramDice(
        IReadOnlySet<string> left,
        IReadOnlySet<string> right)
    {
        if (left.Count == 0 || right.Count == 0)
        {
            return 0;
        }

        var intersection = left.Count <= right.Count
            ? left.Count(right.Contains)
            : right.Count(left.Contains);
        return (2d * intersection) / (left.Count + right.Count);
    }

    internal sealed record QuerySpan(PhuzzyTextForms Forms, int TokenCount);

    internal readonly record struct FieldScoreResult(
        string Field,
        string Signal,
        string QuerySpan,
        int MatchedTokenCount,
        int IgnoredTokenCount,
        int SignalScore,
        int FieldPenalty,
        int CoveragePenalty,
        int FinalScore);

    internal readonly record struct SignalScore(string Name, int Score);
}
