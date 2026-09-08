namespace LyrionVoiceMcp.Ef.Abstractions.Entities;

public sealed class EntityScheduledJobConfiguration : ISupportCreatedUpdated
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool Enabled { get; set; }
    public string CronExpression { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}
