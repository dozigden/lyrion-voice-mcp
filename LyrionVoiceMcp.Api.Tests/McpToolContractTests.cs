using LyrionVoiceMcp.Contracts;

namespace LyrionVoiceMcp.Api.Tests;

public sealed class McpToolContractTests
{
    [Fact]
    public void SearchCandidateShouldUseOneOpaqueReferenceWithoutASearchId()
    {
        // Arrange
        var candidate = new SearchCandidate(
            "opaque-reference",
            SearchEntityKind.Artist,
            "The Copper Lines",
            null,
            null);

        // Act
        var properties = typeof(SearchCandidate).GetProperties();

        // Assert
        Assert.Equal("opaque-reference", candidate.Reference);
        Assert.DoesNotContain(properties, property =>
            string.Equals(property.Name, "SearchId", StringComparison.OrdinalIgnoreCase));
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
        Assert.Equal(PlayQueueMode.Replace, request.Mode);
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
