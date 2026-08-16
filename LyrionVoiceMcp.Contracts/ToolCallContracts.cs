namespace LyrionVoiceMcp.Contracts;

public sealed record ToolCallPageResponse(
    IReadOnlyList<ToolCallSummaryResponse> Items,
    int Total,
    int Offset,
    int Limit,
    int RetentionDays);

public sealed record ToolCallSummaryResponse(
    string Id,
    string ToolName,
    string Status,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt,
    long? DurationMilliseconds,
    string? TraceIdentifier,
    long? ErrorLogId);

public sealed record ToolCallResponse(
    string Id,
    string ToolName,
    string Status,
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
