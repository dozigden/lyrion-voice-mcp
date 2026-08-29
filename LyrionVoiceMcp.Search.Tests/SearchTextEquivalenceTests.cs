using System.Globalization;
using LyrionVoiceMcp.Search;

namespace LyrionVoiceMcp.Search.Tests;

public sealed class SearchTextEquivalenceTests
{
    [Fact]
    public void SpokenAcronymProviderShouldRetainExistingAliasesAndEvidence()
    {
        var indexed = SearchTextEquivalences.CreateIndexedForms("ZYX");
        var query = SearchTextEquivalences.CreateQueryForms("zed why ex");

        var match = Assert.Single(indexed, indexedForm => query.Any(queryForm =>
            queryForm.Lane == indexedForm.Lane
            && queryForm.Key == indexedForm.Key));
        Assert.Equal("acronym", match.Lane);
        Assert.Equal("spoken_acronym", match.Signal);
        Assert.Equal(1_220, match.Score);
    }

    [Theory]
    [InlineData("VI", "6")]
    [InlineData("VI", "six")]
    [InlineData("Volume XXI", "Volume 21")]
    [InlineData("Volume XXI", "Volume twenty one")]
    [InlineData("Signal V", "Signal five")]
    [InlineData("Signal X", "Signal ten")]
    [InlineData("L", "fifty")]
    public void RomanCardinalProviderShouldCreateContextualEquivalentForms(
        string indexedText,
        string queryText)
    {
        var match = FindMatch(indexedText, queryText);

        Assert.NotNull(match);
        Assert.Equal("roman_cardinal", match.Lane);
        Assert.Equal("roman_cardinal_equivalent", match.Signal);
        Assert.Equal(1_200, match.Score);
    }

    [Theory]
    [InlineData("Volume VI", "six")]
    [InlineData("Signal V", "five")]
    [InlineData("Signal X", "ten")]
    [InlineData("I", "one")]
    [InlineData("I Signal", "one signal")]
    [InlineData("V.I.", "six")]
    [InlineData("Volume IIII", "volume four")]
    [InlineData("Volume LI", "volume fifty one")]
    [InlineData("MIX", "one thousand nine")]
    [InlineData("Signal6", "signal six")]
    public void RomanCardinalProviderShouldRejectUnsupportedOrIncompleteForms(
        string indexedText,
        string queryText)
    {
        Assert.Null(FindMatch(indexedText, queryText));
    }

    [Theory]
    [InlineData("V", "five")]
    [InlineData("X", "ten")]
    public void StandaloneVAndXShouldRemainSupported(
        string indexedText,
        string queryText)
    {
        Assert.NotNull(FindMatch(indexedText, queryText));
    }

    [Fact]
    public void EverySupportedCanonicalRomanValueShouldMatchItsArabicForm()
    {
        for (var value = 2; value <= RomanCardinalEquivalenceProvider.MaximumValue; value++)
        {
            Assert.NotNull(FindMatch(
                RomanCardinalEquivalenceProvider.Roman(value),
                value.ToString(CultureInfo.InvariantCulture)));
        }
    }

    [Fact]
    public void QueryRomanCasingShouldNormaliseWithoutAcceptingLowercaseStoredForms()
    {
        Assert.NotNull(FindMatch("VI", "vi"));
        Assert.Null(FindMatch("vi", "six"));
    }

    [Theory]
    [InlineData("VI", "-6")]
    [InlineData("VI", "+6")]
    [InlineData("-6", "six")]
    [InlineData("VI", "minus six")]
    [InlineData("XXI", "minus twenty one")]
    [InlineData("Six", ".VI.")]
    [InlineData("Six", "V.I.")]
    public void SignedNumbersAndDottedRomanQueriesShouldNotCreateEquivalence(
        string indexedText,
        string queryText)
    {
        Assert.Null(FindMatch(indexedText, queryText));
    }

    [Theory]
    [InlineData("50 1", "fifty one")]
    [InlineData("1 hundred", "one hundred")]
    public void UnsupportedSpokenCardinalsShouldNotBeDecomposed(
        string indexedText,
        string queryText)
    {
        Assert.Null(FindMatch(indexedText, queryText));
    }

    [Fact]
    public void NonAsciiDigitsShouldNotBreakPhoneticForms()
    {
        var exception = Record.Exception(() => PhuzzyText.DoubleMetaphoneCodes("Signal ٦"));

        Assert.Null(exception);
    }

    private static SearchTextEquivalenceForm? FindMatch(
        string indexedText,
        string queryText)
    {
        var indexed = SearchTextEquivalences.CreateIndexedForms(indexedText);
        var query = SearchTextEquivalences.CreateQueryForms(queryText);
        return indexed.FirstOrDefault(indexedForm => query.Any(queryForm =>
            queryForm.Lane == indexedForm.Lane
            && queryForm.Key == indexedForm.Key));
    }
}
