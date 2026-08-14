using LyrionVoiceMcp.Abstractions;
using LyrionVoiceMcp.Services;

namespace LyrionVoiceMcp.Services.Tests;

public sealed class PlayerControlServiceTests
{
    private const string PlayerId = "00:11:22:33:44:55";

    [Fact]
    public async Task ControlShouldValidatePlayerThenReturnItsUpdatedStatus()
    {
        // Arrange
        var initialPlayer = Player(PlayerPlaybackState.Playing);
        var updatedPlayer = Player(PlayerPlaybackState.Paused);
        var playerClient = new StubPlayerClient(initialPlayer, updatedPlayer);
        var controlClient = new StubPlayerControlClient();
        var service = new PlayerControlService(playerClient, controlClient);

        // Act
        var outcome = await service.ControlAsync(
            PlayerId.ToUpperInvariant(),
            PlayerControlCommand.Pause,
            TestContext.Current.CancellationToken);

        // Assert
        var succeeded = Assert.IsType<PlayerControlSucceeded>(outcome);
        Assert.Equal(updatedPlayer, succeeded.Player);
        Assert.Equal(PlayerId, controlClient.PlayerId);
        Assert.Equal(PlayerControlCommand.Pause, controlClient.Command);
        Assert.Equal(2, playerClient.CallCount);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task EmptyPlayerShouldBeRejectedWithoutCallingLms(string? playerId)
    {
        // Arrange
        var playerClient = new StubPlayerClient(Player(PlayerPlaybackState.Playing));
        var controlClient = new StubPlayerControlClient();
        var service = new PlayerControlService(playerClient, controlClient);

        // Act
        var outcome = await service.ControlAsync(
            playerId!,
            PlayerControlCommand.Pause,
            TestContext.Current.CancellationToken);

        // Assert
        var rejected = Assert.IsType<PlayerControlRejected>(outcome);
        Assert.Equal(PlayerControlRejectionReason.InvalidPlayer, rejected.Reason);
        Assert.Equal(0, playerClient.CallCount);
        Assert.Null(controlClient.Command);
    }

    [Fact]
    public async Task InvalidActionShouldBeRejectedWithoutCallingLms()
    {
        // Arrange
        var playerClient = new StubPlayerClient(Player(PlayerPlaybackState.Playing));
        var controlClient = new StubPlayerControlClient();
        var service = new PlayerControlService(playerClient, controlClient);

        // Act
        var outcome = await service.ControlAsync(
            PlayerId,
            (PlayerControlCommand)99,
            TestContext.Current.CancellationToken);

        // Assert
        var rejected = Assert.IsType<PlayerControlRejected>(outcome);
        Assert.Equal(PlayerControlRejectionReason.InvalidAction, rejected.Reason);
        Assert.Equal(0, playerClient.CallCount);
        Assert.Null(controlClient.Command);
    }

    [Fact]
    public async Task MissingPlayerShouldBeRejectedBeforeMutation()
    {
        // Arrange
        var playerClient = new StubPlayerClient(Player(PlayerPlaybackState.Playing));
        var controlClient = new StubPlayerControlClient();
        var service = new PlayerControlService(playerClient, controlClient);

        // Act
        var outcome = await service.ControlAsync(
            "66:77:88:99:aa:bb",
            PlayerControlCommand.Next,
            TestContext.Current.CancellationToken);

        // Assert
        var rejected = Assert.IsType<PlayerControlRejected>(outcome);
        Assert.Equal(PlayerControlRejectionReason.PlayerNotFound, rejected.Reason);
        Assert.Equal(1, playerClient.CallCount);
        Assert.Null(controlClient.Command);
    }

    [Fact]
    public async Task MissingPlayerAfterMutationShouldReportAnUpstreamFailure()
    {
        // Arrange
        var playerClient = new StubPlayerClient(Player(PlayerPlaybackState.Playing));
        var controlClient = new StubPlayerControlClient();
        var service = new PlayerControlService(playerClient, controlClient);

        // Act
        var exception = await Assert.ThrowsAsync<LmsRequestException>(() =>
            service.ControlAsync(
                PlayerId,
                PlayerControlCommand.Stop,
                TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(
            "The selected LMS player was no longer available after it was controlled.",
            exception.Message);
        Assert.Equal(PlayerControlCommand.Stop, controlClient.Command);
        Assert.Equal(2, playerClient.CallCount);
    }

    private static LmsPlayerStatus Player(PlayerPlaybackState playbackState) =>
        new(PlayerId, "North Room", true, playbackState);

    private sealed class StubPlayerClient(params LmsPlayerStatus[] results)
        : ILmsPlayerClient
    {
        public int CallCount { get; private set; }

        public Task<IReadOnlyList<LmsPlayerStatus>> GetPlayersAsync(
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = CallCount < results.Length
                ? new[] { results[CallCount] }
                : Array.Empty<LmsPlayerStatus>();
            CallCount++;
            return Task.FromResult<IReadOnlyList<LmsPlayerStatus>>(result);
        }
    }

    private sealed class StubPlayerControlClient : ILmsPlayerControlClient
    {
        public string? PlayerId { get; private set; }

        public PlayerControlCommand? Command { get; private set; }

        public Task ControlAsync(
            string playerId,
            PlayerControlCommand command,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            PlayerId = playerId;
            Command = command;
            return Task.CompletedTask;
        }
    }
}
