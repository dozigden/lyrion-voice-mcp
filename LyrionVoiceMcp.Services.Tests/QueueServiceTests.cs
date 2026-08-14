using LyrionVoiceMcp.Abstractions;
using LyrionVoiceMcp.Services;

namespace LyrionVoiceMcp.Services.Tests;

public sealed class QueueServiceTests
{
    private const string PlayerId = "00:11:22:33:44:55";

    [Fact]
    public async Task GetQueueShouldResolvePlayerBeforeReadingItsQueue()
    {
        // Arrange
        var queue = new LmsPlayerQueue(
            PlayerId,
            0,
            [new(0, "Lantern Signals", "The Paper Comets", "Night Routes", 244.25)]);
        var queueClient = new StubQueueClient(queue);
        var service = new QueueService(
            new StubPlayerClient([Player()]),
            queueClient);

        // Act
        var outcome = await service.GetQueueAsync(
            PlayerId.ToUpperInvariant(),
            TestContext.Current.CancellationToken);

        // Assert
        var succeeded = Assert.IsType<QueueSucceeded>(outcome);
        Assert.Equal(queue, succeeded.Queue);
        Assert.Equal(PlayerId, queueClient.PlayerId);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task EmptyPlayerShouldBeRejectedWithoutCallingLms(string? playerId)
    {
        // Arrange
        var playerClient = new StubPlayerClient([Player()]);
        var queueClient = new StubQueueClient(new(PlayerId, null, []));
        var service = new QueueService(playerClient, queueClient);

        // Act
        var outcome = await service.GetQueueAsync(
            playerId!,
            TestContext.Current.CancellationToken);

        // Assert
        var rejected = Assert.IsType<QueueRejected>(outcome);
        Assert.Equal(QueueRejectionReason.InvalidPlayer, rejected.Reason);
        Assert.Equal(0, playerClient.CallCount);
        Assert.Null(queueClient.PlayerId);
    }

    [Fact]
    public async Task MissingPlayerShouldBeRejectedWithoutReadingAQueue()
    {
        // Arrange
        var queueClient = new StubQueueClient(new(PlayerId, null, []));
        var service = new QueueService(
            new StubPlayerClient([Player()]),
            queueClient);

        // Act
        var outcome = await service.GetQueueAsync(
            "66:77:88:99:aa:bb",
            TestContext.Current.CancellationToken);

        // Assert
        var rejected = Assert.IsType<QueueRejected>(outcome);
        Assert.Equal(QueueRejectionReason.PlayerNotFound, rejected.Reason);
        Assert.Null(queueClient.PlayerId);
    }

    private static LmsPlayerStatus Player() =>
        new(PlayerId, "North Room", true, PlayerPlaybackState.Playing);

    private sealed class StubPlayerClient(IReadOnlyList<LmsPlayerStatus> players)
        : ILmsPlayerClient
    {
        public int CallCount { get; private set; }

        public Task<IReadOnlyList<LmsPlayerStatus>> GetPlayersAsync(
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;
            return Task.FromResult(players);
        }
    }

    private sealed class StubQueueClient(LmsPlayerQueue queue) : ILmsQueueClient
    {
        public string? PlayerId { get; private set; }

        public Task<LmsPlayerQueue> GetQueueAsync(
            string playerId,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            PlayerId = playerId;
            return Task.FromResult(queue);
        }
    }
}
