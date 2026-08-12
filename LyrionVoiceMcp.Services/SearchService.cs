using System.Diagnostics;
using LyrionVoiceMcp.Abstractions;
using Microsoft.Extensions.Logging;

namespace LyrionVoiceMcp.Services;

public sealed class SearchService(
    ILmsSearchClient lmsSearchClient,
    ISearchResultReferenceCodec referenceCodec,
    ISearchObservationStore observationStore,
    TimeProvider timeProvider,
    ILogger<SearchService> logger) : ISearchService
{
    public SearchService(
        ILmsSearchClient lmsSearchClient,
        ISearchResultReferenceCodec referenceCodec,
        ILogger<SearchService> logger)
        : this(
            lmsSearchClient,
            referenceCodec,
            NullSearchObservationStore.Instance,
            TimeProvider.System,
            logger)
    {
    }

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

        var normalisedQuery = query.Trim();
        var observationId = Guid.NewGuid().ToString("N");
        var createdAt = timeProvider.GetUtcNow();
        var stopwatch = Stopwatch.StartNew();
        LmsSearchResponse lmsResponse;
        try
        {
            lmsResponse = await lmsSearchClient.SearchAsync(
                normalisedQuery,
                cancellationToken);
        }
        catch (LmsSearchFailedException exception)
        {
            stopwatch.Stop();
            var failedCandidates = CreateObservationCandidates(exception.Response.Candidates);
            await TryRecordAsync(new SearchObservation(
                observationId,
                createdAt,
                query,
                normalisedQuery,
                null,
                "lms",
                "whole_library",
                "lms-pass-through",
                "1",
                SearchObservationStatus.Failed,
                exception.Message,
                stopwatch.ElapsedMilliseconds,
                exception.Response.RetrievalDurationMilliseconds,
                Math.Max(0, stopwatch.ElapsedMilliseconds - exception.Response.RetrievalDurationMilliseconds),
                exception.Response.Requests,
                failedCandidates,
                null), cancellationToken);
            throw;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            stopwatch.Stop();
            await TryRecordAsync(CreateFailedObservation(
                observationId,
                createdAt,
                query,
                normalisedQuery,
                exception,
                stopwatch.ElapsedMilliseconds), cancellationToken);
            throw;
        }

        var processingStartedAt = stopwatch.ElapsedMilliseconds;
        var candidates = CreateObservationCandidates(lmsResponse.Candidates);

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
        stopwatch.Stop();

        await TryRecordAsync(new SearchObservation(
            observationId,
            createdAt,
            query,
            normalisedQuery,
            null,
            "lms",
            "whole_library",
            "lms-pass-through",
            "1",
            SearchObservationStatus.Completed,
            null,
            stopwatch.ElapsedMilliseconds,
            lmsResponse.RetrievalDurationMilliseconds,
            Math.Max(0, stopwatch.ElapsedMilliseconds - processingStartedAt),
            lmsResponse.Requests,
            candidates,
            null), cancellationToken);

        logger.LogInformation(
            "LMS search for {Query} returned {ResultCount} candidates in {ElapsedMilliseconds} ms.",
            normalisedQuery,
            results.Length,
            stopwatch.ElapsedMilliseconds);

        return new SearchSucceeded(results);
    }

    private static SearchObservationCandidate[] CreateObservationCandidates(
        IReadOnlyList<LmsSearchCandidate> candidates) =>
        candidates.Select((candidate, index) => new SearchObservationCandidate(
            index + 1,
            Guid.NewGuid().ToString("N"),
            candidate.Identity,
            candidate.Title,
            candidate.Artist,
            candidate.Album,
            null)).ToArray();

    private static SearchObservation CreateFailedObservation(
        string id,
        DateTimeOffset createdAt,
        string originalQuery,
        string normalisedQuery,
        Exception exception,
        long elapsedMilliseconds) => new(
            id,
            createdAt,
            originalQuery,
            normalisedQuery,
            null,
            "lms",
            "whole_library",
            "lms-pass-through",
            "1",
            SearchObservationStatus.Failed,
            exception.Message,
            elapsedMilliseconds,
            elapsedMilliseconds,
            0,
            [],
            [],
            null);

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
}
