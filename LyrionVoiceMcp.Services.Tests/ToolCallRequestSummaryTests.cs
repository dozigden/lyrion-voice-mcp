using System.Text.Json;
using LyrionVoiceMcp.Services;

namespace LyrionVoiceMcp.Services.Tests;

public sealed class ToolCallRequestSummaryTests
{
    [Theory]
    [InlineData("search", """{"name":"The Lantern Hours"}""", "The Lantern Hours")]
    [InlineData("search", """{"name":"Paper Satellites","genre":"Jazz","fromYear":1990,"toYear":2000,"rating":4.5,"ratingMatch":"at_least"}""", "Paper Satellites · Genre: Jazz · Years: 1990–2000 · Rating: ≥ 4.5")]
    [InlineData("search", """{"rating":0,"ratingMatch":"exact"}""", "Rating: = 0")]
    [InlineData("search", """{"fromYear":99}""", "From year: 99")]
    [InlineData("search", """{"toYear":2001}""", "To year: 2001")]
    [InlineData("search", "{}", "Broad search")]
    [InlineData("search", "null", "Broad search")]
    [InlineData("search", """{"name":null,"genre":null}""", "Broad search")]
    [InlineData("browse", "{}", "Library roots")]
    [InlineData("browse", """{"browseRef":"albums_fictional"}""", "albums_fictional")]
    [InlineData("get_player_status", "{}", "All players")]
    [InlineData("control_player", """{"player":"Studio player","action":"power_off"}""", "Studio player · Power off")]
    [InlineData("get_queue", """{"player":"Studio player"}""", "Studio player")]
    [InlineData("play", """{"player":"Studio player","items":["track_one","album_two"]}""", "Studio player · 2 requested items")]
    [InlineData("manage_queue", """{"player":"Studio player","action":"insert_next","items":["track_one"]}""", "Studio player · Insert next · 1 requested item")]
    [InlineData("manage_queue", """{"player":"Studio player","action":"clear"}""", "Studio player · Clear")]
    public void ShouldSummariseRecordedCurrentArguments(string tool, string arguments, string expected)
    {
        var summary = ToolCallRequestSummary.Create(tool, arguments, argumentsTruncated: false);

        Assert.Equal(expected, summary);
    }

    [Theory]
    [InlineData("search", "{broken")]
    [InlineData("search", "[]")]
    [InlineData("search", """{"name":{"unexpected":"value"}}""")]
    [InlineData("search", """{"query":"Older request format"}""")]
    [InlineData("browse", """{"browseRef":42}""")]
    [InlineData("play", "null")]
    [InlineData("future_tool", """{"name":"The Lantern Hours"}""")]
    public void ShouldOmitUnavailableSummaries(string tool, string arguments)
    {
        var summary = ToolCallRequestSummary.Create(tool, arguments, argumentsTruncated: false);

        Assert.Null(summary);
    }

    [Fact]
    public void ShouldNotSummariseTruncatedArguments()
    {
        var summary = ToolCallRequestSummary.Create(
            "search", """{"name":"The Lantern Hours"}""", argumentsTruncated: true);

        Assert.Null(summary);
    }

    [Fact]
    public void ShouldKeepTheSummaryOnOneLine()
    {
        var arguments = JsonSerializer.Serialize(new { name = "  Paper\n\tSatellites\0  " });

        var summary = ToolCallRequestSummary.Create("search", arguments, argumentsTruncated: false);

        Assert.Equal("Paper Satellites", summary);
    }

    [Fact]
    public void ShouldBoundLongSummariesWithoutSplittingUnicodePairs()
    {
        var name = new string('x', ToolCallRequestSummary.MaximumLength - 2) + "🎵" + new string('y', 30);
        var arguments = JsonSerializer.Serialize(new { name });

        var summary = ToolCallRequestSummary.Create("search", arguments, argumentsTruncated: false);

        Assert.NotNull(summary);
        Assert.True(summary.Length <= ToolCallRequestSummary.MaximumLength);
        Assert.EndsWith("…", summary);
        Assert.DoesNotContain(summary, char.IsSurrogate);
    }
}
