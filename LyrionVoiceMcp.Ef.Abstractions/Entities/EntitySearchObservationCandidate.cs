namespace LyrionVoiceMcp.Ef.Abstractions.Entities;

public sealed class EntitySearchObservationCandidate
{
    public int Id { get; set; }

    public int SearchObservationId { get; set; }

    public EntitySearchObservation SearchObservation { get; set; } = null!;

    public int Position { get; set; }

    public string CorrelationId { get; set; } = string.Empty;

    public EntityMediaKind Kind { get; set; }

    public string MediaId { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string? Artist { get; set; }

    public string? Album { get; set; }

    public EntitySearchObservationSelection? Selection { get; set; }
}
