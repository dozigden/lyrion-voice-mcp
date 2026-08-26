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
        SearchResolverDescriptor resolver,
        RatingSearchConstraint? ratingConstraint = null,
        string? genre = null,
        YearSearchRange? yearRange = null,
        SearchObservationInterpretation interpretation = SearchObservationInterpretation.Named) => new(
            Guid.NewGuid().ToString("N"),
            timeProvider.GetUtcNow(),
            originalQuery,
            normalisedQuery,
            resolver,
            ratingConstraint,
            genre,
            yearRange,
            interpretation);

    public Task RecordCompletedAsync(
        SearchObservationContext context,
        IReadOnlyList<LmsSearchRequestObservation> catalogueRequests,
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
                RetrievalDuration(catalogueRequests, playlists),
                [.. catalogueRequests, .. playlists?.Requests ?? []],
                CreateObservationCandidates(candidates)),
            cancellationToken);

    public Task RecordCatalogueFailureAsync(
        SearchObservationContext context,
        IReadOnlyList<LmsSearchRequestObservation> catalogueRequests,
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
                RetrievalDuration(catalogueRequests, playlists),
                [.. catalogueRequests, .. playlists?.Requests ?? []],
                CreatePlaylistObservationCandidates(playlists)),
            cancellationToken);

    public Task RecordPlaylistFailureAsync(
        SearchObservationContext context,
        IReadOnlyList<LmsSearchRequestObservation> catalogueRequests,
        LmsSearchResponse? playlists,
        IReadOnlyList<SearchCandidateOccurrence> candidates,
        Exception failure,
        long totalDurationMilliseconds,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<LmsSearchRequestObservation> requests =
            [.. catalogueRequests, .. playlists?.Requests ?? []];
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
                RetrievalDuration(catalogueRequests, playlists),
                requests,
                CreateObservationCandidates(candidates)),
            cancellationToken);
    }

    private static SearchObservationCandidate[] CreateObservationCandidates(
        IReadOnlyList<SearchCandidateOccurrence> candidates) =>
        candidates.Select(candidate => new SearchObservationCandidate(
            candidate.Position,
            candidate.CorrelationId,
            candidate.Identity,
            candidate.Title,
            candidate.Artist,
            candidate.Album,
            null,
            candidate.Identity.Kind == MediaEntityKind.Track
                ? candidate.NativeRating / 20m
                : null,
            candidate.IsExactArtistMatch)).ToArray();

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
                null,
                null))
            .ToArray();

    private static long RetrievalDuration(
        IReadOnlyList<LmsSearchRequestObservation> catalogueRequests,
        LmsSearchResponse? playlists)
    {
        var initialCatalogueDuration = catalogueRequests
            .Where(request => request.Source == "catalogue-index")
            .Select(request => request.DurationMilliseconds)
            .DefaultIfEmpty()
            .Max();
        var artistExpansionDuration = catalogueRequests
            .Where(request => request.Source is
                "catalogue-artist-tracks" or "catalogue-artist-albums"
                or "catalogue-filtered-tracks" or "catalogue-broad-tracks")
            .Sum(request => request.DurationMilliseconds);
        return Math.Max(
                initialCatalogueDuration,
                playlists?.RetrievalDurationMilliseconds ?? 0)
            + artistExpansionDuration;
    }

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
            context.HasTrackConstraint || context.IsNameFree
                ? MediaEntityKind.Track
                : null,
            context.HasTrackConstraint || context.IsNameFree
                ? "catalogue"
                : "catalogue+lms",
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
            null,
            context.RatingConstraint,
            context.Genre,
            context.YearRange?.RequestedFromYear,
            context.YearRange?.RequestedToYear,
            context.YearRange?.FromYear,
            context.YearRange?.ToYear,
            context.Interpretation);

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
    SearchResolverDescriptor Resolver,
    RatingSearchConstraint? RatingConstraint,
    string? Genre,
    YearSearchRange? YearRange,
    SearchObservationInterpretation Interpretation)
{
    public bool HasTrackConstraint =>
        RatingConstraint is not null || Genre is not null || YearRange is not null;

    public bool IsNameFree => Interpretation is
        SearchObservationInterpretation.NameFreeFiltered
        or SearchObservationInterpretation.BroadDiscovery;
}

internal sealed record SearchCandidateOccurrence(
    int Position,
    string CorrelationId,
    MediaIdentity Identity,
    string Title,
    string? Artist,
    string? Album,
    int NativeRating = 0,
    bool IsExactArtistMatch = false);
