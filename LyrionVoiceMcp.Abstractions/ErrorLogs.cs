namespace LyrionVoiceMcp.Abstractions;

public static class ErrorLogSources
{
    public const string Backend = "backend";
    public const string Mcp = "mcp";
}

public static class ErrorLogAreas
{
    public const string ApiRequest = "api-request";
    public const string JobRunner = "job-runner";
    public const string JobScheduler = "job-scheduler";
    public const string McpToolCall = "mcp-tool-call";
}

public sealed record ErrorLogContext(
    string Source,
    string Area,
    string? TraceIdentifier = null,
    string? RequestMethod = null,
    string? RequestPath = null,
    long? JobId = null,
    string? ContextJson = null,
    Guid? ReportId = null);

public sealed record ErrorLog(
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

public sealed record ErrorLogSummary(
    long Id,
    DateTimeOffset OccurredAt,
    string Source,
    string Area,
    string ExceptionType,
    string Message,
    string? TraceIdentifier,
    long? JobId);

public sealed record ErrorLogQuery(
    int Offset = 0,
    int Limit = 100,
    string? Source = null,
    string? Area = null);

public sealed record ErrorLogPage(
    IReadOnlyList<ErrorLogSummary> Items,
    int Total,
    int Offset,
    int Limit);

public interface IErrorLogService
{
    int RetentionDays { get; }

    Task<ErrorLogPage> BrowseAsync(ErrorLogQuery query, CancellationToken cancellationToken);

    Task<ErrorLog?> GetAsync(long id, CancellationToken cancellationToken);

    Task<long?> LogExceptionAsync(
        Exception exception,
        ErrorLogContext context,
        CancellationToken cancellationToken);

    Task<int> PurgeOlderThanAsync(
        DateTimeOffset cutoff,
        CancellationToken cancellationToken);
}
