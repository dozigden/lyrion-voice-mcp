namespace LyrionVoiceMcp.Abstractions;

public interface IOperationalStoreInitialiser
{
    Task InitialiseAsync(CancellationToken cancellationToken);
}

public sealed record OperationalPolicy(
    int JobRetentionDays,
    int ErrorRetentionDays,
    int ToolCallRetentionDays,
    int ToolCallJsonMaximumCharacters,
    TimeZoneInfo TimeZone);

public sealed record OperationalSchedule(
    bool Enabled,
    string CronExpression,
    bool RunOnInitialisation = false);

public sealed record OperationalSchedulePolicy(
    OperationalSchedule CatalogueRefresh,
    OperationalSchedule ErrorLogPurge,
    OperationalSchedule JobHistoryPurge,
    OperationalSchedule ToolCallHistoryPurge);
