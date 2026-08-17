using System.Globalization;
using System.Text;
using LyrionVoiceMcp.Abstractions;

namespace LyrionVoiceMcp.Search;

internal sealed record CatalogueIndexCandidate(
    string StableKey,
    SearchCandidate Value,
    string Title,
    string Artist,
    string Album,
    string Combined)
{
    public static CatalogueIndexCandidate FromDocument(CatalogueSearchDocument document)
    {
        var title = CatalogueSearchText.Normalise(document.Title);
        var artist = CatalogueSearchText.Normalise(document.Artist);
        var album = CatalogueSearchText.Normalise(document.Album);
        return new CatalogueIndexCandidate(
            $"{document.Identity.Kind.ToString().ToLowerInvariant()}:{document.Identity.Id}",
            new SearchCandidate(
                document.Identity.Kind,
                document.Title,
                document.Artist,
                document.Album),
            title,
            artist,
            album,
            CatalogueSearchText.Join(title, artist, album));
    }
}

internal static class CatalogueSearchText
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
