namespace LyrionVoiceMcp.Ef;

public sealed record ApplicationDatabaseSettings(string DatabasePath)
{
    public static ApplicationDatabaseSettings FromValues(
        string contentRootPath,
        string? databasePath)
    {
        var configured = string.IsNullOrWhiteSpace(databasePath)
            ? Path.Combine(contentRootPath, "..", ".data", "lyrion-voice-mcp.db")
            : databasePath.Trim();
        return new ApplicationDatabaseSettings(Path.IsPathRooted(configured)
            ? Path.GetFullPath(configured)
            : Path.GetFullPath(configured, contentRootPath));
    }
}
