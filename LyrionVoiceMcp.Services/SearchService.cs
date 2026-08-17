using System.Diagnostics;
using System.Runtime.ExceptionServices;
using LyrionVoiceMcp.Abstractions;
using Microsoft.Extensions.Logging;

namespace LyrionVoiceMcp.Services;

public sealed class SearchService(
    ICatalogueSearchResolver catalogueSearch,
    ILmsPlaylistSearchClient playlistSearch,
    ISearchResultReferenceCodec referenceCodec,
    ISearchObservationStore observationStore,
    TimeProvider timeProvider,
    ILogger<SearchService> logger) : ISearchService
{
    private const string Resolver = "catalogue-phuzzy-sqlite";
    private const string ResolverVersion = "1";

    public async Task<SearchOutcome> SearchAsync(
        string query,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return new SearchRejected(
                SearchRejectionReason.InvalidQuery,
                "The search query must not be empty.");
        }

        if (query.Length > SearchQueryPolicy.MaximumLength)
        {
            return new SearchRejected(
                SearchRejectionReason.InvalidQuery,
                $"The search query must contain no more than {SearchQueryPolicy.MaximumLength} characters.");
        }

        var normalisedQuery = query.Trim();
        if (SearchQueryPolicy.CountNormalisedTokens(normalisedQuery)
            > SearchQueryPolicy.MaximumTokenCount)
        {
            return new SearchRejected(
                SearchRejectionReason.InvalidQuery,
                $"The search query must contain no more than {SearchQueryPolicy.MaximumTokenCount} words.");
        }

        var observationId = Guid.NewGuid().ToString("N");
        var createdAt = timeProvider.GetUtcNow();
        var stopwatch = Stopwatch.StartNew();
        var catalogueTask = catalogueSearch.SearchAsync(normalisedQuery, cancellationToken);
        var playlistTask = playlistSearch.SearchPlaylistsAsync(normalisedQuery, cancellationToken);

        CatalogueSearchResponse? catalogueResponse = null;
        LmsSearchResponse? playlistResponse = null;
        Exception? catalogueFailure = null;
        LmsSearchFailedException? playlistFailure = null;
        Exception? unexpectedPlaylistFailure = null;
        try
        {
            catalogueResponse = await catalogueTask;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            catalogueFailure = exception;
        }

        try
        {
            playlistResponse = await playlistTask;
        }
        catch (LmsSearchFailedException exception)
        {
            playlistFailure = exception;
            playlistResponse = exception.Response;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            unexpectedPlaylistFailure = exception;
        }

        if (catalogueFailure is not null)
        {
            stopwatch.Stop();
            await TryRecordAsync(CreateFailedObservation(
                observationId,
                createdAt,
                query,
                normalisedQuery,
                catalogueFailure,
                stopwatch.ElapsedMilliseconds,
                playlistResponse), cancellationToken);
            if (catalogueFailure is CatalogueSearchUnavailableException)
            {
                return new SearchRejected(
                    SearchRejectionReason.SearchUnavailable,
                    catalogueFailure.Message);
            }

            ExceptionDispatchInfo.Capture(catalogueFailure).Throw();
            throw new UnreachableException();
        }

        var allCandidates = catalogueResponse!.Candidates
            .Take(20)
            .Select(candidate => new Candidate(
                candidate.Identity,
                candidate.Title,
                candidate.Artist,
                candidate.Album))
            .Concat((playlistResponse?.Candidates ?? [])
                .Where(candidate => candidate.Identity.Kind == MediaEntityKind.Playlist)
                .Take(20)
                .Select(candidate => new Candidate(
                    candidate.Identity,
                    candidate.Title,
                    candidate.Artist,
                    candidate.Album)))
            .ToArray();
        var observedCandidates = CreateObservationCandidates(allCandidates);
        stopwatch.Stop();
        var requests = CreateRequestObservations(catalogueResponse, playlistResponse);
        var retrievalDuration = Math.Max(
            catalogueResponse.RetrievalDurationMilliseconds
                + catalogueResponse.RerankDurationMilliseconds,
            playlistResponse?.RetrievalDurationMilliseconds ?? 0);

        if (unexpectedPlaylistFailure is not null)
        {
            await TryRecordAsync(CreateObservation(
                observationId,
                createdAt,
                query,
                normalisedQuery,
                SearchObservationStatus.Failed,
                unexpectedPlaylistFailure.Message,
                stopwatch.ElapsedMilliseconds,
                retrievalDuration,
                [
                    .. requests,
                    new LmsSearchRequestObservation(
                        "playlists",
                        "playlists",
                        LmsSearchRequestStatus.Failed,
                        unexpectedPlaylistFailure.Message,
                        stopwatch.ElapsedMilliseconds,
                        0)
                ],
                observedCandidates), cancellationToken);
            ExceptionDispatchInfo.Capture(unexpectedPlaylistFailure).Throw();
            throw new UnreachableException();
        }

        if (playlistFailure is not null)
        {
            await TryRecordAsync(CreateObservation(
                observationId,
                createdAt,
                query,
                normalisedQuery,
                SearchObservationStatus.Failed,
                playlistFailure.Message,
                stopwatch.ElapsedMilliseconds,
                retrievalDuration,
                requests,
                observedCandidates), cancellationToken);
            throw playlistFailure;
        }

        var results = observedCandidates
            .Select(candidate => new SearchCandidateResult(
                referenceCodec.Encode(new SearchResultReferenceValue(
                    candidate.CorrelationId,
                    candidate.Identity)),
                candidate.Identity.Kind,
                candidate.Title,
                candidate.Artist,
                candidate.Album))
            .ToArray();
        await TryRecordAsync(CreateObservation(
            observationId,
            createdAt,
            query,
            normalisedQuery,
            SearchObservationStatus.Completed,
            null,
            stopwatch.ElapsedMilliseconds,
            retrievalDuration,
            requests,
            observedCandidates), cancellationToken);

        logger.LogInformation(
            "Catalogue and playlist search for {Query} returned {ResultCount} candidates in {ElapsedMilliseconds} ms.",
            normalisedQuery,
            results.Length,
            stopwatch.ElapsedMilliseconds);
        return new SearchSucceeded(results);
    }

    private static IReadOnlyList<LmsSearchRequestObservation> CreateRequestObservations(
        CatalogueSearchResponse catalogue,
        LmsSearchResponse? playlists) =>
    [
        new LmsSearchRequestObservation(
            "catalogue-index",
            Resolver,
            LmsSearchRequestStatus.Completed,
            null,
            catalogue.RetrievalDurationMilliseconds + catalogue.RerankDurationMilliseconds,
            catalogue.Candidates.Count),
        .. playlists?.Requests ?? []
    ];

    private static SearchObservationCandidate[] CreateObservationCandidates(
        IReadOnlyList<Candidate> candidates) =>
        candidates.Select((candidate, index) => new SearchObservationCandidate(
            index + 1,
            Guid.NewGuid().ToString("N"),
            candidate.Identity,
            candidate.Title,
            candidate.Artist,
            candidate.Album,
            null)).ToArray();

    private static SearchObservation CreateObservation(
        string id,
        DateTimeOffset createdAt,
        string originalQuery,
        string normalisedQuery,
        SearchObservationStatus status,
        string? failureMessage,
        long totalDuration,
        long retrievalDuration,
        IReadOnlyList<LmsSearchRequestObservation> requests,
        IReadOnlyList<SearchObservationCandidate> candidates) => new(
            id,
            createdAt,
            originalQuery,
            normalisedQuery,
            null,
            "catalogue+lms",
            "whole_library",
            Resolver,
            ResolverVersion,
            status,
            failureMessage,
            totalDuration,
            retrievalDuration,
            Math.Max(0, totalDuration - retrievalDuration),
            requests,
            candidates,
            null);

    private static SearchObservation CreateFailedObservation(
        string id,
        DateTimeOffset createdAt,
        string originalQuery,
        string normalisedQuery,
        Exception exception,
        long elapsedMilliseconds,
        LmsSearchResponse? playlists)
    {
        var playlistCandidates = (playlists?.Candidates ?? [])
            .Select(candidate => new Candidate(
                candidate.Identity,
                candidate.Title,
                candidate.Artist,
                candidate.Album))
            .ToArray();
        return CreateObservation(
            id,
            createdAt,
            originalQuery,
            normalisedQuery,
            SearchObservationStatus.Failed,
            exception.Message,
            elapsedMilliseconds,
            elapsedMilliseconds,
            [
                new LmsSearchRequestObservation(
                    "catalogue-index",
                    Resolver,
                    LmsSearchRequestStatus.Failed,
                    exception.Message,
                    elapsedMilliseconds,
                    0),
                .. playlists?.Requests ?? []
            ],
            CreateObservationCandidates(playlistCandidates));
    }

    private async Task TryRecordAsync(
        SearchObservation observation,
        CancellationToken cancellationToken)
    {
        try
        {
            await observationStore.RecordAsync(observation, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(
                exception,
                "Could not persist search observation {ObservationId}.",
                observation.Id);
        }
    }

    private sealed record Candidate(
        MediaIdentity Identity,
        string Title,
        string? Artist,
        string? Album);
}
