using LyrionVoiceMcp.Abstractions;
using LyrionVoiceMcp.Services;

namespace LyrionVoiceMcp.Services.Tests;

public sealed class ToolCallReferenceSnapshotsTests
{
    [Fact]
    public void PlayShouldCaptureEveryStringReferenceInRequestOrder()
    {
        // Arrange
        var metadata = new ReferenceDisplayMetadata(
            ReferenceDisplayKind.Track,
            "Paper Satellites",
            "The Lantern Hours",
            "Northern Windows");

        // Act
        var snapshots = ToolCallReferenceSnapshots.Capture(
            "play",
            """{"player":"Studio player","items":["track_one","unknown","track_one"]}""",
            reference => reference == "track_one" ? metadata : null);

        // Assert
        var captured = Assert.IsAssignableFrom<IReadOnlyList<ToolCallReferenceSnapshot>>(snapshots);
        Assert.Equal(["items[0]", "items[1]", "items[2]"], captured.Select(item => item.ArgumentPath));
        Assert.Equal(["track_one", "unknown", "track_one"], captured.Select(item => item.Reference));
        Assert.Same(metadata, captured[0].DisplayMetadata);
        Assert.Null(captured[1].DisplayMetadata);
        Assert.Same(metadata, captured[2].DisplayMetadata);
    }

    [Fact]
    public void BrowseShouldCaptureItsLocationWithoutInspectingUnrelatedArguments()
    {
        var metadata = new ReferenceDisplayMetadata(
            ReferenceDisplayKind.Album,
            "Northern Windows",
            "The Lantern Hours");

        var snapshots = ToolCallReferenceSnapshots.Capture(
            "browse",
            """{"browseRef":"album_one","future":"ignored"}""",
            reference => reference == "album_one" ? metadata : null);

        var snapshot = Assert.Single(snapshots!);
        Assert.Equal("browseRef", snapshot.ArgumentPath);
        Assert.Equal("album_one", snapshot.Reference);
        Assert.Same(metadata, snapshot.DisplayMetadata);
    }

    [Theory]
    [InlineData("search")]
    [InlineData("get_player_status")]
    [InlineData("future_tool")]
    public void ToolsWithoutReferenceArgumentsShouldNotCreateSnapshots(string toolName)
    {
        var snapshots = ToolCallReferenceSnapshots.Capture(
            toolName,
            "{}",
            _ => throw new InvalidOperationException("Should not resolve references."));

        Assert.Null(snapshots);
    }
}
