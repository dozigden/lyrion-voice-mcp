using System.Globalization;
using System.Text;
using LyrionVoiceMcp.Abstractions;

namespace LyrionVoiceMcp.Search;

internal sealed record CatalogueIndexCandidate(
    string StableKey,
    EvaluationSearchCandidate Value,
    string Title,
    string Artist,
    string Album,
    string Combined)
{
    public static CatalogueIndexCandidate FromDocument(CatalogueSearchDocument document)
    {
        var title = CatalogueEvaluationText.Normalise(document.Title);
        var artist = CatalogueEvaluationText.Normalise(document.Artist);
        var album = CatalogueEvaluationText.Normalise(document.Album);
        return new CatalogueIndexCandidate(
            $"{document.Identity.Kind.ToString().ToLowerInvariant()}:{document.Identity.Id}",
            new EvaluationSearchCandidate(
                document.Identity.Kind,
                document.Title,
                document.Artist,
                document.Album),
            title,
            artist,
            album,
            CatalogueEvaluationText.Join(title, artist, album));
    }
}

internal static class CatalogueEvaluationText
{
    public static string Normalise(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var decomposed = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        var previousWasSpace = true;
        foreach (var rune in decomposed.EnumerateRunes())
        {
            if (Rune.GetUnicodeCategory(rune) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (Rune.IsLetterOrDigit(rune))
            {
                builder.Append(Rune.ToLowerInvariant(rune));
                previousWasSpace = false;
            }
            else if (!previousWasSpace)
            {
                builder.Append(' ');
                previousWasSpace = true;
            }
        }

        return builder.ToString().Trim();
    }

    public static IReadOnlyList<string> SplitTokens(string value) =>
        value.Split(' ', StringSplitOptions.RemoveEmptyEntries);

    public static string Join(params string[] values) =>
        string.Join(' ', values.Where(value => value.Length > 0));
}
