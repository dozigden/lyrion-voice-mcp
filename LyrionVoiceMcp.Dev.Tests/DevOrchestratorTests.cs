using LyrionVoiceMcp.Dev;

namespace LyrionVoiceMcp.Dev.Tests;

public sealed class DevOrchestratorTests
{
    [Fact]
    public void ApiLaunchSettingsShouldUseTheDocumentedEndpoint()
    {
        // Act
        var environment = DevApiLaunchSettings.CreateEnvironment();
        var arguments = DevApiLaunchSettings.CreateRunArguments("/repo/LyrionVoiceMcp.Api.csproj");

        // Assert
        Assert.Equal("http://127.0.0.1:5600", DevApiLaunchSettings.Endpoint);
        Assert.Equal(5600, DevApiLaunchSettings.Port);
        Assert.Equal(DevApiLaunchSettings.Endpoint, environment["ASPNETCORE_URLS"]);
        Assert.Equal("true", environment[DevApiLaunchSettings.LoadLocalSettingsKey]);
        Assert.Contains("--no-build", arguments);
    }

    [Theory]
    [InlineData(false, false, null, false, "stopped", false)]
    [InlineData(true, true, null, false, "running", false)]
    [InlineData(true, false, 0, false, "stopped", false)]
    [InlineData(true, false, 137, true, "stopped", false)]
    [InlineData(true, false, 2, false, "exit 2", true)]
    public void ProcessStateShouldDescribeObservedState(
        bool hasProcess,
        bool isRunning,
        int? exitCode,
        bool stoppedByUser,
        string expectedText,
        bool expectedFailure)
    {
        // Act
        var result = ServiceProcessState.Resolve(hasProcess, isRunning, exitCode, stoppedByUser);

        // Assert
        Assert.Equal(expectedText, result.Text);
        Assert.Equal(expectedFailure, result.HasFailed);
    }

    [Fact]
    public void RecentLogBufferShouldRetainOnlyItsCapacity()
    {
        // Arrange
        var buffer = new RecentLogBuffer(3);
        buffer.Add("one");
        buffer.Add("two");
        buffer.Add("three");
        buffer.Add("four");

        // Act
        var result = buffer.Tail(2);

        // Assert
        Assert.Equal(3, buffer.Count);
        Assert.Equal(["three", "four"], result);
    }

    [Theory]
    [InlineData("dotnet LyrionVoiceMcp.Api", new[] { "LyrionVoiceMcp.Api" }, true)]
    [InlineData("node vite --port 5175", new[] { "vite" }, true)]
    [InlineData("node another-server", new[] { "vite" }, false)]
    public void ListenerRecognitionShouldRequireEveryFragment(
        string commandLine,
        string[] fragments,
        bool expected)
    {
        // Act
        var result = PortConflictResolver.IsRecognised(commandLine, fragments);

        // Assert
        Assert.Equal(expected, result);
    }
}
