namespace LyrionVoiceMcp.Ef.Abstractions.Entities;

public enum EntityJobStatus
{
    Pending,
    Running,
    Completed,
    Failed,
    Cancelled
}

public sealed class EntityJob : ISupportCreatedUpdated
{
    public int Id { get; set; }
    public string Type { get; set; } = string.Empty;
    public EntityJobStatus Status { get; set; }
    public DateTime RunAfterUtc { get; set; }
    public string PayloadJson { get; set; } = "{}";
    public string ResultJson { get; set; } = "{}";
    public string? ErrorMessage { get; set; }
    public DateTime? StartedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public string? CorrelationId { get; set; }
    public List<EntityJobLog> Logs { get; set; } = [];
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}
