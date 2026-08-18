namespace LyrionVoiceMcp.Ef.Abstractions.Entities;

public sealed class EntitySearchObservationReview
{
    public int Id { get; set; }

    public int SearchObservationId { get; set; }

    public EntitySearchObservation SearchObservation { get; set; } = null!;

    public EntitySearchReviewClassification Classification { get; set; }

    public string? ExpectedCorrelationId { get; set; }

    public EntityMediaKind? ExpectedKind { get; set; }

    public string? ExpectedTitle { get; set; }

    public string? ExpectedArtist { get; set; }

    public string? ExpectedAlbum { get; set; }

    public string? Notes { get; set; }

    public bool IncludeInEvaluation { get; set; }

    public DateTime ReviewedAtUtc { get; set; }
}

public enum EntitySearchReviewClassification
{
    Good,
    WrongOrder,
    NoMatch,
    Ambiguous,
    TranscriptionError,
    Other
}
