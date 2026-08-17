using System.Diagnostics;
using System.Runtime.ExceptionServices;
using LyrionVoiceMcp.Abstractions;
using Microsoft.Extensions.Logging;

namespace LyrionVoiceMcp.Services;

internal sealed class SearchService(
    ICatalogueSearchResolver catalogueSearch,
    ILmsPlaylistSearchClient playlistSearch,
    ISearchResultReferenceCodec referenceCodec,
    SearchObservationRecorder observationRecorder,
    ILogger<SearchService> logger) : ISearchService
{
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

        var observation = observationRecorder.Begin(
            query,
            normalisedQuery,
            catalogueSearch.Descriptor);
        var stopwatch = Stopwatch.StartNew();
        var catalogueTask = catalogueSearch.SearchAsync(normalisedQuery, cancellationToken);
        var playlistTask = playlistSearch.SearchPlaylistsAsync(normalisedQuery, cancellationToken);

        CatalogueSearchResponse? catalogueResponse = null;
        LmsSearchResponse? playlistResponse = null;
        Exception? catalogueFailure = null;
        Exception? playlistFailure = null;
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
            playlistFailure = exception;
        }

        if (catalogueFailure is not null)
        {
            stopwatch.Stop();
            await observationRecorder.RecordCatalogueFailureAsync(
                observation,
                catalogueFailure,
                stopwatch.ElapsedMilliseconds,
                playlistResponse,
                cancellationToken);
            if (catalogueFailure is CatalogueSearchUnavailableException)
            {
                return new SearchRejected(
                    SearchRejectionReason.SearchUnavailable,
                    catalogueFailure.Message);
            }

            ExceptionDispatchInfo.Capture(catalogueFailure).Throw();
            throw new UnreachableException();
        }

        var candidates = catalogueResponse!.Candidates
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
            .Select((candidate, index) => new SearchCandidateOccurrence(
                index + 1,
                Guid.NewGuid().ToString("N"),
                candidate.Identity,
                candidate.Title,
                candidate.Artist,
                candidate.Album))
            .ToArray();
        stopwatch.Stop();

        if (playlistFailure is not null)
        {
            await observationRecorder.RecordPlaylistFailureAsync(
                observation,
                catalogueResponse,
                playlistResponse,
                candidates,
                playlistFailure,
                stopwatch.ElapsedMilliseconds,
                cancellationToken);
            ExceptionDispatchInfo.Capture(playlistFailure).Throw();
            throw new UnreachableException();
        }

        var results = candidates
            .Select(candidate => new SearchCandidateResult(
                referenceCodec.Encode(new SearchResultReferenceValue(
                    candidate.CorrelationId,
                    candidate.Identity)),
                candidate.Identity.Kind,
                candidate.Title,
                candidate.Artist,
                candidate.Album))
            .ToArray();
        await observationRecorder.RecordCompletedAsync(
            observation,
            catalogueResponse,
            playlistResponse,
            candidates,
            stopwatch.ElapsedMilliseconds,
            cancellationToken);

        logger.LogInformation(
            "Catalogue and playlist search for {Query} returned {ResultCount} candidates in {ElapsedMilliseconds} ms.",
            normalisedQuery,
            results.Length,
            stopwatch.ElapsedMilliseconds);
        return new SearchSucceeded(results);
    }

    private sealed record Candidate(
        MediaIdentity Identity,
        string Title,
        string? Artist,
        string? Album);
}
