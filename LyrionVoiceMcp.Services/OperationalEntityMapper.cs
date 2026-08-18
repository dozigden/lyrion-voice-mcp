using LyrionVoiceMcp.Abstractions;
using LyrionVoiceMcp.Ef.Abstractions.Entities;
using LyrionVoiceMcp.Ef.Abstractions.ErrorLogs;
using LyrionVoiceMcp.Ef.Abstractions.Jobs;
using LyrionVoiceMcp.Ef.Abstractions.ToolCalls;

namespace LyrionVoiceMcp.Services;

internal static class OperationalEntityMapper
{
    public static Job ToModel(EntityJob entity) => new(
        entity.Id,
        entity.Type,
        ToModel(entity.Status),
        ToDateTimeOffset(entity.RunAfterUtc),
        entity.PayloadJson,
        entity.ResultJson,
        entity.ErrorMessage,
        ToNullableDateTimeOffset(entity.StartedAtUtc),
        ToNullableDateTimeOffset(entity.CompletedAtUtc),
        entity.CorrelationId,
        ToDateTimeOffset(entity.CreatedAtUtc),
        ToDateTimeOffset(entity.UpdatedAtUtc));

    public static JobSummary ToModel(EntityJobSummary entity) => new(
        entity.Id,
        entity.Type,
        ToModel(entity.Status),
        ToDateTimeOffset(entity.RunAfterUtc),
        ToNullableDateTimeOffset(entity.StartedAtUtc),
        ToNullableDateTimeOffset(entity.CompletedAtUtc),
        entity.CorrelationId,
        ToDateTimeOffset(entity.CreatedAtUtc),
        ToDateTimeOffset(entity.UpdatedAtUtc));

    public static JobLog ToModel(EntityJobLog entity) => new(
        entity.Id,
        entity.JobId,
        ToModel(entity.Level),
        entity.Message,
        entity.DataJson,
        ToDateTimeOffset(entity.LoggedAtUtc));

    public static ErrorLog ToModel(EntityErrorLog entity) => new(
        entity.Id,
        entity.ReportId,
        ToDateTimeOffset(entity.OccurredAtUtc),
        entity.Source,
        entity.Area,
        entity.ExceptionType,
        entity.Message,
        entity.StackTrace,
        entity.TraceIdentifier,
        entity.RequestMethod,
        entity.RequestPath,
        entity.JobId,
        entity.ContextJson,
        ToDateTimeOffset(entity.CreatedAtUtc));

    public static ErrorLogSummary ToModel(EntityErrorLogSummary entity) => new(
        entity.Id,
        ToDateTimeOffset(entity.OccurredAtUtc),
        entity.Source,
        entity.Area,
        entity.ExceptionType,
        entity.Message,
        entity.TraceIdentifier,
        entity.JobId);

    public static ToolCall ToModel(EntityToolCall entity) => new(
        entity.ToolCallId,
        entity.ToolName,
        ToModel(entity.Status),
        ToDateTimeOffset(entity.StartedAtUtc),
        ToNullableDateTimeOffset(entity.CompletedAtUtc),
        entity.DurationMilliseconds,
        entity.ArgumentsJson,
        entity.ArgumentsTruncated,
        entity.ResultJson,
        entity.ResultTruncated,
        entity.ErrorMessage,
        entity.TraceIdentifier,
        entity.ErrorLogId);

    public static ToolCallSummary ToModel(EntityToolCallSummary entity) => new(
        entity.ToolCallId,
        entity.ToolName,
        ToModel(entity.Status),
        ToDateTimeOffset(entity.StartedAtUtc),
        ToNullableDateTimeOffset(entity.CompletedAtUtc),
        entity.DurationMilliseconds,
        entity.TraceIdentifier,
        entity.ErrorLogId);

    public static EntityJobStatus ToEntity(JobStatus status) => status switch
    {
        JobStatus.Pending => EntityJobStatus.Pending,
        JobStatus.Running => EntityJobStatus.Running,
        JobStatus.Completed => EntityJobStatus.Completed,
        JobStatus.Failed => EntityJobStatus.Failed,
        JobStatus.Cancelled => EntityJobStatus.Cancelled,
        _ => throw new InvalidOperationException($"Unsupported job status '{status}'.")
    };

    public static EntityJobLogLevel ToEntity(JobLogLevel level) => level switch
    {
        JobLogLevel.Information => EntityJobLogLevel.Information,
        JobLogLevel.Warning => EntityJobLogLevel.Warning,
        JobLogLevel.Error => EntityJobLogLevel.Error,
        _ => throw new InvalidOperationException($"Unsupported job log level '{level}'.")
    };

    public static EntityToolCallStatus ToEntity(ToolCallStatus status) => status switch
    {
        ToolCallStatus.Running => EntityToolCallStatus.Running,
        ToolCallStatus.Succeeded => EntityToolCallStatus.Succeeded,
        ToolCallStatus.ToolError => EntityToolCallStatus.ToolError,
        ToolCallStatus.Cancelled => EntityToolCallStatus.Cancelled,
        ToolCallStatus.Failed => EntityToolCallStatus.Failed,
        ToolCallStatus.Interrupted => EntityToolCallStatus.Interrupted,
        _ => throw new InvalidOperationException($"Unsupported tool-call status '{status}'.")
    };

    public static bool TryGetEntityId(long id, out int entityId)
    {
        if (id is < 1 or > int.MaxValue)
        {
            entityId = 0;
            return false;
        }

        entityId = (int)id;
        return true;
    }

    public static DateTime ToUtcDateTime(DateTimeOffset value) => value.UtcDateTime;

    private static JobStatus ToModel(EntityJobStatus status) => status switch
    {
        EntityJobStatus.Pending => JobStatus.Pending,
        EntityJobStatus.Running => JobStatus.Running,
        EntityJobStatus.Completed => JobStatus.Completed,
        EntityJobStatus.Failed => JobStatus.Failed,
        EntityJobStatus.Cancelled => JobStatus.Cancelled,
        _ => throw new InvalidOperationException($"Unsupported persisted job status '{status}'.")
    };

    private static JobLogLevel ToModel(EntityJobLogLevel level) => level switch
    {
        EntityJobLogLevel.Information => JobLogLevel.Information,
        EntityJobLogLevel.Warning => JobLogLevel.Warning,
        EntityJobLogLevel.Error => JobLogLevel.Error,
        _ => throw new InvalidOperationException($"Unsupported persisted job log level '{level}'.")
    };

    private static ToolCallStatus ToModel(EntityToolCallStatus status) => status switch
    {
        EntityToolCallStatus.Running => ToolCallStatus.Running,
        EntityToolCallStatus.Succeeded => ToolCallStatus.Succeeded,
        EntityToolCallStatus.ToolError => ToolCallStatus.ToolError,
        EntityToolCallStatus.Cancelled => ToolCallStatus.Cancelled,
        EntityToolCallStatus.Failed => ToolCallStatus.Failed,
        EntityToolCallStatus.Interrupted => ToolCallStatus.Interrupted,
        _ => throw new InvalidOperationException($"Unsupported persisted tool-call status '{status}'.")
    };

    private static DateTimeOffset ToDateTimeOffset(DateTime value) =>
        new(DateTime.SpecifyKind(value, DateTimeKind.Utc));

    private static DateTimeOffset? ToNullableDateTimeOffset(DateTime? value) =>
        value is null ? null : ToDateTimeOffset(value.Value);
}
