using LyrionVoiceMcp.Abstractions;
using LyrionVoiceMcp.Services;

namespace LyrionVoiceMcp.Services.Tests;

public sealed class PlayerStatusServiceTests
{
    [Fact]
    public async Task GetPlayersShouldDelegateToLmsClient()
    {
        // Arrange
        IReadOnlyList<LmsPlayerStatus> expected =
        [
            new("00:11:22:33:44:55", "North Room", true, PlayerPlaybackState.Stopped)
        ];
        var service = new PlayerStatusService(new StubLmsPlayerClient(expected));

        // Act
        var result = await service.GetPlayersAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Same(expected, result);
    }

    private sealed class StubLmsPlayerClient(
        IReadOnlyList<LmsPlayerStatus> result) : ILmsPlayerClient
    {
        public Task<IReadOnlyList<LmsPlayerStatus>> GetPlayersAsync(
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(result);
        }
    }
}
