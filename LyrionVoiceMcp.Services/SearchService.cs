using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Text.RegularExpressions;
using LyrionVoiceMcp.Abstractions;
using Microsoft.Extensions.Logging;

namespace LyrionVoiceMcp.Services;

internal sealed partial class SearchService(
    ICatalogueSearchResolver catalogueSearch,
    ICatalogueArtistTrackResolver artistTracks,
    ILmsPlaylistSearchClient playlistSearch,
    ISearchResultReferenceCodec referenceCodec,
    IBrowseReferenceCodec browseReferenceCodec,
    SearchCandidateSelector candidateSelector,
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
                "The search name must not be empty.");
        }

        if (query.Length > SearchQueryPolicy.MaximumLength)
        {
            return new SearchRejected(
                SearchRejectionReason.InvalidQuery,
                $"The search name must contain no more than {SearchQueryPolicy.MaximumLength} characters.");
        }

        var normalisedQuery = query.Trim();
        var tokenCount = SearchQueryPolicy.CountNormalisedTokens(normalisedQuery);
        if (tokenCount == 0)
        {
            return new SearchRejected(
                SearchRejectionReason.InvalidQuery,
                "The search name must include media-name text; '*' is not a wildcard. For rating-only exploration, use browse and open Ratings.");
        }

        if (tokenCount > SearchQueryPolicy.MaximumTokenCount)
        {
            return new SearchRejected(
                SearchRejectionReason.InvalidQuery,
                $"The search name must contain no more than {SearchQueryPolicy.MaximumTokenCount} words.");
        }

        if (RatingSyntax().IsMatch(normalisedQuery))
        {
            return new SearchRejected(
                SearchRejectionReason.InvalidQuery,
                "The name must contain media-name text only. Put the numeric rating in rating and use ratingMatch exact or at_least; for example, name \"Copper Lines\", rating 5, ratingMatch \"exact\".");
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
        var unconstrainedCatalogueTask = ObserveCatalogueSearchAsync(
            "search:unconstrained",
            normalisedQuery,
            null,
            cancellationToken);
        var catalogueTask = criteria.RatingConstraint is null
            ? unconstrainedCatalogueTask
            : ObserveCatalogueSearchAsync(
                "search:requested-rating",
                normalisedQuery,
                criteria.RatingConstraint,
                cancellationToken);
        var topRatingConstraint = TopRatingConstraint(criteria.RatingConstraint);
        var topCatalogueTask = topRatingConstraint is null
            ? null
            : topRatingConstraint == criteria.RatingConstraint
                ? catalogueTask
                : ObserveCatalogueSearchAsync(
                    "search:top-rating",
                    normalisedQuery,
                    topRatingConstraint,
                    cancellationToken);
        var playlistTask = criteria.RatingConstraint is null
            ? playlistSearch.SearchPlaylistsAsync(normalisedQuery, cancellationToken)
            : null;
        var catalogueTasks = new List<Task<ObservedCatalogueSearch>>
        {
            unconstrainedCatalogueTask
        };
        if (!ReferenceEquals(catalogueTask, unconstrainedCatalogueTask))
        {
            catalogueTasks.Add(catalogueTask);
        }

        if (topCatalogueTask is not null
            && !catalogueTasks.Any(task => ReferenceEquals(task, topCatalogueTask)))
        {
            catalogueTasks.Add(topCatalogueTask);
        }

        CatalogueSearchResponse? catalogueResponse;
        CatalogueSearchResponse? unconstrainedCatalogueResponse;
        CatalogueSearchResponse? topCatalogueResponse;
        LmsSearchResponse? playlistResponse = null;
        Exception? playlistFailure = null;
        var catalogueSearches = await Task.WhenAll(catalogueTasks);
        var catalogueRequests = catalogueSearches
            .Select(search => search.Request)
            .ToList();
        var catalogueFailure = catalogueSearches
            .Select(search => search.Failure)
            .FirstOrDefault(failure => failure is not null);
        unconstrainedCatalogueResponse = (await unconstrainedCatalogueTask).Response;
        catalogueResponse = (await catalogueTask).Response;
        topCatalogueResponse = topCatalogueTask is null
            ? null
            : (await topCatalogueTask).Response;

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
            return await HandleCatalogueFailureAsync(
                observation,
                catalogueRequests,
                catalogueFailure,
                stopwatch.ElapsedMilliseconds,
                playlistResponse,
                cancellationToken);
        }

        var catalogueCandidates = criteria.RatingConstraint is null
            ? catalogueResponse!.Candidates
            : catalogueResponse!.Candidates
                .Where(candidate => candidate.Identity.Kind == MediaEntityKind.Track)
                .ToArray();
        var exactArtist = ExactArtist(unconstrainedCatalogueResponse!.Candidates);
        IReadOnlyList<CatalogueSearchCandidate> selectedTopTracks;
        IReadOnlyList<CatalogueSearchCandidate> selectedTracks;
        if (exactArtist is null)
        {
            selectedTopTracks = candidateSelector.Rotate(
                (topCatalogueResponse?.Candidates ?? [])
                    .Where(candidate => candidate.Identity.Kind == MediaEntityKind.Track)
                    .ToArray(),
                SearchResultPolicy.TopTrackLimit,
                TopTrackWeight);
            selectedTracks = SelectRegularTracks(
                catalogueCandidates
                    .Where(candidate => candidate.Identity.Kind == MediaEntityKind.Track)
                    .ToArray(),
                selectedTopTracks);
        }
        else
        {
            var selection = await SelectArtistTracksAsync(
                exactArtist.Identity.Id,
                criteria.RatingConstraint,
                cancellationToken);
            catalogueRequests.Add(selection.Request);
            if (selection.Failure is not null)
            {
                stopwatch.Stop();
                return await HandleCatalogueFailureAsync(
                    observation,
                    catalogueRequests,
                    selection.Failure,
                    stopwatch.ElapsedMilliseconds,
                    playlistResponse,
                    cancellationToken);
            }

            selectedTopTracks = selection.TopTracks;
            selectedTracks = selection.Tracks;
        }

        var exactArtistCandidate = exactArtist is null
            ? Array.Empty<Candidate>()
            : [ToCandidate(exactArtist, CandidateGroup.ExactArtist)];
        var nonTrackCandidates = catalogueCandidates
            .Where(candidate => exactArtist is null
                && candidate.Identity.Kind == MediaEntityKind.Artist)
            .Take(SearchResultPolicy.ArtistLimit)
            .Concat(catalogueCandidates
                .Where(candidate => candidate.Identity.Kind == MediaEntityKind.Album)
                .Take(SearchResultPolicy.AlbumLimit))
            .Select(candidate => new Candidate(
                candidate.Identity,
                candidate.Title,
                candidate.Artist,
                candidate.Album,
                candidate.NativeRating))
            .ToArray();
        var playlistCandidates = (playlistResponse?.Candidates ?? [])
            .Where(candidate => candidate.Identity.Kind == MediaEntityKind.Playlist)
            .Take(SearchResultPolicy.PlaylistLimit)
            .Select(candidate => new Candidate(
                candidate.Identity,
                candidate.Title,
                candidate.Artist,
                candidate.Album))
            .ToArray();
        var topCandidates = selectedTopTracks
            .Select(candidate => ToCandidate(candidate, CandidateGroup.TopTrack))
            .ToArray();
        var candidates = exactArtistCandidate
            .Concat(nonTrackCandidates)
            .Concat(topCandidates)
            .Concat(selectedTracks.Select(candidate => ToCandidate(candidate, CandidateGroup.Standard)))
            .Concat(playlistCandidates)
            .Select((candidate, index) => new SelectedCandidate(
                candidate.Group,
                new SearchCandidateOccurrence(
                    index + 1,
                    Guid.NewGuid().ToString("N"),
                    candidate.Identity,
                    candidate.Title,
                    candidate.Artist,
                    candidate.Album,
                    candidate.NativeRating,
                    candidate.Group == CandidateGroup.ExactArtist)))
            .ToArray();
        var observedCandidates = candidates
            .Select(candidate => candidate.Occurrence)
            .ToArray();
        stopwatch.Stop();

        if (playlistFailure is not null)
        {
            await observationRecorder.RecordPlaylistFailureAsync(
                observation,
                catalogueRequests,
                playlistResponse,
                observedCandidates,
                playlistFailure,
                stopwatch.ElapsedMilliseconds,
                cancellationToken);
            ExceptionDispatchInfo.Capture(playlistFailure).Throw();
            throw new UnreachableException();
        }

        var results = candidates
            .Where(candidate => candidate.Group == CandidateGroup.Standard)
            .Select(candidate => ToResult(candidate.Occurrence))
            .ToArray();
        var topResults = candidates
            .Where(candidate => candidate.Group == CandidateGroup.TopTrack)
            .Select(candidate => ToResult(candidate.Occurrence))
            .ToArray();
        var exactArtistResult = candidates
            .Where(candidate => candidate.Group == CandidateGroup.ExactArtist)
            .Select(candidate => ToExactArtistMatch(candidate.Occurrence))
            .SingleOrDefault();
        await observationRecorder.RecordCompletedAsync(
            observation,
            catalogueRequests,
            playlistResponse,
            observedCandidates,
            stopwatch.ElapsedMilliseconds,
            cancellationToken);

        logger.LogInformation(
            "Media search for {Query} resolved exact artist {ExactArtist}, and returned {ArtistCount} artist candidates, {AlbumCount} albums, {TopTrackCount} top tracks, {TrackCount} tracks, and {PlaylistCount} playlists in {ElapsedMilliseconds} ms.",
            normalisedQuery,
            exactArtistResult?.Name,
            results.Count(result => result.Kind == MediaEntityKind.Artist),
            results.Count(result => result.Kind == MediaEntityKind.Album),
            topResults.Length,
            results.Count(result => result.Kind == MediaEntityKind.Track),
            results.Count(result => result.Kind == MediaEntityKind.Playlist),
            stopwatch.ElapsedMilliseconds);
        return new SearchSucceeded(results, topResults, exactArtistResult);
    }

    private static RatingSearchConstraint? TopRatingConstraint(
        RatingSearchConstraint? ratingConstraint) =>
        ratingConstraint switch
        {
            null => new RatingSearchConstraint(4, RatingMatchMode.AtLeast),
            { Match: RatingMatchMode.Exact, Rating: < 4 } => null,
            { Match: RatingMatchMode.Exact } => ratingConstraint,
            { Match: RatingMatchMode.AtLeast, Rating: >= 4 } => ratingConstraint,
            { Match: RatingMatchMode.AtLeast } => new RatingSearchConstraint(
                4,
                RatingMatchMode.AtLeast),
            _ => null
        };

    private static CatalogueSearchCandidate? ExactArtist(
        IReadOnlyList<CatalogueSearchCandidate> catalogueCandidates)
    {
        var artists = catalogueCandidates
            .Where(candidate =>
                candidate.Identity.Kind == MediaEntityKind.Artist
                && candidate.IsExactTitleMatch)
            .Take(2)
            .ToArray();
        if (artists.Length != 1
            || catalogueCandidates.Any(candidate =>
                candidate.Identity.Kind is MediaEntityKind.Album or MediaEntityKind.Track
                && candidate.IsExactTitleMatch))
        {
            return null;
        }

        return artists[0];
    }

    private IReadOnlyList<CatalogueSearchCandidate> SelectRegularTracks(
        IReadOnlyCollection<CatalogueSearchCandidate> candidates,
        IReadOnlyCollection<CatalogueSearchCandidate> topTracks)
    {
        var topIdentities = topTracks
            .Select(candidate => candidate.Identity)
            .ToHashSet();
        return candidateSelector.Rotate(candidates, SearchResultPolicy.PreparedTrackLimit)
            .Where(candidate => !topIdentities.Contains(candidate.Identity))
            .Take(SearchResultPolicy.TrackLimit)
            .ToArray();
    }

    private async Task<ArtistTrackSelection> SelectArtistTracksAsync(
        string artistId,
        RatingSearchConstraint? ratingConstraint,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var observedCount = 0;
        var regularReservoir = candidateSelector.CreateReservoir(
            SearchResultPolicy.ArtistTrackReservoirLimit);
        var topReservoir = candidateSelector.CreateReservoir(
            SearchResultPolicy.ArtistTrackReservoirLimit);
        try
        {
            await foreach (var candidate in artistTracks.ReadArtistTracksAsync(
                artistId,
                cancellationToken))
            {
                observedCount++;
                if (!MatchesRating(candidate.NativeRating, ratingConstraint))
                {
                    continue;
                }

                regularReservoir.Consider(candidate);
                if (candidate.NativeRating >= 80)
                {
                    topReservoir.Consider(candidate);
                }
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            stopwatch.Stop();
            return new ArtistTrackSelection(
                [],
                [],
                CatalogueRequest(
                    "catalogue-artist-tracks",
                    "artist-tracks",
                    LmsSearchRequestStatus.Failed,
                    exception.Message,
                    stopwatch.ElapsedMilliseconds,
                    observedCount),
                exception);
        }

        stopwatch.Stop();
        var topTracks = candidateSelector.Rotate(
            topReservoir.Candidates,
            SearchResultPolicy.TopTrackLimit,
            TopTrackWeight);
        return new ArtistTrackSelection(
            topTracks,
            SelectRegularTracks(regularReservoir.Candidates, topTracks),
            CatalogueRequest(
                "catalogue-artist-tracks",
                "artist-tracks",
                LmsSearchRequestStatus.Completed,
                null,
                stopwatch.ElapsedMilliseconds,
                observedCount),
            null);
    }

    private async Task<ObservedCatalogueSearch> ObserveCatalogueSearchAsync(
        string command,
        string query,
        RatingSearchConstraint? ratingConstraint,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var response = await catalogueSearch.SearchAsync(
                query,
                ratingConstraint,
                cancellationToken);
            stopwatch.Stop();
            return new ObservedCatalogueSearch(
                response,
                CatalogueRequest(
                    "catalogue-index",
                    command,
                    LmsSearchRequestStatus.Completed,
                    null,
                    stopwatch.ElapsedMilliseconds,
                    response.Candidates.Count),
                null);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            stopwatch.Stop();
            return new ObservedCatalogueSearch(
                null,
                CatalogueRequest(
                    "catalogue-index",
                    command,
                    LmsSearchRequestStatus.Failed,
                    exception.Message,
                    stopwatch.ElapsedMilliseconds,
                    0),
                exception);
        }
    }

    private async Task<SearchOutcome> HandleCatalogueFailureAsync(
        SearchObservationContext observation,
        IReadOnlyList<LmsSearchRequestObservation> catalogueRequests,
        Exception failure,
        long elapsedMilliseconds,
        LmsSearchResponse? playlistResponse,
        CancellationToken cancellationToken)
    {
        await observationRecorder.RecordCatalogueFailureAsync(
            observation,
            catalogueRequests,
            failure,
            elapsedMilliseconds,
            playlistResponse,
            cancellationToken);
        if (failure is CatalogueSearchUnavailableException)
        {
            return new SearchRejected(
                SearchRejectionReason.SearchUnavailable,
                failure.Message);
        }

        ExceptionDispatchInfo.Capture(failure).Throw();
        throw new UnreachableException();
    }

    private static LmsSearchRequestObservation CatalogueRequest(
        string source,
        string command,
        LmsSearchRequestStatus status,
        string? failureMessage,
        long durationMilliseconds,
        int resultCount) =>
        new(
            source,
            command,
            status,
            failureMessage,
            durationMilliseconds,
            resultCount);

    private static bool MatchesRating(
        int nativeRating,
        RatingSearchConstraint? ratingConstraint)
    {
        if (ratingConstraint is null)
        {
            return true;
        }

        var nativeThreshold = ratingConstraint.Rating * 20;
        return ratingConstraint.Match switch
        {
            RatingMatchMode.Exact => decimal.IsInteger(nativeThreshold)
                && nativeRating == decimal.ToInt32(nativeThreshold),
            RatingMatchMode.AtLeast => nativeRating >= decimal.ToInt32(
                decimal.Ceiling(nativeThreshold)),
            _ => false
        };
    }

    private static double TopTrackWeight(CatalogueSearchCandidate candidate) =>
        1 + Math.Max(0, candidate.NativeRating - 80) / 10d;

    private Candidate ToCandidate(
        CatalogueSearchCandidate candidate,
        CandidateGroup group) =>
        new(
            candidate.Identity,
            candidate.Title,
            candidate.Artist,
            candidate.Album,
            candidate.NativeRating,
            group);

    private SearchCandidateResult ToResult(SearchCandidateOccurrence candidate) =>
        new(
            referenceCodec.Encode(new SearchResultReferenceValue(
                candidate.CorrelationId,
                candidate.Identity)),
            candidate.Identity.Kind,
            candidate.Title,
            candidate.Artist,
            candidate.Album,
            candidate.NativeRating);

    private ExactArtistMatchResult ToExactArtistMatch(
        SearchCandidateOccurrence candidate) => new(
            candidate.Title,
            browseReferenceCodec.Encode(new BrowseReferenceValue(
                new BrowseTarget(
                    BrowseTargetKind.AlbumArtistAlbums,
                    candidate.Identity.Id,
                    0),
                null,
                candidate.CorrelationId)));

    [GeneratedRegex(
        @"(?:\b(?:rating|rated)\s*(?:(?:at\s+least|exactly|of)\s*)?(?::|=)?\s*\d+(?:\.\d+)?(?:\s*(?:\+|/5))?|\b\d+(?:\.\d+)?\s*(?:\+|/5)?\s*(?:star(?:s)?|rating)\b|\b[0-5](?:\.\d+)?\s*\+(?=\s|$))",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        100)]
    private static partial Regex RatingSyntax();

    private sealed record Candidate(
        MediaIdentity Identity,
        string Title,
        string? Artist,
        string? Album,
        int NativeRating = 0,
        CandidateGroup Group = CandidateGroup.Standard);

    private sealed record SelectedCandidate(
        CandidateGroup Group,
        SearchCandidateOccurrence Occurrence);

    private sealed record ObservedCatalogueSearch(
        CatalogueSearchResponse? Response,
        LmsSearchRequestObservation Request,
        Exception? Failure);

    private sealed record ArtistTrackSelection(
        IReadOnlyList<CatalogueSearchCandidate> TopTracks,
        IReadOnlyList<CatalogueSearchCandidate> Tracks,
        LmsSearchRequestObservation Request,
        Exception? Failure);

    private enum CandidateGroup
    {
        ExactArtist,
        Standard,
        TopTrack
    }
}
