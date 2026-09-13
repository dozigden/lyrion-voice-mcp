using LyrionVoiceMcp.Abstractions.Providers.BbcSounds;
using LyrionVoiceMcp.Search.Providers.BbcSounds;

namespace LyrionVoiceMcp.Search.Tests.Providers.BbcSounds;

public sealed class BbcStationIndexTests
{
    [Theory]
    [InlineData("Fictional Radio Four")]
    [InlineData("Radio Four")]
    [InlineData("Fictional Radio 4")]
    [InlineData("Fikshonal Radio Four")]
    [InlineData("play Fictional Radio Four live")]
    public void SharedTextMatchingShouldFindAStation(string query)
    {
        var index = CreateIndex(
            new("radio-four", "Fictional Radio Four", "sounds://_LIVE_radio-four"),
            new("orchard", "Orchard Speech", "sounds://_LIVE_orchard"));

        var match = index.Search(query, TestContext.Current.CancellationToken)[0];

        Assert.Equal("radio-four", match.Station.Id);
        Assert.False(string.IsNullOrWhiteSpace(match.Signal));
    }

    [Fact]
    public void SearchShouldBeIndependentBoundedAndUnavailableForBlankInput()
    {
        var index = CreateIndex(Enumerable.Range(1, 8)
            .Select(number => new BbcStation(
                $"station-{number}",
                $"Fictional Radio {number}",
                $"sounds://_LIVE_station-{number}"))
            .ToArray());

        Assert.Equal(BbcSoundsProvider.StationSearchLimit,
            index.Search("Fictional Radio", TestContext.Current.CancellationToken).Count);
        Assert.Empty(index.Search(" ", TestContext.Current.CancellationToken));

        index.Publish(new BbcStations(false, []));

        Assert.False(index.Available);
        Assert.Empty(index.Search("Fictional Radio", TestContext.Current.CancellationToken));
    }

    private static BbcStationIndex CreateIndex(params BbcStation[] stations)
    {
        var index = new BbcStationIndex();
        index.Publish(new BbcStations(true, stations));
        return index;
    }
}
