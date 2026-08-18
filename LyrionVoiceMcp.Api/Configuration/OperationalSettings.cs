using LyrionVoiceMcp.Abstractions;

namespace LyrionVoiceMcp.Api.Configuration;

public sealed record OperationalSettings(
    int JobRetentionDays,
    int ErrorRetentionDays,
    int ToolCallRetentionDays,
    int ToolCallJsonMaximumCharacters,
    TimeZoneInfo TimeZone)
{
    public const int DefaultJobRetentionDays = 90;
    public const int DefaultErrorRetentionDays = 90;
    public const int DefaultToolCallRetentionDays = 30;
    public const int DefaultToolCallJsonMaximumCharacters = 262_144;

    public static OperationalSettings FromValues(
        string? jobRetentionDays,
        string? errorRetentionDays,
        string? toolCallRetentionDays,
        string? toolCallJsonMaximumCharacters,
        string? timeZoneId) => new(
        PositiveOrDefault(jobRetentionDays, DefaultJobRetentionDays, "job retention"),
        PositiveOrDefault(errorRetentionDays, DefaultErrorRetentionDays, "error retention"),
        PositiveOrDefault(
            toolCallRetentionDays,
            DefaultToolCallRetentionDays,
            "tool-call retention"),
        PositiveOrDefault(
            toolCallJsonMaximumCharacters,
            DefaultToolCallJsonMaximumCharacters,
            "tool-call JSON maximum characters"),
        ResolveTimeZone(timeZoneId));

    public OperationalPolicy ToPolicy() => new(
        JobRetentionDays,
        ErrorRetentionDays,
        ToolCallRetentionDays,
        ToolCallJsonMaximumCharacters,
        TimeZone);

    public static OperationalSchedulePolicy CreateSchedulePolicy(
        bool catalogueRefreshEnabled,
        string? catalogueRefreshCron,
        bool errorLogPurgeEnabled,
        string? errorLogPurgeCron,
        bool jobHistoryPurgeEnabled,
        string? jobHistoryPurgeCron,
        bool toolCallHistoryPurgeEnabled,
        string? toolCallHistoryPurgeCron) => new(
        new(catalogueRefreshEnabled, CronOrDefault(catalogueRefreshCron, "0 3 * * *")),
        new(errorLogPurgeEnabled, CronOrDefault(errorLogPurgeCron, "15 3 * * *")),
        new(jobHistoryPurgeEnabled, CronOrDefault(jobHistoryPurgeCron, "30 3 * * *")),
        new(toolCallHistoryPurgeEnabled, CronOrDefault(toolCallHistoryPurgeCron, "45 3 * * *")));

    private static int PositiveOrDefault(string? value, int defaultValue, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return defaultValue;
        }

        if (!int.TryParse(value, out var parsed) || parsed < 1)
        {
            throw new InvalidOperationException(
                $"The configured {name} must be a positive integer.");
        }

        return parsed;
    }

    private static TimeZoneInfo ResolveTimeZone(string? value)
    {
        var id = string.IsNullOrWhiteSpace(value) ? TimeZoneInfo.Local.Id : value.Trim();
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(id);
        }
        catch (TimeZoneNotFoundException exception)
        {
            throw new InvalidOperationException(
                $"The configured operational time zone '{id}' was not found.",
                exception);
        }
        catch (InvalidTimeZoneException exception)
        {
            throw new InvalidOperationException(
                $"The configured operational time zone '{id}' is invalid.",
                exception);
        }
    }

    private static string CronOrDefault(string? value, string defaultValue) =>
        string.IsNullOrWhiteSpace(value) ? defaultValue : value.Trim();
}
