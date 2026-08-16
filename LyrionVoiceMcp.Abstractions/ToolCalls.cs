namespace LyrionVoiceMcp.Abstractions;

public enum ToolCallStatus
{
    Running,
    Succeeded,
    ToolError,
    Cancelled,
    Failed,
    Interrupted
}

public sealed record ToolCall(
    string Id,
    string ToolName,
    ToolCallStatus Status,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt,
    long? DurationMilliseconds,
    string ArgumentsJson,
    bool ArgumentsTruncated,
    string? ResultJson,
    bool ResultTruncated,
    string? ErrorMessage,
    string? TraceIdentifier,
    long? ErrorLogId);

public sealed record ToolCallSummary(
    string Id,
    string ToolName,
    ToolCallStatus Status,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt,
    long? DurationMilliseconds,
    string? TraceIdentifier,
    long? ErrorLogId);

public sealed record ToolCallQuery(
    int Offset = 0,
    int Limit = 100,
    string? ToolName = null,
    ToolCallStatus? Status = null);

public sealed record ToolCallPage(
    IReadOnlyList<ToolCallSummary> Items,
    int Total,
    int Offset,
    int Limit);

public interface IToolCallStore
{
    Task StartAsync(ToolCallStart entry, CancellationToken cancellationToken);

    Task CompleteAsync(ToolCallCompletion completion, CancellationToken cancellationToken);

    Task<int> MarkRunningInterruptedAsync(
        DateTimeOffset completedAt,
        CancellationToken cancellationToken);

    Task<ToolCallPage> BrowseAsync(ToolCallQuery query, CancellationToken cancellationToken);

    Task<ToolCall?> GetAsync(string id, CancellationToken cancellationToken);

    Task<int> DeleteOlderThanAsync(
        DateTimeOffset cutoff,
        int batchSize,
        CancellationToken cancellationToken);
}

public sealed record ToolCallStart(
    string Id,
    string ToolName,
    DateTimeOffset StartedAt,
    string ArgumentsJson,
    bool ArgumentsTruncated,
    string? TraceIdentifier);

public sealed record ToolCallCompletion(
    string Id,
    ToolCallStatus Status,
    DateTimeOffset CompletedAt,
    long DurationMilliseconds,
    string? ResultJson,
    bool ResultTruncated,
    string? ErrorMessage,
    long? ErrorLogId);

public sealed record BoundedJson(string Json, bool Truncated);
public sealed record ToolCallRecording(string Id, DateTimeOffset StartedAt);

public interface IToolCallHistoryService
{
    int RetentionDays { get; }

    BoundedJson BoundJson(string json);

    Task<ToolCallRecording?> StartAsync(
        string toolName,
        string argumentsJson,
        string? traceIdentifier,
        CancellationToken cancellationToken);

    Task CompleteAsync(
        string id,
        ToolCallStatus status,
        DateTimeOffset startedAt,
        string? resultJson,
        string? errorMessage,
        long? errorLogId,
        CancellationToken cancellationToken);

    Task<ToolCallPage> BrowseAsync(ToolCallQuery query, CancellationToken cancellationToken);

    Task<ToolCall?> GetAsync(string id, CancellationToken cancellationToken);

    Task<int> PurgeOlderThanAsync(
        DateTimeOffset cutoff,
        CancellationToken cancellationToken);
}
