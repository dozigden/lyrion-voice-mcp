namespace LyrionVoiceMcp.Ef.Abstractions.Entities;

public sealed class EntityScheduledJobState : ISupportCreatedUpdated
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateTime LastRunAtUtc { get; set; }
    public DateTime? LastEvaluatedAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}
