namespace LyrionVoiceMcp.Persistence;

public sealed record CatalogueSettings(string DatabasePath)
{
    public static CatalogueSettings FromValues(
        string contentRootPath,
        string? databasePath)
    {
        var configuredPath = string.IsNullOrWhiteSpace(databasePath)
            ? Path.Combine("..", ".data", "catalogue.db")
            : databasePath.Trim();
        var resolvedPath = Path.IsPathRooted(configuredPath)
            ? Path.GetFullPath(configuredPath)
            : Path.GetFullPath(configuredPath, contentRootPath);
        return new CatalogueSettings(resolvedPath);
    }
}
