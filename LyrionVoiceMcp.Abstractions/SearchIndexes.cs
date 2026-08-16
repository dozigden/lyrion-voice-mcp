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
    IReadOnlyList<string> Resolvers { get; }

    Task<SearchIndexArtifact?> GetArtifactAsync(
        string resolver,
        CancellationToken cancellationToken);

    Task<SearchIndexRebuildResult> RebuildAsync(
        string resolver,
        string catalogueRefreshId,
        long jobId,
        ISearchIndexProgress progress,
        CancellationToken cancellationToken);
}

public interface ISearchIndexService
{
    Task<IReadOnlyList<SearchIndexStatus>> ListAsync(CancellationToken cancellationToken);

    Task<SearchIndexRebuildOutcome> RebuildAsync(
        string resolver,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<long>> EnqueueForCatalogueAsync(
        string catalogueRefreshId,
        CancellationToken cancellationToken);
}

public abstract record SearchIndexRebuildOutcome;

public sealed record SearchIndexRebuildStarted(
    SearchIndexStatus Status) : SearchIndexRebuildOutcome;

public sealed record SearchIndexRebuildRejected(
    string Message) : SearchIndexRebuildOutcome;

public sealed record SearchIndexRebuildPayload(
    string Resolver,
    string CatalogueRefreshId);
