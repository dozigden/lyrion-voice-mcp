namespace LyrionVoiceMcp.Ef.Abstractions.Entities;

public enum EntityToolCallStatus
{
    Running,
    Succeeded,
    ToolError,
    Cancelled,
    Failed,
    Interrupted
}

public sealed class EntityToolCall : ISupportCreatedUpdated
{
    public int Id { get; set; }
    public string ToolCallId { get; set; } = string.Empty;
    public string ToolName { get; set; } = string.Empty;
    public EntityToolCallStatus Status { get; set; }
    public DateTime StartedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public long? DurationMilliseconds { get; set; }
    public string ArgumentsJson { get; set; } = "{}";
    public bool ArgumentsTruncated { get; set; }
    public string? ResultJson { get; set; }
    public bool ResultTruncated { get; set; }
    public string? ErrorMessage { get; set; }
    public string? TraceIdentifier { get; set; }
    public int? ErrorLogId { get; set; }
    public EntityErrorLog? ErrorLog { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}
