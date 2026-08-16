namespace LyrionVoiceMcp.Contracts;

public sealed record SearchIndexStatusResponse(
    string Resolver,
    SearchIndexArtifactResponse? Artifact,
    SearchIndexJobResponse? LatestJob);

public sealed record SearchIndexArtifactResponse(
    string ResolverVersion,
    string CatalogueRefreshId,
    DateTimeOffset BuiltAt,
    int CandidateCount,
    long PreparationDurationMilliseconds,
    long IndexSizeBytes);

public sealed record SearchIndexJobResponse(
    long Id,
    string Status,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    string? ErrorMessage);
