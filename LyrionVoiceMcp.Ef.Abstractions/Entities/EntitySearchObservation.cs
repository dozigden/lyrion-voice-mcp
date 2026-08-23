namespace LyrionVoiceMcp.Ef.Abstractions.Entities;

public sealed class EntitySearchObservation
{
    public int Id { get; set; }

    public string ObservationId { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; }

    public string OriginalQuery { get; set; } = string.Empty;

    public string NormalisedQuery { get; set; } = string.Empty;

    public decimal? Rating { get; set; }

    public EntityRatingMatchMode? RatingMatch { get; set; }

    public string? Genre { get; set; }

    public int? RequestedFromYear { get; set; }

    public int? RequestedToYear { get; set; }

    public int? EffectiveFromYear { get; set; }

    public int? EffectiveToYear { get; set; }

    public EntityMediaKind? RequestedKind { get; set; }

    public string Provider { get; set; } = string.Empty;

    public string Collection { get; set; } = string.Empty;

    public string Resolver { get; set; } = string.Empty;

    public string ResolverVersion { get; set; } = string.Empty;

    public EntitySearchObservationStatus Status { get; set; }

    public string? FailureMessage { get; set; }

    public long TotalDurationMilliseconds { get; set; }

    public long RetrievalDurationMilliseconds { get; set; }

    public long ProcessingDurationMilliseconds { get; set; }

    public List<EntitySearchObservationRequest> Requests { get; set; } = [];

    public List<EntitySearchObservationCandidate> Candidates { get; set; } = [];

    public EntitySearchObservationReview? Review { get; set; }
}

public enum EntitySearchObservationStatus
{
    Completed,
    Failed
}

public enum EntityMediaKind
{
    Artist,
    Album,
    Track,
    Playlist
}

public enum EntityRatingMatchMode
{
    Exact,
    AtLeast
}
