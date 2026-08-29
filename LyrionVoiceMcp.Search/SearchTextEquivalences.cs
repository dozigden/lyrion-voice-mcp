using System.Globalization;

namespace LyrionVoiceMcp.Search;

internal sealed record SearchTextEquivalenceForm(
    string Lane,
    string Signal,
    int Score,
    string Key);

internal sealed record SearchTextEquivalenceQuerySpan(
    string Text,
    int TokenCount,
    IReadOnlyList<SearchTextEquivalenceForm> Forms);

internal sealed record SearchTextEquivalenceToken(
    string Raw,
    string Normalised,
    bool IsDotted,
    bool IsSigned);

internal sealed record SearchTextEquivalenceQuery(
    string Text,
    IReadOnlyList<SearchTextEquivalenceToken> Tokens,
    IReadOnlyList<SearchTextEquivalenceToken> ContextTokens,
    int Start);

internal interface ISearchTextEquivalenceProvider
{
    IReadOnlyList<SearchTextEquivalenceForm> CreateIndexedForms(string? value);

    IReadOnlyList<SearchTextEquivalenceForm> CreateQueryForms(
        SearchTextEquivalenceQuery query);
}

internal static class SearchTextEquivalences
{
    private static readonly IReadOnlyList<ISearchTextEquivalenceProvider> Providers =
    [
        new SpokenAcronymEquivalenceProvider(),
        new RomanCardinalEquivalenceProvider()
    ];

    public static IReadOnlyList<SearchTextEquivalenceForm> CreateIndexedForms(
        string? value) =>
        CreateForms(value, static (provider, text) => provider.CreateIndexedForms(text));

    public static IReadOnlyList<SearchTextEquivalenceForm> CreateQueryForms(
        string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return [];
        }

        var tokens = Tokenise(value);
        return CreateQueryForms(new SearchTextEquivalenceQuery(
            string.Join(' ', tokens.Select(token => token.Normalised)),
            tokens,
            tokens,
            0));
    }

    public static IReadOnlyList<SearchTextEquivalenceQuerySpan> CreateQuerySpans(
        string value,
        IReadOnlyList<string> normalisedTokens)
    {
        var sourceTokens = Tokenise(value);
        var canPreserveSourceSyntax = sourceTokens.Count == normalisedTokens.Count;
        var spans = new List<SearchTextEquivalenceQuerySpan>();
        for (var start = 0; start < normalisedTokens.Count; start++)
        {
            for (var length = 1; length <= normalisedTokens.Count - start; length++)
            {
                var text = string.Join(' ', normalisedTokens.Skip(start).Take(length));
                var forms = canPreserveSourceSyntax
                    ? CreateQueryForms(new SearchTextEquivalenceQuery(
                        text,
                        sourceTokens.Skip(start).Take(length).ToArray(),
                        sourceTokens,
                        start))
                    : [];
                spans.Add(new SearchTextEquivalenceQuerySpan(text, length, forms));
            }
        }

        return spans;
    }

    private static IReadOnlyList<SearchTextEquivalenceForm> CreateForms(
        string? value,
        Func<ISearchTextEquivalenceProvider, string?,
            IReadOnlyList<SearchTextEquivalenceForm>> create)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return [];
        }

        return Providers
            .SelectMany(provider => create(provider, value))
            .Distinct()
            .ToArray();
    }

    private static IReadOnlyList<SearchTextEquivalenceForm> CreateQueryForms(
        SearchTextEquivalenceQuery query) =>
        Providers
            .SelectMany(provider => provider.CreateQueryForms(query))
            .Distinct()
            .ToArray();

    internal static IReadOnlyList<SearchTextEquivalenceToken> Tokenise(string value)
    {
        var tokens = new List<SearchTextEquivalenceToken>();
        var start = -1;
        for (var index = 0; index <= value.Length; index++)
        {
            var isTokenCharacter = index < value.Length && char.IsLetterOrDigit(value[index]);
            if (isTokenCharacter && start < 0)
            {
                start = index;
                continue;
            }

            if (isTokenCharacter || start < 0)
            {
                continue;
            }

            var raw = value[start..index];
            var normalised = PhuzzyText.Normalise(raw);
            if (normalised.Length > 0)
            {
                var isDotted = (start > 0 && value[start - 1] == '.')
                    || (index < value.Length && value[index] == '.');
                var signIndex = start - 1;
                while (signIndex >= 0 && char.IsWhiteSpace(value[signIndex]))
                {
                    signIndex--;
                }

                var isSigned = signIndex >= 0
                    && value[signIndex] is '-' or '+'
                    && (signIndex == 0 || !char.IsLetterOrDigit(value[signIndex - 1]));
                tokens.Add(new SearchTextEquivalenceToken(
                    raw,
                    normalised,
                    isDotted,
                    isSigned));
            }

            start = -1;
        }

        return tokens;
    }
}

internal sealed class SpokenAcronymEquivalenceProvider : ISearchTextEquivalenceProvider
{
    private const string Lane = "acronym";
    private const string Signal = "spoken_acronym";
    private const int Score = 1_220;

    public IReadOnlyList<SearchTextEquivalenceForm> CreateIndexedForms(string? value)
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
        return [
            Form(firstSpokenThenLiteral),
            Form(fullySpoken)
        ];
    }

    public IReadOnlyList<SearchTextEquivalenceForm> CreateQueryForms(
        SearchTextEquivalenceQuery query)
    {
        var compact = PhuzzyText.Normalise(query.Text)
            .Replace(" ", string.Empty, StringComparison.Ordinal);
        return compact.Length == 0 ? [] : [Form(compact)];
    }

    private static SearchTextEquivalenceForm Form(string key) =>
        new(Lane, Signal, Score, key);

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

internal sealed class RomanCardinalEquivalenceProvider : ISearchTextEquivalenceProvider
{
    internal const int MinimumValue = 1;
    internal const int MaximumValue = 50;

    private const string Lane = "roman_cardinal";
    private const string Signal = "roman_cardinal_equivalent";
    private const int Score = 1_200;
    private static readonly IReadOnlyList<(int Value, string Text)> RomanParts =
    [
        (50, "L"),
        (40, "XL"),
        (10, "X"),
        (9, "IX"),
        (5, "V"),
        (4, "IV"),
        (1, "I")
    ];
    private static readonly IReadOnlyList<string> Units =
    [
        "zero",
        "one",
        "two",
        "three",
        "four",
        "five",
        "six",
        "seven",
        "eight",
        "nine",
        "ten",
        "eleven",
        "twelve",
        "thirteen",
        "fourteen",
        "fifteen",
        "sixteen",
        "seventeen",
        "eighteen",
        "nineteen"
    ];
    private static readonly IReadOnlyList<string> Tens =
    [
        string.Empty,
        string.Empty,
        "twenty",
        "thirty",
        "forty",
        "fifty"
    ];
    private static readonly IReadOnlyList<CardinalDefinition> Definitions =
        Enumerable.Range(MinimumValue, MaximumValue)
            .Select(value => new CardinalDefinition(
                value,
                Roman(value),
                Spoken(value)))
            .ToArray();
    private static readonly IReadOnlyDictionary<string, int> RomanValues = Definitions
        .ToDictionary(definition => definition.Roman, definition => definition.Value);
    private static readonly IReadOnlyDictionary<string, int> SpokenValues = Definitions
        .ToDictionary(definition => definition.Spoken, definition => definition.Value);
    private static readonly IReadOnlySet<string> SpokenNumberWords = Units
        .Concat(Tens.Where(value => value.Length > 0))
        .ToHashSet(StringComparer.Ordinal);

    public IReadOnlyList<SearchTextEquivalenceForm> CreateIndexedForms(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? []
            : CreateForms(
                SearchTextEquivalences.Tokenise(value),
                requireCanonicalRomanCasing: true);

    public IReadOnlyList<SearchTextEquivalenceForm> CreateQueryForms(
        SearchTextEquivalenceQuery query) =>
        HasUnsupportedSyntax(query.ContextTokens)
            || SplitsSpokenCardinal(query)
            ? []
            : CreateForms(query.Tokens, requireCanonicalRomanCasing: false);

    private static IReadOnlyList<SearchTextEquivalenceForm> CreateForms(
        IReadOnlyList<SearchTextEquivalenceToken> tokens,
        bool requireCanonicalRomanCasing)
    {
        if (tokens.Count == 0 || HasUnsupportedSyntax(tokens))
        {
            return [];
        }

        var canonical = new List<string>(tokens.Count);
        var transformed = false;
        for (var index = 0; index < tokens.Count; index++)
        {
            if (TryReadSpoken(tokens, index, out var spokenValue, out var spokenLength))
            {
                canonical.Add(CardinalKey(spokenValue));
                index += spokenLength - 1;
                transformed = true;
                continue;
            }

            var token = tokens[index];
            if (TryReadArabic(token, out var arabicValue))
            {
                canonical.Add(CardinalKey(arabicValue));
                transformed = true;
                continue;
            }

            if (TryReadRoman(
                token,
                requireCanonicalRomanCasing,
                out var romanValue))
            {
                canonical.Add(CardinalKey(romanValue));
                transformed = true;
                continue;
            }

            canonical.Add(token.Normalised);
        }

        if (!transformed)
        {
            return [];
        }

        canonical.Sort(StringComparer.Ordinal);
        return [new SearchTextEquivalenceForm(
            Lane,
            Signal,
            Score,
            string.Join(' ', canonical))];
    }

    private static bool TryReadSpoken(
        IReadOnlyList<SearchTextEquivalenceToken> tokens,
        int index,
        out int value,
        out int length)
    {
        if (IsBlocked(tokens[index]))
        {
            value = 0;
            length = 0;
            return false;
        }

        if (index + 1 < tokens.Count)
        {
            var pair = $"{tokens[index].Normalised} {tokens[index + 1].Normalised}";
            if (!IsBlocked(tokens[index + 1])
                && SpokenValues.TryGetValue(pair, out value))
            {
                length = 2;
                return true;
            }
        }

        if (SpokenValues.TryGetValue(tokens[index].Normalised, out value))
        {
            length = 1;
            return true;
        }

        value = 0;
        length = 0;
        return false;
    }

    private static bool TryReadArabic(
        SearchTextEquivalenceToken token,
        out int value)
    {
        if (IsBlocked(token)
            || token.Normalised.Length == 0
            || token.Normalised.Any(character => character is < '0' or > '9')
            || !int.TryParse(
                token.Normalised,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out value)
            || value is < MinimumValue or > MaximumValue)
        {
            value = 0;
            return false;
        }

        return string.Equals(
            token.Normalised,
            value.ToString(CultureInfo.InvariantCulture),
            StringComparison.Ordinal);
    }

    private static bool TryReadRoman(
        SearchTextEquivalenceToken token,
        bool requireCanonicalCasing,
        out int value)
    {
        var raw = token.Raw;
        if (IsBlocked(token)
            || token.IsDotted
            || (requireCanonicalCasing
                && raw.Any(character => character is < 'A' or > 'Z'))
            || !RomanValues.TryGetValue(raw.ToUpperInvariant(), out value)
            || value == 1)
        {
            value = 0;
            return false;
        }

        return true;
    }

    private static bool HasUnsupportedSpokenSequence(
        IReadOnlyList<SearchTextEquivalenceToken> tokens)
    {
        for (var index = 0; index + 1 < tokens.Count; index++)
        {
            var first = tokens[index].Normalised;
            var second = tokens[index + 1].Normalised;
            if (SpokenNumberWords.Contains(first)
                && SpokenNumberWords.Contains(second))
            {
                if (!SpokenValues.ContainsKey($"{first} {second}"))
                {
                    return true;
                }

                index++;
                continue;
            }

            if ((SpokenNumberWords.Contains(first)
                    && second is "hundred" or "thousand")
                || (first is "hundred" or "thousand"
                    && SpokenNumberWords.Contains(second)))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasUnsupportedSyntax(
        IReadOnlyList<SearchTextEquivalenceToken> tokens) =>
        HasUnsupportedSpokenSequence(tokens)
        || tokens.Any(token => token.IsSigned && IsNumberLike(token.Normalised))
        || tokens.Zip(tokens.Skip(1)).Any(pair =>
            pair.First.Normalised is "minus" or "negative"
            && IsNumberLike(pair.Second.Normalised));

    private static bool SplitsSpokenCardinal(SearchTextEquivalenceQuery query)
    {
        var end = query.Start + query.Tokens.Count;
        for (var index = 0; index + 1 < query.ContextTokens.Count; index++)
        {
            var phrase = $"{query.ContextTokens[index].Normalised} "
                + query.ContextTokens[index + 1].Normalised;
            if (!SpokenValues.ContainsKey(phrase))
            {
                continue;
            }

            var includesFirst = query.Start <= index && index < end;
            var includesSecond = query.Start <= index + 1 && index + 1 < end;
            if (includesFirst != includesSecond)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsNumberLike(string value) =>
        SpokenNumberWords.Contains(value)
        || (value.Length > 0
            && value.All(character => character is >= '0' and <= '9'))
        || RomanValues.ContainsKey(value.ToUpperInvariant());

    private static bool IsBlocked(SearchTextEquivalenceToken token) =>
        token.IsSigned;

    private static string CardinalKey(int value) => $"cardinal:{value}";

    internal static string Roman(int value)
    {
        var remaining = value;
        var builder = new System.Text.StringBuilder();
        foreach (var part in RomanParts)
        {
            while (remaining >= part.Value)
            {
                builder.Append(part.Text);
                remaining -= part.Value;
            }
        }

        return builder.ToString();
    }

    private static string Spoken(int value)
    {
        if (value < 20)
        {
            return Units[value];
        }

        var tens = value / 10;
        var units = value % 10;
        return units == 0 ? Tens[tens] : $"{Tens[tens]} {Units[units]}";
    }

    private sealed record CardinalDefinition(int Value, string Roman, string Spoken);

}
