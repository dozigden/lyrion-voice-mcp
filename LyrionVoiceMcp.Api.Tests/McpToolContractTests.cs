using LyrionVoiceMcp.Contracts;

namespace LyrionVoiceMcp.Api.Tests;

public sealed class McpToolContractTests
{
    [Fact]
    public void SearchResultsShouldExposeReferencesByCapability()
    {
        // Arrange
        var artist = new SearchArtist("The Copper Lines", "browse-reference");
        var exactArtist = new SearchExactArtistMatch(
            "The Copper Lines",
            7,
            "discography-reference");
        var album = new SearchAlbum(
            "Lantern Signals",
            "The Copper Lines",
            "browse-reference",
            "play-reference");
        var track = new SearchTrack(
            "Ninety Point Signal",
            "The Copper Lines",
            "Lantern Signals",
            4.5m,
            "play-reference");

        // Act
        var artistProperties = typeof(SearchArtist).GetProperties();
        var exactArtistProperties = typeof(SearchExactArtistMatch).GetProperties();
        var albumProperties = typeof(SearchAlbum).GetProperties();
        var trackProperties = typeof(SearchTrack).GetProperties();

        // Assert
        Assert.Equal(["BrowseRef", "Name"], artistProperties.Select(item => item.Name).Order());
        Assert.Equal(
            ["DiscographyAlbumCount", "DiscographyBrowseRef", "Name"],
            exactArtistProperties.Select(item => item.Name).Order());
        Assert.Equal(
            ["Artist", "BrowseRef", "PlayRef", "Title"],
            albumProperties.Select(item => item.Name).Order());
        Assert.Equal(
            ["Album", "Artist", "PlayRef", "Rating", "Title"],
            trackProperties.Select(item => item.Name).Order());
        Assert.Equal("browse-reference", artist.BrowseRef);
        Assert.Equal("discography-reference", exactArtist.DiscographyBrowseRef);
        Assert.Equal(7, exactArtist.DiscographyAlbumCount);
        Assert.Equal("play-reference", album.PlayRef);
        Assert.Equal(4.5m, track.Rating);
    }

    [Fact]
    public void PlayShouldAcceptRawPlayerIdAndOrderedResultReferences()
    {
        // Arrange
        var references = new[] { "first-reference", "second-reference" };

        // Act
        var request = new PlayRequest("00:11:22:33:44:55", references);

        // Assert
        Assert.Equal("00:11:22:33:44:55", request.Player);
        Assert.Equal(references, request.Items);
        Assert.Equal(
            ["Items", "Player"],
            typeof(PlayRequest)
                .GetProperties()
                .Select(property => property.Name)
                .Order()
                .ToArray());
    }

    [Fact]
    public void PlayerStatusShouldContainFullVoiceRelevantState()
    {
        // Arrange
        var expectedProperties = new[]
        {
            "Id",
            "Mode",
            "Muted",
            "Name",
            "NowPlaying",
            "PoweredOn",
            "Volume"
        };

        // Act
        var properties = typeof(PlayerStatus)
            .GetProperties()
            .Select(property => property.Name)
            .Order()
            .ToArray();

        // Assert
        Assert.Equal(expectedProperties, properties);
    }

    [Fact]
    public void ControlPlayerShouldAcceptOnePlayerAndOneAction()
    {
        // Arrange
        var request = new ControlPlayerRequest(
            "00:11:22:33:44:55",
            PlayerControlAction.Previous);

        // Act
        var properties = typeof(ControlPlayerRequest)
            .GetProperties()
            .Select(property => property.Name)
            .Order()
            .ToArray();

        // Assert
        Assert.Equal(["Action", "Player"], properties);
        Assert.Equal("00:11:22:33:44:55", request.Player);
        Assert.Equal(PlayerControlAction.Previous, request.Action);
    }

    [Fact]
    public void GetQueueShouldContainOnlyPlayerCurrentIndexAndDisplayItems()
    {
        // Arrange
        var expectedResponseProperties = new[]
        {
            "CurrentIndex",
            "Items",
            "Player"
        };
        var expectedItemProperties = new[]
        {
            "Album",
            "Artist",
            "DurationSeconds",
            "Index",
            "Title"
        };

        // Act
        var responseProperties = typeof(GetQueueResponse)
            .GetProperties()
            .Select(property => property.Name)
            .Order()
            .ToArray();
        var itemProperties = typeof(QueueItem)
            .GetProperties()
            .Select(property => property.Name)
            .Order()
            .ToArray();

        // Assert
        Assert.Equal(expectedResponseProperties, responseProperties);
        Assert.Equal(expectedItemProperties, itemProperties);
    }

    [Fact]
    public void NowPlayingShouldExcludeQueueAndLmsIdentity()
    {
        // Arrange
        var expectedProperties = new[]
        {
            "Album",
            "Artist",
            "DurationSeconds",
            "ElapsedSeconds",
            "Title"
        };

        // Act
        var properties = typeof(NowPlaying)
            .GetProperties()
            .Select(property => property.Name)
            .Order()
            .ToArray();

        // Assert
        Assert.Equal(expectedProperties, properties);
    }
}
