namespace LyrionVoiceMcp.Contracts;

public sealed record JobPageResponse(
    IReadOnlyList<JobSummaryResponse> Items,
    int Total,
    int Offset,
    int Limit,
    int RetentionDays);

public sealed record JobSummaryResponse(
    long Id,
    string Type,
    string Status,
    DateTimeOffset RunAfter,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    string? CorrelationId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record JobResponse(
    long Id,
    string Type,
    string Status,
    DateTimeOffset RunAfter,
    string PayloadJson,
    string ResultJson,
    string? ErrorMessage,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    string? CorrelationId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record JobLogResponse(
    long Id,
    string Level,
    string Message,
    string? DataJson,
    DateTimeOffset LoggedAt);

public sealed record JobDetailsResponse(
    JobResponse Job,
    IReadOnlyList<JobLogResponse> Logs);

public sealed record ScheduledJobRunResponse(
    long Id,
    string Status,
    DateTimeOffset? StartedAt);

public sealed record ScheduledJobResponse(
    string Name,
    string DisplayName,
    bool Enabled,
    string CronExpression,
    string TimeZoneId,
    DateTimeOffset? LastEvaluatedAt,
    DateTimeOffset? NextOccurrenceAt,
    ScheduledJobRunResponse? CurrentJob,
    ScheduledJobRunResponse? LastStartedJob);

public sealed record ScheduledJobRunNowResponse(
    int EnqueuedCount,
    IReadOnlyList<long> JobIds);

public sealed record ApiErrorResponse(string Message);
