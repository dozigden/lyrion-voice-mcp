using System.Diagnostics;
using System.Globalization;
using System.Text;
using LyrionVoiceMcp.Abstractions;

namespace LyrionVoiceMcp.Evaluation;

public sealed class CataloguePhuzzySearchResolver : IEvaluationSearchResolver
{
    private const int ResultLimit = 20;
    private readonly IReadOnlyList<PhuzzyCandidate> candidates;

    private CataloguePhuzzySearchResolver(
        IReadOnlyList<PhuzzyCandidate> candidates,
        long preparationDurationMilliseconds)
    {
        this.candidates = candidates;
        Metrics = new EvaluationResolverMetrics(
            candidates.Count,
            preparationDurationMilliseconds,
            null);
    }

    public string Name => "catalogue-phuzzy";
    public string Version => "1";
    public EvaluationResolverMetrics Metrics { get; }

    public static async Task<CataloguePhuzzySearchResolver> CreateAsync(
        string databasePath,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var index = await CatalogueEvaluationIndex.LoadAsync(databasePath, cancellationToken);
        var forms = new Dictionary<string, PhuzzyTextForms>(StringComparer.Ordinal);
        var candidates = index.Candidates
            .Select(candidate => CreateCandidate(candidate, forms))
            .ToArray();
        stopwatch.Stop();
        return new CataloguePhuzzySearchResolver(candidates, stopwatch.ElapsedMilliseconds);
    }

    public Task<EvaluationSearchResponse> SearchAsync(
        string query,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var queryForms = PhuzzyTextForms.Create(query);
        if (queryForms.Normalised.Length == 0)
        {
            return Task.FromResult(new EvaluationSearchResponse([], null));
        }

        var spans = CreateQuerySpans(queryForms.Tokens);
        var scored = new List<ScoredCandidate>();
        for (var index = 0; index < candidates.Count; index++)
        {
            if ((index & 255) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            var candidate = candidates[index];
            var score = Score(candidate, spans, queryForms.Tokens.Count);
            if (score > 0)
            {
                scored.Add(new ScoredCandidate(candidate, score));
            }
        }

        var results = scored
            .OrderByDescending(item => item.Score)
            .ThenBy(item => KindOrder(item.Candidate.Source.Value.Kind))
            .ThenBy(item => item.Candidate.Source.Value.Title, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Candidate.Source.StableKey, StringComparer.Ordinal)
            .Take(ResultLimit)
            .Select(item => item.Candidate.Source.Value)
            .ToArray();
        return Task.FromResult(new EvaluationSearchResponse(results, null));
    }

    private static PhuzzyCandidate CreateCandidate(
        CatalogueEvaluationCandidate candidate,
        Dictionary<string, PhuzzyTextForms> cache)
    {
        var title = GetForms(candidate.Value.Title, cache);
        var artist = GetForms(candidate.Value.Artist, cache);
        var album = GetForms(candidate.Value.Album, cache);
        var combined = PhuzzyTextForms.Create(CatalogueEvaluationText.Join(
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

    private static IReadOnlyList<QuerySpan> CreateQuerySpans(
        IReadOnlyList<string> tokens)
    {
        var spans = new List<QuerySpan>();
        for (var start = 0; start < tokens.Count; start++)
        {
            for (var length = 1; length <= tokens.Count - start; length++)
            {
                spans.Add(new QuerySpan(
                    PhuzzyTextForms.Create(string.Join(' ', tokens.Skip(start).Take(length))),
                    length));
            }
        }

        return spans;
    }

    private static int Score(
        PhuzzyCandidate candidate,
        IReadOnlyList<QuerySpan> spans,
        int queryTokenCount)
    {
        var score = FieldScore(candidate.Title, spans, queryTokenCount);
        score = Math.Max(score, FieldScore(candidate.Artist, spans, queryTokenCount) - 180);
        score = Math.Max(score, FieldScore(candidate.Album, spans, queryTokenCount) - 240);
        score = Math.Max(score, FieldScore(candidate.Combined, spans, queryTokenCount) - 100);
        return Math.Max(0, score);
    }

    private static int FieldScore(
        PhuzzyTextForms field,
        IReadOnlyList<QuerySpan> spans,
        int queryTokenCount)
    {
        if (field.Normalised.Length == 0)
        {
            return 0;
        }

        var best = 0;
        foreach (var span in spans)
        {
            var ignoredTokenPenalty = (queryTokenCount - span.TokenCount) * 250;
            best = Math.Max(
                best,
                SpanScore(field, span.Forms) - ignoredTokenPenalty);
        }

        return Math.Max(0, best);
    }

    private static int SpanScore(PhuzzyTextForms field, PhuzzyTextForms query)
    {
        if (string.Equals(query.Normalised, field.Normalised, StringComparison.Ordinal))
        {
            return 1_300;
        }

        if (SameTokens(query.Tokens, field.Tokens))
        {
            return 1_260;
        }

        if (string.Equals(query.Compact, field.Compact, StringComparison.Ordinal))
        {
            return 1_230;
        }

        if (field.SpokenAcronymAliases.Contains(query.Compact, StringComparer.Ordinal))
        {
            return 1_220;
        }

        if (field.Normalised.StartsWith(query.Normalised, StringComparison.Ordinal))
        {
            return 1_140;
        }

        if (field.Phonetic.Length >= 3
            && string.Equals(query.Phonetic, field.Phonetic, StringComparison.Ordinal))
        {
            return 1_080;
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
                return 1_000 - (distance * 40);
            }
        }

        var similarity = TrigramDice(query.Trigrams, field.Trigrams);
        return similarity >= 0.45
            ? 700 + (int)Math.Round(similarity * 200, MidpointRounding.AwayFromZero)
            : 0;
    }

    private static bool SameTokens(
        IReadOnlyList<string> left,
        IReadOnlyList<string> right) =>
        left.Count == right.Count
        && left.Order(StringComparer.Ordinal).SequenceEqual(
            right.Order(StringComparer.Ordinal),
            StringComparer.Ordinal);

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

    private static int KindOrder(MediaEntityKind kind) => kind switch
    {
        MediaEntityKind.Artist => 0,
        MediaEntityKind.Album => 1,
        MediaEntityKind.Track => 2,
        MediaEntityKind.Playlist => 3,
        _ => 4
    };

    private sealed record PhuzzyCandidate(
        CatalogueEvaluationCandidate Source,
        PhuzzyTextForms Title,
        PhuzzyTextForms Artist,
        PhuzzyTextForms Album,
        PhuzzyTextForms Combined);

    private sealed record QuerySpan(PhuzzyTextForms Forms, int TokenCount);

    private sealed record ScoredCandidate(PhuzzyCandidate Candidate, int Score);
}

internal sealed record PhuzzyTextForms(
    string Normalised,
    string Compact,
    IReadOnlyList<string> Tokens,
    string Phonetic,
    IReadOnlySet<string> Trigrams,
    IReadOnlyList<string> SpokenAcronymAliases)
{
    public static PhuzzyTextForms Create(string? value)
    {
        var normalised = PhuzzyText.Normalise(value);
        var compact = normalised.Replace(" ", string.Empty, StringComparison.Ordinal);
        return new PhuzzyTextForms(
            normalised,
            compact,
            CatalogueEvaluationText.SplitTokens(normalised),
            PhuzzyText.PhoneticSkeleton(compact),
            PhuzzyText.Trigrams(compact),
            PhuzzyText.SpokenAcronymAliases(value));
    }
}

internal static class PhuzzyText
{
    public static string Normalise(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var decomposed = value.Normalize(NormalizationForm.FormD);
        var transliterated = new StringBuilder(decomposed.Length);
        foreach (var rune in decomposed.EnumerateRunes())
        {
            if (Rune.GetUnicodeCategory(rune) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            switch (rune.Value)
            {
                case 0x00D0 or 0x00F0:
                    transliterated.Append('d');
                    break;
                case 0x00DE or 0x00FE:
                    transliterated.Append("th");
                    break;
                case 0x0131:
                    transliterated.Append('i');
                    break;
                case 0x0141 or 0x0142:
                    transliterated.Append('l');
                    break;
                case 0x00D8 or 0x00F8:
                    transliterated.Append('o');
                    break;
                case 0x00C6 or 0x00E6:
                    transliterated.Append("ae");
                    break;
                case 0x0152 or 0x0153:
                    transliterated.Append("oe");
                    break;
                case 0x00DF:
                    transliterated.Append("ss");
                    break;
                default:
                    transliterated.Append(rune);
                    break;
            }
        }

        return CatalogueEvaluationText.Normalise(transliterated.ToString());
    }

    public static string PhoneticSkeleton(string compact)
    {
        var simplified = compact
            .Replace("ph", "f", StringComparison.Ordinal)
            .Replace("ght", "t", StringComparison.Ordinal)
            .Replace("ck", "k", StringComparison.Ordinal)
            .Replace("qu", "k", StringComparison.Ordinal)
            .Replace("sh", "s", StringComparison.Ordinal)
            .Replace("ch", "c", StringComparison.Ordinal)
            .Replace("th", "t", StringComparison.Ordinal);
        var builder = new StringBuilder(simplified.Length);
        char? previous = null;
        foreach (var value in simplified)
        {
            if (value is 'a' or 'e' or 'i' or 'o' or 'u' or 'y')
            {
                continue;
            }

            var mapped = value switch
            {
                'c' or 'q' => 'k',
                'v' => 'f',
                'z' => 's',
                _ => value
            };
            if (mapped != previous)
            {
                builder.Append(mapped);
                previous = mapped;
            }
        }

        return builder.ToString();
    }

    public static IReadOnlySet<string> Trigrams(string compact)
    {
        if (compact.Length < 3)
        {
            return new HashSet<string>(StringComparer.Ordinal);
        }

        var trigrams = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index <= compact.Length - 3; index++)
        {
            trigrams.Add(compact.Substring(index, 3));
        }

        return trigrams;
    }

    public static IReadOnlyList<string> SpokenAcronymAliases(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return [];
        }

        var letters = value.Where(char.IsLetter).ToArray();
        if (letters.Length is < 2 or > 6
            || value.Any(character => !char.IsLetter(character))
            || letters.Any(character => character is < 'A' or > 'Z'))
        {
            return [];
        }

        var firstLetterName = LetterName(char.ToLowerInvariant(letters[0]));
        var firstSpokenThenLiteral = firstLetterName
            + new string(letters[1..]).ToLowerInvariant();
        var fullySpoken = string.Concat(letters.Select(character =>
            LetterName(char.ToLowerInvariant(character))));
        return [firstSpokenThenLiteral, fullySpoken];
    }

    private static string LetterName(char value) => value switch
    {
        'a' => "ay",
        'b' => "bee",
        'c' => "see",
        'd' => "dee",
        'e' => "ee",
        'f' => "ef",
        'g' => "gee",
        'h' => "aitch",
        'i' => "eye",
        'j' => "jay",
        'k' => "kay",
        'l' => "el",
        'm' => "em",
        'n' => "en",
        'o' => "oh",
        'p' => "pee",
        'q' => "cue",
        'r' => "ar",
        's' => "ess",
        't' => "tee",
        'u' => "you",
        'v' => "vee",
        'w' => "doubleyou",
        'x' => "ex",
        'y' => "why",
        'z' => "zed",
        _ => value.ToString()
    };
}
