using LyrionVoiceMcp.Lms;

namespace LyrionVoiceMcp.Lms.Tests;

public sealed class LmsConnectionSettingsTests
{
    [Fact]
    public void FromValuesShouldBuildConfiguredJsonRpcEndpoint()
    {
        // Arrange and act
        var settings = LmsConnectionSettings.FromValues(
            " development ",
            "http://music.test:9000/",
            "7");

        // Assert
        Assert.True(settings.IsConfigured);
        Assert.Equal("development", settings.ServerId);
        Assert.Equal("http://music.test:9000/", settings.BaseUrl?.AbsoluteUri);
        Assert.Equal("http://music.test:9000/jsonrpc.js", settings.JsonRpcUrl?.AbsoluteUri);
        Assert.Equal(TimeSpan.FromSeconds(7), settings.RequestTimeout);
    }

    [Fact]
    public void FromValuesShouldAllowAnUnconfiguredDevelopmentRuntime()
    {
        // Arrange and act
        var settings = LmsConnectionSettings.FromValues(null, null, null);

        // Assert
        Assert.False(settings.IsConfigured);
        Assert.Equal(TimeSpan.FromSeconds(5), settings.RequestTimeout);
    }

    [Theory]
    [InlineData(null, "http://music.test:9000", null)]
    [InlineData("development", null, null)]
    [InlineData("development", "ftp://music.test", null)]
    [InlineData("development", "http://user:pass@music.test", null)]
    [InlineData("development", "http://music.test/path", null)]
    [InlineData("development", "http://music.test?query=yes", null)]
    [InlineData("development", "http://music.test", "0")]
    [InlineData("development", "http://music.test", "31")]
    public void FromValuesShouldRejectInvalidConfiguration(
        string? serverId,
        string? baseUrl,
        string? requestTimeoutSeconds)
    {
        // Arrange and act
        var action = () => LmsConnectionSettings.FromValues(
            serverId,
            baseUrl,
            requestTimeoutSeconds);

        // Assert
        Assert.Throws<InvalidOperationException>(action);
    }
}
