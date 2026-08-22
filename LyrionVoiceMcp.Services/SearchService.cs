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
    public Task<SearchOutcome> SearchAsync(
        string query,
        CancellationToken cancellationToken) =>
        SearchAsync(new SearchCriteria(query), cancellationToken);

    public async Task<SearchOutcome> SearchAsync(
        SearchCriteria criteria,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(criteria);
        var query = criteria.Query;
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
        var tokenCount = SearchQueryPolicy.CountNormalisedTokens(normalisedQuery);
        if (tokenCount == 0)
        {
            return new SearchRejected(
                SearchRejectionReason.InvalidQuery,
                "The search query must include a media name; '*' is not a wildcard. For rating-only exploration, use browse and open Ratings.");
        }

        if (tokenCount > SearchQueryPolicy.MaximumTokenCount)
        {
            return new SearchRejected(
                SearchRejectionReason.InvalidQuery,
                $"The search query must contain no more than {SearchQueryPolicy.MaximumTokenCount} words.");
        }

        if (criteria.RatingConstraint is { } ratingConstraint
            && (ratingConstraint.Rating is < 0 or > 5
                || !Enum.IsDefined(ratingConstraint.Match)))
        {
            return new SearchRejected(
                SearchRejectionReason.InvalidQuery,
                "The rating must be from 0 to 5 and ratingMatch must be exact or at_least.");
        }

        var observation = observationRecorder.Begin(
            query,
            normalisedQuery,
            catalogueSearch.Descriptor,
            criteria.RatingConstraint);
        var stopwatch = Stopwatch.StartNew();
        var catalogueTask = catalogueSearch.SearchAsync(
            normalisedQuery,
            criteria.RatingConstraint,
            cancellationToken);
        var playlistTask = criteria.RatingConstraint is null
            ? playlistSearch.SearchPlaylistsAsync(normalisedQuery, cancellationToken)
            : null;

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
            if (playlistTask is not null)
            {
                playlistResponse = await playlistTask;
            }
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

        var catalogueCandidates = criteria.RatingConstraint is null
            ? catalogueResponse!.Candidates
            : catalogueResponse!.Candidates
                .Where(candidate => candidate.Identity.Kind == MediaEntityKind.Track)
                .ToArray();
        var candidates = catalogueCandidates
            .Where(candidate => candidate.Identity.Kind == MediaEntityKind.Artist)
            .Take(SearchResultPolicy.ArtistLimit)
            .Concat(catalogueCandidates
                .Where(candidate => candidate.Identity.Kind == MediaEntityKind.Album)
                .Take(SearchResultPolicy.AlbumLimit))
            .Concat(catalogueCandidates
                .Where(candidate => candidate.Identity.Kind == MediaEntityKind.Track)
                .Take(SearchResultPolicy.TrackLimit))
            .Select(candidate => new Candidate(
                candidate.Identity,
                candidate.Title,
                candidate.Artist,
                candidate.Album,
                candidate.NativeRating))
            .Concat((playlistResponse?.Candidates ?? [])
                .Where(candidate => candidate.Identity.Kind == MediaEntityKind.Playlist)
                .Take(SearchResultPolicy.PlaylistLimit)
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
                candidate.Album,
                candidate.NativeRating))
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
                candidate.Album,
                candidate.NativeRating))
            .ToArray();
        await observationRecorder.RecordCompletedAsync(
            observation,
            catalogueResponse,
            playlistResponse,
            candidates,
            stopwatch.ElapsedMilliseconds,
            cancellationToken);

        logger.LogInformation(
            "Media search for {Query} returned {ArtistCount} artists, {AlbumCount} albums, {TrackCount} tracks, and {PlaylistCount} playlists in {ElapsedMilliseconds} ms.",
            normalisedQuery,
            results.Count(result => result.Kind == MediaEntityKind.Artist),
            results.Count(result => result.Kind == MediaEntityKind.Album),
            results.Count(result => result.Kind == MediaEntityKind.Track),
            results.Count(result => result.Kind == MediaEntityKind.Playlist),
            stopwatch.ElapsedMilliseconds);
        return new SearchSucceeded(results);
    }

    private sealed record Candidate(
        MediaIdentity Identity,
        string Title,
        string? Artist,
        string? Album,
        int NativeRating = 0);
}
