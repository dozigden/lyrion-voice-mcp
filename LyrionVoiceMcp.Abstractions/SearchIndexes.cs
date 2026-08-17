namespace LyrionVoiceMcp.Abstractions;

public sealed record SearchIndexArtifact(
    string Resolver,
    string ResolverVersion,
    string CatalogueRefreshId,
    DateTimeOffset BuiltAt,
    int CandidateCount,
    long PreparationDurationMilliseconds,
    long IndexSizeBytes);

public sealed record SearchIndexStatus(
    string Resolver,
    SearchIndexArtifact? Artifact,
    Job? LatestJob);

public sealed record SearchIndexRebuildResult(
    SearchIndexArtifact Artifact);

public interface ISearchIndexProgress
{
    Task ReportAsync(
        string message,
        object? data,
        CancellationToken cancellationToken);
}

public interface ISearchIndexBuilder
{
    SearchResolverDescriptor Descriptor { get; }

    Task<SearchIndexArtifact?> GetArtifactAsync(CancellationToken cancellationToken);

    Task<SearchIndexRebuildResult> RebuildAsync(
        string catalogueRefreshId,
        long jobId,
        ISearchIndexProgress progress,
        CancellationToken cancellationToken);
}

public interface ISearchIndexService
{
    Task<SearchIndexStatus> GetAsync(CancellationToken cancellationToken);

    Task<SearchIndexRebuildOutcome> RebuildAsync(CancellationToken cancellationToken);

    Task<long?> EnqueueForCatalogueAsync(
        string catalogueRefreshId,
        CancellationToken cancellationToken);
}

public abstract record SearchIndexRebuildOutcome;

public sealed record SearchIndexRebuildStarted(
    SearchIndexStatus Status) : SearchIndexRebuildOutcome;

public sealed record SearchIndexRebuildRejected(
    string Message) : SearchIndexRebuildOutcome;

public sealed record SearchIndexRebuildPayload(
    string CatalogueRefreshId);
