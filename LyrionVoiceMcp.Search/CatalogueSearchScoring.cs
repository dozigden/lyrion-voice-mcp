using System.Globalization;
using System.Text;
using Lucene.Net.Analysis.Phonetic.Language;
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

        var spans = CreateQuerySpans(queryForms.Tokens);
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
                    score.Evidence));
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
            return new CandidateScore(0, null);
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
        return new CandidateScore(value.FinalScore, evidence);
    }

    private static void SelectBest(
        FieldScoreResult? candidate,
        ref FieldScoreResult? best)
    {
        if (candidate is not null
            && candidate.Value.FinalScore > (best?.FinalScore ?? 0))
        {
            best = candidate;
        }
    }

    private static FieldScoreResult? FieldScore(
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
            var coveragePenalty = ignoredTokenCount * 250;
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

    private static SignalScore? SpanScore(PhuzzyTextForms field, PhuzzyTextForms query)
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

        if (field.SpokenAcronymAliases.Contains(query.Compact, StringComparer.Ordinal))
        {
            return new SignalScore("spoken_acronym", 1_220);
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

    private sealed record QuerySpan(PhuzzyTextForms Forms, int TokenCount);

    private readonly record struct CandidateScore(
        int Score,
        SearchScoreEvidence? Evidence);

    private readonly record struct FieldScoreResult(
        string Field,
        string Signal,
        string QuerySpan,
        int MatchedTokenCount,
        int IgnoredTokenCount,
        int SignalScore,
        int FieldPenalty,
        int CoveragePenalty,
        int FinalScore);

    private readonly record struct SignalScore(string Name, int Score);
}

internal sealed record PhuzzyCandidate(
    CatalogueIndexCandidate Source,
    PhuzzyTextForms Title,
    PhuzzyTextForms Artist,
    PhuzzyTextForms Album,
    PhuzzyTextForms Combined);

internal sealed record PhuzzyTextForms(
    string Normalised,
    string Compact,
    IReadOnlyList<string> Tokens,
    string Phonetic,
    IReadOnlySet<string> DoubleMetaphoneCodes,
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
            CatalogueSearchText.SplitTokens(normalised),
            PhuzzyText.PhoneticSkeleton(compact),
            PhuzzyText.DoubleMetaphoneCodes(normalised),
            PhuzzyText.Trigrams(compact),
            PhuzzyText.SpokenAcronymAliases(value));
    }
}

internal static class PhuzzyText
{
    private static readonly DoubleMetaphone DoubleMetaphoneEncoder = new()
    {
        MaxCodeLen = 8
    };

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

        return CatalogueSearchText.Normalise(transliterated.ToString());
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

    public static IReadOnlySet<string> DoubleMetaphoneCodes(string normalised)
    {
        var codes = new HashSet<string>(StringComparer.Ordinal);
        if (normalised.Length == 0)
        {
            return codes;
        }

        var phoneticInput = ExpandDigitsForPhonetics(normalised);
        var primary = DoubleMetaphoneEncoder.GetDoubleMetaphone(phoneticInput);
        var alternate = DoubleMetaphoneEncoder.GetDoubleMetaphone(
            phoneticInput,
            alternate: true);
        if (!string.IsNullOrEmpty(primary))
        {
            codes.Add(primary);
        }

        if (!string.IsNullOrEmpty(alternate))
        {
            codes.Add(alternate);
        }

        return codes;
    }

    private static string ExpandDigitsForPhonetics(string value)
    {
        if (!value.Any(char.IsDigit))
        {
            return value;
        }

        var parts = new List<string>();
        foreach (var token in value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var expanded = new StringBuilder();
            foreach (var character in token)
            {
                if (char.IsDigit(character))
                {
                    if (expanded.Length > 0 && expanded[^1] != ' ')
                    {
                        expanded.Append(' ');
                    }

                    expanded.Append(DigitName(character));
                    expanded.Append(' ');
                }
                else
                {
                    expanded.Append(character);
                }
            }

            parts.Add(expanded.ToString().Trim());
        }

        return string.Join(' ', parts);
    }

    private static string DigitName(char value) => value switch
    {
        '0' => "zero",
        '1' => "one",
        '2' => "two",
        '3' => "three",
        '4' => "four",
        '5' => "five",
        '6' => "six",
        '7' => "seven",
        '8' => "eight",
        '9' => "nine",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null)
    };

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
