using LyrionVoiceMcp.Dev;

namespace LyrionVoiceMcp.Dev.Tests;

public sealed class DevTestAgentTests
{
    [Fact]
    public void LaunchDisablesInheritedTransportsAndRequiresLocalLyrion()
    {
        const string workspace = "/tmp/local test workspace";
        var start = DevTestAgent.CreateStartInfo("codex", workspace, "lyrion_dev_test",
            [new("deployed.lyrion", false), new("local-tool", true)]);

        var config = Assert.Single(start.ArgumentList, value => value.StartsWith("mcp_servers=", StringComparison.Ordinal));
        Assert.Contains("\"deployed.lyrion\"={url=\"http://127.0.0.1:5600/mcp\",enabled=false}", config);
        Assert.Contains("\"local-tool\"={command=\"false\",enabled=false}", config);
        Assert.Contains("\"lyrion_dev_test\"={url=\"http://127.0.0.1:5600/mcp\",enabled=true,required=true}", config);
        Assert.Equal(workspace, start.WorkingDirectory);
        Assert.Contains("--no-daemon", start.ArgumentList);
        Assert.Contains("read-only", start.ArgumentList);
        Assert.Contains("get_player_status", start.ArgumentList.Last());
        Assert.False(start.RedirectStandardInput);
        Assert.False(start.RedirectStandardOutput);
    }
}
