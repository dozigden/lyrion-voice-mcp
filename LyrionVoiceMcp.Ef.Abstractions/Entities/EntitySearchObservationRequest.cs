namespace LyrionVoiceMcp.Ef.Abstractions.Entities;

public sealed class EntitySearchObservationRequest
{
    public int Id { get; set; }

    public int SearchObservationId { get; set; }

    public EntitySearchObservation SearchObservation { get; set; } = null!;

    public int Sequence { get; set; }

    public string Source { get; set; } = string.Empty;

    public string Command { get; set; } = string.Empty;

    public EntitySearchObservationRequestStatus Status { get; set; }

    public string? FailureMessage { get; set; }

    public long DurationMilliseconds { get; set; }

    public int ResultCount { get; set; }
}

public enum EntitySearchObservationRequestStatus
{
    Completed,
    Failed
}
