using System.Text.RegularExpressions;

namespace LyrionVoiceMcp.Search.Providers.BbcSounds;

internal sealed record BbcProgrammeQuery(
    PhuzzyTextForms Forms, IReadOnlyList<TextMatchScorer.QuerySpan> Spans,
    BbcProgrammeQuery? NameInterpretation = null)
{
    private static readonly string[][] BoundaryPhrases =
    [
        ["from", "bbc", "sounds"], ["on", "bbc", "sounds"], ["bbc", "sounds"],
        ["the", "latest", "episode"], ["the", "newest", "episode"],
        ["latest", "episode"], ["newest", "episode"]
    ];

    public static BbcProgrammeQuery Create(string text)
    {
        var original = Prepare(text);
        var tokens = Regex.Matches(text, @"[\p{L}\p{Nd}]+");
        var start = 0;
        var end = tokens.Count;
        var rawStart = 0;
        var rawEnd = text.Length;
        while (start < end)
        {
            var prefix = BoundaryPhrases.FirstOrDefault(phrase => Matches(tokens, start, end, phrase));
            if (prefix is not null)
            {
                start += prefix.Length;
                if (prefix[^1] == "episode" && start < end && tokens[start].Value.Equals("of", StringComparison.OrdinalIgnoreCase)) start++;
                rawStart = tokens[start - 1].Index + tokens[start - 1].Length;
                continue;
            }
            var suffix = BoundaryPhrases.FirstOrDefault(phrase => Matches(tokens, end - phrase.Length, end, phrase, start));
            if (suffix is null) break;
            end -= suffix.Length;
            rawEnd = tokens[end].Index;
        }
        if (start == 0 && end == tokens.Count) return original;
        // Slice the original text so punctuation/sign eligibility for equivalence
        // matching survives; never reconstruct a name from normalised tokens.
        var name = start == end ? string.Empty : text[rawStart..rawEnd];
        return original with { NameInterpretation = Prepare(name) };
    }

    private static BbcProgrammeQuery Prepare(string text)
    {
        var forms = PhuzzyTextForms.Create(text);
        return new BbcProgrammeQuery(forms, TextMatchScorer.CreateQuerySpans(text, forms.Tokens));
    }

    private static bool Matches(MatchCollection tokens, int start, int end, string[] phrase, int minimumStart = 0) =>
        start >= minimumStart && start + phrase.Length <= end
        && phrase.Select((word, index) => tokens[start + index].Value.Equals(word, StringComparison.OrdinalIgnoreCase)).All(value => value);
}
