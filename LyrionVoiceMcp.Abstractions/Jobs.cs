namespace LyrionVoiceMcp.Abstractions;

public static class JobTypes
{
    public const string CatalogueRefresh = "catalogue.refresh";
    public const string SearchIndexRebuild = "search-index.rebuild";
    public const string ErrorLogPurge = "error-log.purge";
    public const string JobHistoryPurge = "job-history.purge";
    public const string ToolCallHistoryPurge = "tool-call-history.purge";
}

public enum JobStatus
{
    Pending,
    Running,
    Completed,
    Failed,
    Cancelled
}

public enum JobLogLevel
{
    Information,
    Warning,
    Error
}

public sealed record Job(
    long Id,
    string Type,
    JobStatus Status,
    DateTimeOffset RunAfter,
    string PayloadJson,
    string ResultJson,
    string? ErrorMessage,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    string? CorrelationId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record JobLog(
    long Id,
    long JobId,
    JobLogLevel Level,
    string Message,
    string? DataJson,
    DateTimeOffset LoggedAt);

public sealed record JobDetails(
    Job Job,
    IReadOnlyList<JobLog> Logs);

public sealed record JobSummary(
    long Id,
    string Type,
    JobStatus Status,
    DateTimeOffset RunAfter,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    string? CorrelationId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record JobQuery(
    int Offset = 0,
    int Limit = 100,
    string? Type = null,
    JobStatus? Status = null);

public sealed record JobPage(
    IReadOnlyList<JobSummary> Items,
    int Total,
    int Offset,
    int Limit);

public sealed record CreateJob(
    string Type,
    string PayloadJson,
    DateTimeOffset RunAfter,
    string? CorrelationId);

public sealed class JobConflictException(string message, Exception? innerException = null)
    : Exception(message, innerException);

public sealed record ScheduledJobState(
    string Name,
    DateTimeOffset LastRunAt,
    DateTimeOffset? LastEvaluatedAt);

public interface IJobStore
{
    Task<Job> CreateAsync(CreateJob request, DateTimeOffset now, CancellationToken cancellationToken);

    Task<JobPage> BrowseAsync(JobQuery query, CancellationToken cancellationToken);

    Task<JobDetails?> GetAsync(long id, CancellationToken cancellationToken);

    Task<Job?> GetLatestActiveByTypeAsync(string type, CancellationToken cancellationToken);

    Task<Job?> GetLatestByTypeAsync(string type, CancellationToken cancellationToken);

    Task<Job?> GetLatestActiveByCorrelationPrefixesAsync(
        string firstPrefix,
        string secondPrefix,
        CancellationToken cancellationToken);

    Task<Job?> GetLatestStartedByCorrelationPrefixesAsync(
        string firstPrefix,
        string secondPrefix,
        CancellationToken cancellationToken);

    Task<bool> ExistsByCorrelationIdAsync(string correlationId, CancellationToken cancellationToken);

    Task<IReadOnlyList<Job>> MarkRunningInterruptedAsync(
        DateTimeOffset completedAt,
        string message,
        CancellationToken cancellationToken);

    Task<Job?> TryStartNextDueAsync(DateTimeOffset now, CancellationToken cancellationToken);

    Task<bool> CompleteAsync(
        long id,
        string resultJson,
        DateTimeOffset completedAt,
        CancellationToken cancellationToken);

    Task<bool> FailAsync(
        long id,
        string errorMessage,
        string resultJson,
        DateTimeOffset completedAt,
        CancellationToken cancellationToken);

    Task<bool> RequeueAsync(
        long id,
        string resultJson,
        DateTimeOffset runAfter,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken);

    Task<bool> CancelAsync(
        long id,
        JobStatus expectedStatus,
        string resultJson,
        DateTimeOffset completedAt,
        CancellationToken cancellationToken);

    Task AppendLogAsync(
        long jobId,
        JobLogLevel level,
        string message,
        string? dataJson,
        DateTimeOffset loggedAt,
        CancellationToken cancellationToken);

    Task<int> DeleteTerminalBatchBeforeAsync(
        DateTimeOffset completedBefore,
        long excludingJobId,
        int batchSize,
        CancellationToken cancellationToken);

    Task<ScheduledJobState?> GetScheduledJobStateAsync(
        string name,
        CancellationToken cancellationToken);

    Task UpsertScheduledJobStateAsync(
        ScheduledJobState state,
        CancellationToken cancellationToken);
}

public interface IJobService
{
    int RetentionDays { get; }

    Task<JobPage> BrowseAsync(JobQuery query, CancellationToken cancellationToken);

    Task<JobDetails?> GetAsync(long id, CancellationToken cancellationToken);

    Task<JobEnqueueOutcome> EnqueueAsync(
        CreateJob request,
        CancellationToken cancellationToken);

    Task<JobCancellationOutcome> RequestCancellationAsync(
        long id,
        CancellationToken cancellationToken);
}

public abstract record JobEnqueueOutcome;

public sealed record JobEnqueued(Job Job) : JobEnqueueOutcome;

public sealed record JobEnqueueRejected(string Message) : JobEnqueueOutcome;

public abstract record JobCancellationOutcome;

public sealed record JobCancellationAccepted(Job Job) : JobCancellationOutcome;

public sealed record JobCancellationRejected(string Message) : JobCancellationOutcome;

public interface IJobHandler
{
    string Type { get; }

    Task<JobHandlerResult> HandleAsync(
        JobContext context,
        CancellationToken cancellationToken);
}

public abstract class JobHandlerBase<TPayload> : IJobHandler
{
    public abstract string Type { get; }

    public async Task<JobHandlerResult> HandleAsync(
        JobContext context,
        CancellationToken cancellationToken)
    {
        TPayload? payload;
        try
        {
            payload = System.Text.Json.JsonSerializer.Deserialize<TPayload>(context.PayloadJson);
        }
        catch (System.Text.Json.JsonException)
        {
            return JobHandlerResult.Failed("Invalid JSON payload.");
        }

        if (payload is null)
        {
            return JobHandlerResult.Failed("Invalid JSON payload.");
        }

        return await HandleAsync(context, payload, cancellationToken);
    }

    protected abstract Task<JobHandlerResult> HandleAsync(
        JobContext context,
        TPayload payload,
        CancellationToken cancellationToken);
}

public sealed record JobContext(
    long JobId,
    string Type,
    string PayloadJson);

public sealed record JobHandlerResult(
    bool Success,
    string ResultJson,
    string? ErrorMessage,
    bool ShouldFinalise,
    DateTimeOffset? RunAfter)
{
    public static JobHandlerResult Succeeded(string resultJson = "{}") =>
        new(true, resultJson, null, true, null);

    public static JobHandlerResult Failed(string errorMessage, string resultJson = "{}") =>
        new(false, resultJson, errorMessage, true, null);

    public static JobHandlerResult Requeued(DateTimeOffset runAfter, string resultJson = "{}") =>
        new(true, resultJson, null, false, runAfter);
}

public interface IJobRunner
{
    Task MarkRunningJobsFailedAsync(CancellationToken cancellationToken);

    Task<bool> RunNextDueAsync(CancellationToken cancellationToken);
}

public interface IJobCancellationRegistry
{
    CancellationToken Register(long jobId, CancellationToken stoppingToken);

    void Unregister(long jobId);

    bool RequestCancellation(long jobId);

    bool IsCancellationRequested(long jobId);
}

public interface IJobLifecycleGate
{
    Task ExecuteAsync(
        Func<CancellationToken, Task> operation,
        CancellationToken cancellationToken);

    Task<TResult> ExecuteAsync<TResult>(
        Func<CancellationToken, Task<TResult>> operation,
        CancellationToken cancellationToken);
}

public interface IJobLogWriter
{
    Task WriteAsync(
        long jobId,
        JobLogLevel level,
        string message,
        object? data,
        CancellationToken cancellationToken);
}

public interface IScheduledJobService
{
    Task<IReadOnlyList<ScheduledJob>> ListAsync(CancellationToken cancellationToken);

    Task<ScheduledJobRunOutcome> RunNowAsync(
        string scheduleName,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<ScheduledJobEnqueueResult>> EnqueueDueJobsAsync(
        CancellationToken cancellationToken);
}

public interface IScheduledJobDefinition
{
    string Name { get; }

    string DisplayName { get; }

    string SchedulerStateName { get; }

    Task<ScheduledJobConfiguration> GetConfigurationAsync(
        CancellationToken cancellationToken);

    Task<IReadOnlyList<ScheduledJobOccurrence>> CreateOccurrencesAsync(
        DateTimeOffset dueAt,
        CancellationToken cancellationToken);
}

public interface ICronOccurrenceCalculator
{
    DateTimeOffset? GetLatestOccurrence(
        string cronExpression,
        TimeZoneInfo timeZone,
        DateTimeOffset fromExclusive,
        DateTimeOffset throughInclusive);

    DateTimeOffset? GetNextOccurrence(
        string cronExpression,
        TimeZoneInfo timeZone,
        DateTimeOffset after);
}

public sealed record ScheduledJobConfiguration(
    bool Enabled,
    string CronExpression,
    bool RunOnInitialisation = false);

public sealed record ScheduledJobOccurrence(
    string JobType,
    DateTimeOffset DueAt,
    string PayloadJson,
    string CorrelationId);

public sealed record ScheduledJobEnqueueResult(
    string ScheduleName,
    DateTimeOffset DueAt,
    string CorrelationId,
    bool Enqueued,
    long? JobId);

public sealed record ScheduledJobRun(
    long Id,
    JobStatus Status,
    DateTimeOffset? StartedAt);

public sealed record ScheduledJob(
    string Name,
    string DisplayName,
    bool Enabled,
    string CronExpression,
    string TimeZoneId,
    DateTimeOffset? LastEvaluatedAt,
    DateTimeOffset? NextOccurrenceAt,
    ScheduledJobRun? CurrentJob,
    ScheduledJobRun? LastStartedJob);

public abstract record ScheduledJobRunOutcome;

public sealed record ScheduledJobRunStarted(
    int EnqueuedCount,
    IReadOnlyList<long> JobIds) : ScheduledJobRunOutcome;

public sealed record ScheduledJobRunRejected(string Message) : ScheduledJobRunOutcome;
