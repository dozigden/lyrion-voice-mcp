using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Text.RegularExpressions;
using LyrionVoiceMcp.Abstractions;
using Microsoft.Extensions.Logging;

namespace LyrionVoiceMcp.Services;

internal sealed partial class SearchService(
    ICatalogueSearchResolver catalogueSearch,
    ICatalogueArtistTrackResolver artistTracks,
    ICatalogueTrackResolver tracks,
    ICatalogueAlbumResolver albums,
    ICatalogueArtistAlbumResolver artistAlbums,
    ILmsPlaylistSearchClient playlistSearch,
    ISearchResultReferenceCodec referenceCodec,
    IBrowseReferenceCodec browseReferenceCodec,
    SearchCandidateSelector candidateSelector,
    SearchObservationRecorder observationRecorder,
    TimeProvider timeProvider,
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
        if (criteria.Query?.Length > SearchQueryPolicy.MaximumLength)
        {
            return new SearchRejected(
                SearchRejectionReason.InvalidQuery,
                $"The search name must contain no more than {SearchQueryPolicy.MaximumLength} characters.");
        }

        if (criteria.Genre?.Length > SearchQueryPolicy.MaximumLength)
        {
            return new SearchRejected(
                SearchRejectionReason.InvalidQuery,
                $"The genre must contain no more than {SearchQueryPolicy.MaximumLength} characters.");
        }

        var query = string.IsNullOrWhiteSpace(criteria.Query) ? null : criteria.Query.Trim();
        var genre = SearchConstraintPolicy.NormaliseGenre(criteria.Genre);
        var yearValidation = SearchConstraintPolicy.NormaliseYearRange(
            criteria.FromYear,
            criteria.ToYear,
            timeProvider.GetUtcNow().Year);
        if (yearValidation.Error is not null)
        {
            return new SearchRejected(SearchRejectionReason.InvalidQuery, yearValidation.Error);
        }

        var yearRange = yearValidation.Value;
        var normalisedQuery = query ?? string.Empty;
        var tokenCount = SearchQueryPolicy.CountNormalisedTokens(normalisedQuery);
        if (query is not null && tokenCount == 0)
        {
            return new SearchRejected(
                SearchRejectionReason.InvalidQuery,
                "The search name must include media-name text; '*' is not a wildcard. Omit name for broad or filtered track discovery.");
        }

        if (query is not null && tokenCount > SearchQueryPolicy.MaximumTokenCount)
        {
            return new SearchRejected(
                SearchRejectionReason.InvalidQuery,
                $"The search name must contain no more than {SearchQueryPolicy.MaximumTokenCount} words.");
        }

        if (query is not null && RatingSyntax().IsMatch(normalisedQuery))
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

        var trackConstraint = new CatalogueTrackSearchConstraint(
            criteria.RatingConstraint,
            SearchConstraintPolicy.GenreKey(genre),
            yearRange?.FromYear,
            yearRange?.ToYear);
        var catalogueConstraint = CatalogueSearchConstraint.ForRequest(trackConstraint);
        var albumConstraint = catalogueConstraint.AlbumConstraint;
        var hasTrackConstraint = HasTrackConstraint(trackConstraint);
        var hasGenreOrYearConstraint = trackConstraint.GenreKey is not null
            || trackConstraint.FromYear is not null;
        var interpretation = query is not null
            ? SearchObservationInterpretation.Named
            : hasTrackConstraint
                ? SearchObservationInterpretation.NameFreeFiltered
                : SearchObservationInterpretation.BroadDiscovery;

        var observation = observationRecorder.Begin(
            criteria.Query ?? string.Empty,
            normalisedQuery,
            catalogueSearch.Descriptor,
            criteria.RatingConstraint,
            genre,
            yearRange,
            interpretation,
            albumConstraint is not null);
        var stopwatch = Stopwatch.StartNew();
        if (query is null)
        {
            return await SearchWithoutNameAsync(
                observation,
                trackConstraint,
                albumConstraint,
                stopwatch,
                cancellationToken);
        }

        var unconstrainedCatalogueTask = ObserveCatalogueSearchAsync(
            "search:unconstrained",
            normalisedQuery,
            null,
            cancellationToken);
        var catalogueTask = !hasTrackConstraint
            ? unconstrainedCatalogueTask
            : ObserveCatalogueSearchAsync(
                hasGenreOrYearConstraint
                    ? "search:requested-constraints"
                    : "search:requested-rating",
                normalisedQuery,
                catalogueConstraint,
                cancellationToken);
        var topTrackConstraint = TopTrackConstraint(trackConstraint);
        var topCatalogueTask = topTrackConstraint is null
            ? null
            : topTrackConstraint == trackConstraint
                ? catalogueTask
                : ObserveCatalogueSearchAsync(
                    hasGenreOrYearConstraint
                        ? "search:top-constraints"
                        : "search:top-rating",
                    normalisedQuery,
                    new CatalogueSearchConstraint(topTrackConstraint),
                    cancellationToken);
        var playlistTask = !hasTrackConstraint
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

        var catalogueCandidates = !hasTrackConstraint
            ? catalogueResponse!.Candidates
            : catalogueResponse!.Candidates
                .Where(candidate =>
                    candidate.Identity.Kind == MediaEntityKind.Track
                    || (albumConstraint is not null
                        && candidate.Identity.Kind == MediaEntityKind.Album))
                .ToArray();
        var exactArtist = ExactArtist(unconstrainedCatalogueResponse!.Candidates);
        IReadOnlyList<CatalogueSearchCandidate> selectedAlbums;
        IReadOnlyList<CatalogueSearchCandidate> selectedTopTracks;
        IReadOnlyList<CatalogueSearchCandidate> selectedTracks;
        if (exactArtist is null)
        {
            selectedAlbums = catalogueCandidates
                .Where(candidate => candidate.Identity.Kind == MediaEntityKind.Album)
                .Take(SearchResultPolicy.AlbumLimit)
                .ToArray();
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
                trackConstraint,
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
            if (!hasTrackConstraint || albumConstraint is not null)
            {
                var albumSelection = await SelectArtistAlbumsAsync(
                    exactArtist.Identity.Id,
                    albumConstraint,
                    cancellationToken);
                catalogueRequests.Add(albumSelection.Request);
                if (albumSelection.Failure is not null)
                {
                    stopwatch.Stop();
                    return await HandleCatalogueFailureAsync(
                        observation,
                        catalogueRequests,
                        albumSelection.Failure,
                        stopwatch.ElapsedMilliseconds,
                        playlistResponse,
                        cancellationToken);
                }

                selectedAlbums = albumSelection.Albums;
            }
            else
            {
                selectedAlbums = [];
            }
        }

        var exactArtistCandidate = exactArtist is null
            ? Array.Empty<Candidate>()
            : [ToCandidate(exactArtist, CandidateGroup.ExactArtist)];
        var nonTrackCandidates = catalogueCandidates
            .Where(candidate => exactArtist is null
                && candidate.Identity.Kind == MediaEntityKind.Artist)
            .Take(SearchResultPolicy.ArtistLimit)
            .Concat(selectedAlbums)
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

    private async Task<SearchOutcome> SearchWithoutNameAsync(
        SearchObservationContext observation,
        CatalogueTrackSearchConstraint trackConstraint,
        CatalogueAlbumSearchConstraint? albumConstraint,
        Stopwatch stopwatch,
        CancellationToken cancellationToken)
    {
        var isBroadDiscovery = observation.Interpretation
            == SearchObservationInterpretation.BroadDiscovery;
        var trackSelectionTask = SelectNameFreeTracksAsync(
            trackConstraint,
            isBroadDiscovery,
            cancellationToken);
        var albumSelectionTask = albumConstraint is null
            ? null
            : SelectNameFreeAlbumsAsync(albumConstraint, cancellationToken);
        var trackSelection = await trackSelectionTask;
        var albumSelection = albumSelectionTask is null
            ? null
            : await albumSelectionTask;
        IReadOnlyList<LmsSearchRequestObservation> requests = albumSelection is null
            ? [trackSelection.Request]
            : [trackSelection.Request, albumSelection.Request];
        var failure = trackSelection.Failure ?? albumSelection?.Failure;
        if (failure is not null)
        {
            stopwatch.Stop();
            return await HandleCatalogueFailureAsync(
                observation,
                requests,
                failure,
                stopwatch.ElapsedMilliseconds,
                null,
                cancellationToken);
        }

        var candidates = (albumSelection?.Albums ?? [])
            .Select(candidate => ToCandidate(candidate, CandidateGroup.Standard))
            .Concat(trackSelection.TopTracks
                .Select(candidate => ToCandidate(candidate, CandidateGroup.TopTrack))
                .Concat(trackSelection.Tracks.Select(candidate => ToCandidate(
                    candidate,
                    CandidateGroup.Standard))))
            .Select((candidate, index) => new SelectedCandidate(
                candidate.Group,
                new SearchCandidateOccurrence(
                    index + 1,
                    Guid.NewGuid().ToString("N"),
                    candidate.Identity,
                    candidate.Title,
                    candidate.Artist,
                    candidate.Album,
                    candidate.NativeRating)))
            .ToArray();
        var observedCandidates = candidates
            .Select(candidate => candidate.Occurrence)
            .ToArray();
        stopwatch.Stop();
        await observationRecorder.RecordCompletedAsync(
            observation,
            requests,
            null,
            observedCandidates,
            stopwatch.ElapsedMilliseconds,
            cancellationToken);
        var results = candidates
            .Where(candidate => candidate.Group == CandidateGroup.Standard)
            .Select(candidate => ToResult(candidate.Occurrence))
            .ToArray();
        var topResults = candidates
            .Where(candidate => candidate.Group == CandidateGroup.TopTrack)
            .Select(candidate => ToResult(candidate.Occurrence))
            .ToArray();

        logger.LogInformation(
            "Name-free {Interpretation} media search returned {AlbumCount} albums, {TopTrackCount} top tracks, and {TrackCount} tracks in {ElapsedMilliseconds} ms.",
            isBroadDiscovery ? "broad" : "filtered",
            results.Count(result => result.Kind == MediaEntityKind.Album),
            topResults.Length,
            results.Count(result => result.Kind == MediaEntityKind.Track),
            stopwatch.ElapsedMilliseconds);
        return new SearchSucceeded(results, topResults);
    }

    private static CatalogueTrackSearchConstraint? TopTrackConstraint(
        CatalogueTrackSearchConstraint constraint)
    {
        var topRating = constraint.RatingConstraint switch
        {
            null => new RatingSearchConstraint(4, RatingMatchMode.AtLeast),
            { Match: RatingMatchMode.Exact, Rating: < 4 } => null,
            { Match: RatingMatchMode.Exact } exact => exact,
            { Match: RatingMatchMode.AtLeast, Rating: >= 4 } minimum => minimum,
            { Match: RatingMatchMode.AtLeast } => new RatingSearchConstraint(
                4,
                RatingMatchMode.AtLeast),
            _ => null
        };
        return topRating is null
            ? null
            : constraint with { RatingConstraint = topRating };
    }

    private static bool HasTrackConstraint(CatalogueTrackSearchConstraint constraint) =>
        constraint.RatingConstraint is not null
        || constraint.GenreKey is not null
        || constraint.FromYear is not null
        || constraint.ToYear is not null;

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
        CatalogueTrackSearchConstraint constraint,
        CancellationToken cancellationToken)
    {
        var effectiveConstraint = HasTrackConstraint(constraint) ? constraint : null;
        return await SelectTracksAsync(
            artistTracks.ReadArtistTracksAsync(
                artistId,
                effectiveConstraint,
                cancellationToken),
            "catalogue-artist-tracks",
            "artist-tracks",
            cancellationToken);
    }

    private async Task<ArtistTrackSelection> SelectNameFreeTracksAsync(
        CatalogueTrackSearchConstraint constraint,
        bool isBroadDiscovery,
        CancellationToken cancellationToken) =>
        await SelectTracksAsync(
            tracks.ReadTracksAsync(constraint, cancellationToken),
            isBroadDiscovery ? "catalogue-broad-tracks" : "catalogue-filtered-tracks",
            isBroadDiscovery ? "broad-tracks" : "filtered-tracks",
            cancellationToken);

    private async Task<ArtistTrackSelection> SelectTracksAsync(
        IAsyncEnumerable<CatalogueSearchCandidate> candidates,
        string source,
        string command,
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
            await foreach (var candidate in candidates.WithCancellation(cancellationToken))
            {
                observedCount++;
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
                    source,
                    command,
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
                source,
                command,
                LmsSearchRequestStatus.Completed,
                null,
                stopwatch.ElapsedMilliseconds,
                observedCount),
            null);
    }

    private async Task<AlbumSelection> SelectArtistAlbumsAsync(
        string artistId,
        CatalogueAlbumSearchConstraint? constraint,
        CancellationToken cancellationToken) =>
        await SelectAlbumsAsync(
            artistAlbums.ReadArtistAlbumsAsync(
                artistId,
                constraint,
                cancellationToken),
            "catalogue-artist-albums",
            "artist-albums",
            cancellationToken);

    private async Task<AlbumSelection> SelectNameFreeAlbumsAsync(
        CatalogueAlbumSearchConstraint constraint,
        CancellationToken cancellationToken) =>
        await SelectAlbumsAsync(
            albums.ReadAlbumsAsync(constraint, cancellationToken),
            "catalogue-filtered-albums",
            "filtered-albums",
            cancellationToken);

    private async Task<AlbumSelection> SelectAlbumsAsync(
        IAsyncEnumerable<CatalogueSearchCandidate> candidates,
        string source,
        string command,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var observedCount = 0;
        var reservoir = candidateSelector.CreateReservoir(
            SearchResultPolicy.AlbumReservoirLimit);
        try
        {
            await foreach (var candidate in candidates.WithCancellation(cancellationToken))
            {
                observedCount++;
                reservoir.Consider(candidate);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            stopwatch.Stop();
            return new AlbumSelection(
                [],
                CatalogueRequest(
                    source,
                    command,
                    LmsSearchRequestStatus.Failed,
                    exception.Message,
                    stopwatch.ElapsedMilliseconds,
                    observedCount),
                exception);
        }

        stopwatch.Stop();
        return new AlbumSelection(
            candidateSelector.Rotate(
                reservoir.Candidates,
                SearchResultPolicy.AlbumLimit),
            CatalogueRequest(
                source,
                command,
                LmsSearchRequestStatus.Completed,
                null,
                stopwatch.ElapsedMilliseconds,
                observedCount),
            null);
    }

    private async Task<ObservedCatalogueSearch> ObserveCatalogueSearchAsync(
        string command,
        string query,
        CatalogueSearchConstraint? constraint,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var response = await catalogueSearch.SearchAsync(
                query,
                constraint,
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

    private sealed record AlbumSelection(
        IReadOnlyList<CatalogueSearchCandidate> Albums,
        LmsSearchRequestObservation Request,
        Exception? Failure);

    private enum CandidateGroup
    {
        ExactArtist,
        Standard,
        TopTrack
    }
}
