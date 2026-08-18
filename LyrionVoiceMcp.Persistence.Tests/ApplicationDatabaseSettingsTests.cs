using LyrionVoiceMcp.Ef;

namespace LyrionVoiceMcp.Persistence.Tests;

public sealed class ApplicationDatabaseSettingsTests
{
    [Fact]
    public void RelativeDatabasePathShouldResolveAgainstContentRoot()
    {
        var contentRoot = Path.Combine(Path.GetTempPath(), "lyrion-settings-root");

        var settings = ApplicationDatabaseSettings.FromValues(
            contentRoot,
            Path.Combine("data", "application.db"));

        Assert.Equal(
            Path.GetFullPath(Path.Combine(contentRoot, "data", "application.db")),
            settings.DatabasePath);
    }

    [Fact]
    public void MissingDatabasePathShouldUseSiblingDataDirectory()
    {
        var contentRoot = Path.Combine(Path.GetTempPath(), "lyrion-settings-root", "api");

        var settings = ApplicationDatabaseSettings.FromValues(contentRoot, null);

        Assert.Equal(
            Path.GetFullPath(Path.Combine(contentRoot, "..", ".data", "lyrion-voice-mcp.db")),
            settings.DatabasePath);
    }
}
