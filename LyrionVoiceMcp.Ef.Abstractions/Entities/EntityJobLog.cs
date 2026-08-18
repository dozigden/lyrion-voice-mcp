namespace LyrionVoiceMcp.Ef.Abstractions.Entities;

public enum EntityJobLogLevel
{
    Information,
    Warning,
    Error
}

public sealed class EntityJobLog : ISupportCreatedUpdated
{
    public int Id { get; set; }
    public int JobId { get; set; }
    public EntityJob Job { get; set; } = null!;
    public EntityJobLogLevel Level { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? DataJson { get; set; }
    public DateTime LoggedAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}
