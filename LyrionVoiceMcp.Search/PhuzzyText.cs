using System.Globalization;
using System.Text;
using Lucene.Net.Analysis.Phonetic.Language;

namespace LyrionVoiceMcp.Search;

internal sealed record PhuzzyTextForms(
    string Normalised,
    string Compact,
    IReadOnlyList<string> Tokens,
    string Phonetic,
    IReadOnlySet<string> DoubleMetaphoneCodes,
    IReadOnlySet<string> Trigrams,
    IReadOnlyList<SearchTextEquivalenceForm> IndexedEquivalenceForms,
    IReadOnlyList<SearchTextEquivalenceForm> QueryEquivalenceForms)
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
            SearchTextEquivalences.CreateIndexedForms(value),
            SearchTextEquivalences.CreateQueryForms(value));
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
        if (!value.Any(IsAsciiDigit))
        {
            return value;
        }

        var parts = new List<string>();
        foreach (var token in value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var expanded = new StringBuilder();
            foreach (var character in token)
            {
                if (IsAsciiDigit(character))
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

    private static bool IsAsciiDigit(char value) => value is >= '0' and <= '9';

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

}
