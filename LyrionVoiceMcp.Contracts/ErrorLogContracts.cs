namespace LyrionVoiceMcp.Contracts;

public sealed record ErrorLogPageResponse(
    IReadOnlyList<ErrorLogSummaryResponse> Items,
    int Total,
    int Offset,
    int Limit,
    int RetentionDays);

public sealed record ErrorLogSummaryResponse(
    long Id,
    DateTimeOffset OccurredAt,
    string Source,
    string Area,
    string ExceptionType,
    string Message,
    string? TraceIdentifier,
    long? JobId);

public sealed record ErrorLogResponse(
    long Id,
    Guid? ReportId,
    DateTimeOffset OccurredAt,
    string Source,
    string Area,
    string ExceptionType,
    string Message,
    string? StackTrace,
    string? TraceIdentifier,
    string? RequestMethod,
    string? RequestPath,
    long? JobId,
    string? ContextJson,
    DateTimeOffset CreatedAt);
