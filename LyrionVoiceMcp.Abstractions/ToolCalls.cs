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

public enum ReferenceDisplayKind
{
    Category,
    AlbumArtist,
    Artist,
    Album,
    Genre,
    Playlist,
    Year,
    Track,
    Programme,
    Episode
}

public sealed record ReferenceDisplayMetadata(
    ReferenceDisplayKind Kind,
    string Title,
    string? Artist = null,
    string? Album = null,
    bool IsContinuation = false);

public sealed record ToolCallReferenceSnapshot(
    string ArgumentPath,
    string Reference,
    ReferenceDisplayMetadata? DisplayMetadata);

public interface IReferenceDisplayMetadataResolver
{
    ReferenceDisplayMetadata? Resolve(string reference);
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
    IReadOnlyList<ToolCallReferenceSnapshot>? ReferenceSnapshots,
    bool ReferenceSnapshotsTruncated,
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
    long? ErrorLogId,
    string? RequestSummary);

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

    Task MarkRunningInterruptedAsync(CancellationToken cancellationToken);

    Task<ToolCallPage> BrowseAsync(ToolCallQuery query, CancellationToken cancellationToken);

    Task<ToolCall?> GetAsync(string id, CancellationToken cancellationToken);

    Task<int> PurgeOlderThanAsync(
        DateTimeOffset cutoff,
        CancellationToken cancellationToken);
}
