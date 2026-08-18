namespace LyrionVoiceMcp.Ef.Abstractions.Entities;

public sealed class EntitySearchObservationSelection
{
    public int Id { get; set; }

    public int SearchObservationCandidateId { get; set; }

    public EntitySearchObservationCandidate SearchObservationCandidate { get; set; } = null!;

    public DateTime SelectedAtUtc { get; set; }
}
