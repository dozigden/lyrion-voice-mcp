using LyrionVoiceMcp.Abstractions;
using LyrionVoiceMcp.Services;

namespace LyrionVoiceMcp.Services.Tests;

public sealed class PlayerSelectorResolverTests
{
    private readonly PlayerSelectorResolver resolver = new();

    [Fact]
    public void UniqueNameShouldResolveCaseInsensitivelyAfterTrimming()
    {
        // Arrange
        var player = Player("00:11:22:33:44:55", "North Room");

        // Act
        var outcome = resolver.Resolve([player], "  NORTH room  ");

        // Assert
        var resolved = Assert.IsType<PlayerSelectorResolved>(outcome);
        Assert.Same(player, resolved.Player);
    }

    [Fact]
    public void PlayerIdShouldTakePrecedenceOverANameMatch()
    {
        // Arrange
        var idMatch = Player("north-room", "Study");
        var nameMatch = Player("00:11:22:33:44:55", "North-Room");

        // Act
        var outcome = resolver.Resolve([idMatch, nameMatch], "NORTH-ROOM");

        // Assert
        var resolved = Assert.IsType<PlayerSelectorResolved>(outcome);
        Assert.Same(idMatch, resolved.Player);
    }

    [Fact]
    public void DuplicateNamesShouldReturnAnActionableAmbiguity()
    {
        // Arrange
        var first = Player("00:11:22:33:44:55", "North Room");
        var second = Player("66:77:88:99:aa:bb", "north room");

        // Act
        var outcome = resolver.Resolve([first, second], "North Room");

        // Assert
        var rejected = Assert.IsType<PlayerSelectorRejected>(outcome);
        Assert.Equal(PlayerSelectorRejectionReason.AmbiguousPlayer, rejected.Reason);
        Assert.Contains(first.Id, rejected.Message, StringComparison.Ordinal);
        Assert.Contains(second.Id, rejected.Message, StringComparison.Ordinal);
        Assert.Contains("get_player_status", rejected.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void UnknownPlayerShouldDirectTheCallerToPlayerDiscovery()
    {
        // Arrange
        var players = new[] { Player("00:11:22:33:44:55", "North Room") };

        // Act
        var outcome = resolver.Resolve(players, "Garden");

        // Assert
        var rejected = Assert.IsType<PlayerSelectorRejected>(outcome);
        Assert.Equal(PlayerSelectorRejectionReason.PlayerNotFound, rejected.Reason);
        Assert.Contains("Garden", rejected.Message, StringComparison.Ordinal);
        Assert.Contains("get_player_status", rejected.Message, StringComparison.Ordinal);
    }

    private static LmsPlayerStatus Player(string id, string name) =>
        new(id, name, true, PlayerPlaybackState.Stopped);
}
