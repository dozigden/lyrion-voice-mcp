namespace LyrionVoiceMcp.Ef.Abstractions.Entities;

public sealed class EntityErrorLog : ISupportCreatedUpdated
{
    public int Id { get; set; }
    public Guid? ReportId { get; set; }
    public DateTime OccurredAtUtc { get; set; }
    public string Source { get; set; } = string.Empty;
    public string Area { get; set; } = string.Empty;
    public string ExceptionType { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string? StackTrace { get; set; }
    public string? TraceIdentifier { get; set; }
    public string? RequestMethod { get; set; }
    public string? RequestPath { get; set; }
    public int? JobId { get; set; }
    public EntityJob? Job { get; set; }
    public string? ContextJson { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}
