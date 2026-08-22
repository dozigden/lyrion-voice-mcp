using LyrionVoiceMcp.Abstractions;
using Microsoft.Extensions.Logging;

namespace LyrionVoiceMcp.Services;

internal sealed class SearchObservationRecorder(
    ISearchObservationStore store,
    TimeProvider timeProvider,
    ILogger<SearchObservationRecorder> logger)
{
    public SearchObservationContext Begin(
        string originalQuery,
        string normalisedQuery,
        SearchResolverDescriptor resolver) => new(
            Guid.NewGuid().ToString("N"),
            timeProvider.GetUtcNow(),
            originalQuery,
            normalisedQuery,
            resolver);

    public Task RecordCompletedAsync(
        SearchObservationContext context,
        CatalogueSearchResponse catalogue,
        LmsSearchResponse? playlists,
        IReadOnlyList<SearchCandidateOccurrence> candidates,
        long totalDurationMilliseconds,
        CancellationToken cancellationToken) =>
        TryRecordAsync(
            CreateObservation(
                context,
                SearchObservationStatus.Completed,
                null,
                totalDurationMilliseconds,
                RetrievalDuration(catalogue, playlists),
                CreateRequestObservations(context.Resolver, catalogue, playlists),
                CreateObservationCandidates(candidates)),
            cancellationToken);

    public Task RecordCatalogueFailureAsync(
        SearchObservationContext context,
        Exception failure,
        long elapsedMilliseconds,
        LmsSearchResponse? playlists,
        CancellationToken cancellationToken) =>
        TryRecordAsync(
            CreateObservation(
                context,
                SearchObservationStatus.Failed,
                failure.Message,
                elapsedMilliseconds,
                elapsedMilliseconds,
                [
                    new LmsSearchRequestObservation(
                        "catalogue-index",
                        context.Resolver.Name,
                        LmsSearchRequestStatus.Failed,
                        failure.Message,
                        elapsedMilliseconds,
                        0),
                    .. playlists?.Requests ?? []
                ],
                CreatePlaylistObservationCandidates(playlists)),
            cancellationToken);

    public Task RecordPlaylistFailureAsync(
        SearchObservationContext context,
        CatalogueSearchResponse catalogue,
        LmsSearchResponse? playlists,
        IReadOnlyList<SearchCandidateOccurrence> candidates,
        Exception failure,
        long totalDurationMilliseconds,
        CancellationToken cancellationToken)
    {
        var requests = CreateRequestObservations(context.Resolver, catalogue, playlists);
        if (failure is not LmsSearchFailedException)
        {
            requests = [
                .. requests,
                new LmsSearchRequestObservation(
                    "playlists",
                    "playlists",
                    LmsSearchRequestStatus.Failed,
                    failure.Message,
                    totalDurationMilliseconds,
                    0)
            ];
        }

        return TryRecordAsync(
            CreateObservation(
                context,
                SearchObservationStatus.Failed,
                failure.Message,
                totalDurationMilliseconds,
                RetrievalDuration(catalogue, playlists),
                requests,
                CreateObservationCandidates(candidates)),
            cancellationToken);
    }

    private static IReadOnlyList<LmsSearchRequestObservation> CreateRequestObservations(
        SearchResolverDescriptor resolver,
        CatalogueSearchResponse catalogue,
        LmsSearchResponse? playlists) =>
    [
        new LmsSearchRequestObservation(
            "catalogue-index",
            resolver.Name,
            LmsSearchRequestStatus.Completed,
            null,
            catalogue.RetrievalDurationMilliseconds + catalogue.RerankDurationMilliseconds,
            catalogue.Candidates.Count),
        .. playlists?.Requests ?? []
    ];

    private static SearchObservationCandidate[] CreateObservationCandidates(
        IReadOnlyList<SearchCandidateOccurrence> candidates) =>
        candidates.Select(candidate => new SearchObservationCandidate(
            candidate.Position,
            candidate.CorrelationId,
            candidate.Identity,
            candidate.Title,
            candidate.Artist,
            candidate.Album,
            null)).ToArray();

    private static SearchObservationCandidate[] CreatePlaylistObservationCandidates(
        LmsSearchResponse? playlists) =>
        (playlists?.Candidates ?? [])
            .Select((candidate, index) => new SearchObservationCandidate(
                index + 1,
                Guid.NewGuid().ToString("N"),
                candidate.Identity,
                candidate.Title,
                candidate.Artist,
                candidate.Album,
                null))
            .ToArray();

    private static long RetrievalDuration(
        CatalogueSearchResponse catalogue,
        LmsSearchResponse? playlists) =>
        Math.Max(
            catalogue.RetrievalDurationMilliseconds
                + catalogue.RerankDurationMilliseconds,
            playlists?.RetrievalDurationMilliseconds ?? 0);

    private static SearchObservation CreateObservation(
        SearchObservationContext context,
        SearchObservationStatus status,
        string? failureMessage,
        long totalDurationMilliseconds,
        long retrievalDurationMilliseconds,
        IReadOnlyList<LmsSearchRequestObservation> requests,
        IReadOnlyList<SearchObservationCandidate> candidates) => new(
            context.Id,
            context.CreatedAt,
            context.OriginalQuery,
            context.NormalisedQuery,
            null,
            "catalogue+lms",
            "whole_library",
            context.Resolver.Name,
            context.Resolver.Version,
            status,
            failureMessage,
            totalDurationMilliseconds,
            retrievalDurationMilliseconds,
            Math.Max(0, totalDurationMilliseconds - retrievalDurationMilliseconds),
            requests,
            candidates,
            null);

    private async Task TryRecordAsync(
        SearchObservation observation,
        CancellationToken cancellationToken)
    {
        try
        {
            await store.RecordAsync(observation, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(
                exception,
                "Could not persist search observation {ObservationId}.",
                observation.Id);
        }
    }
}

internal sealed record SearchObservationContext(
    string Id,
    DateTimeOffset CreatedAt,
    string OriginalQuery,
    string NormalisedQuery,
    SearchResolverDescriptor Resolver);

internal sealed record SearchCandidateOccurrence(
    int Position,
    string CorrelationId,
    MediaIdentity Identity,
    string Title,
    string? Artist,
    string? Album,
    int? NativeRating = null);
