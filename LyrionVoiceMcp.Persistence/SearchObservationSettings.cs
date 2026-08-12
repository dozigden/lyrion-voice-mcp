namespace LyrionVoiceMcp.Persistence;

public sealed record SearchObservationSettings(string DatabasePath, int RetentionDays)
{
    public static SearchObservationSettings FromValues(
        string contentRootPath,
        string? databasePath,
        string? retentionDays)
    {
        var configuredPath = string.IsNullOrWhiteSpace(databasePath)
            ? Path.Combine("..", ".data", "search-observations.db")
            : databasePath.Trim();
        var resolvedPath = Path.IsPathRooted(configuredPath)
            ? Path.GetFullPath(configuredPath)
            : Path.GetFullPath(configuredPath, contentRootPath);

        var days = 90;
        if (!string.IsNullOrWhiteSpace(retentionDays)
            && (!int.TryParse(retentionDays, out days) || days is < 1 or > 3650))
        {
            throw new InvalidOperationException(
                "LyrionVoiceMcpObservations:RetentionDays must be between 1 and 3650.");
        }

        return new SearchObservationSettings(resolvedPath, days);
    }
}
