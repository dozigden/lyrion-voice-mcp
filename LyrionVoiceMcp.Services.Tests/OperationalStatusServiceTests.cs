using LyrionVoiceMcp.Services;
using LyrionVoiceMcp.Abstractions;

namespace LyrionVoiceMcp.Services.Tests;

public sealed class OperationalStatusServiceTests
{
    [Fact]
    public void GetStatusShouldReportOk()
    {
        // Arrange
        var service = new OperationalStatusService();

        // Act
        var result = service.GetStatus();

        // Assert
        Assert.Equal("ok", result.Status);
    }
}

public sealed class LmsConnectionStatusServiceTests
{
    [Fact]
    public async Task GetStatusShouldDelegateToTheLmsProbe()
    {
        // Arrange
        var expected = new LmsConnectionStatus(
            LmsConnectionState.Online,
            "development",
            "http://music.test:9000",
            "9.0.1",
            "Connected.");
        var service = new LmsConnectionStatusService(new StubLmsConnectionProbe(expected));

        // Act
        var result = await service.GetStatusAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Same(expected, result);
    }

    private sealed class StubLmsConnectionProbe(
        LmsConnectionStatus result) : ILmsConnectionProbe
    {
        public Task<LmsConnectionStatus> CheckAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(result);
        }
    }
}
