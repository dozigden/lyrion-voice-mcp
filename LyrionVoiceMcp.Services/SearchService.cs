using System.Diagnostics;
using LyrionVoiceMcp.Abstractions;
using Microsoft.Extensions.Logging;

namespace LyrionVoiceMcp.Services;

public sealed class SearchService(
    ILmsSearchClient lmsSearchClient,
    ISearchResultReferenceCodec referenceCodec,
    ILogger<SearchService> logger) : ISearchService
{
    public async Task<IReadOnlyList<SearchCandidateResult>> SearchAsync(
        string query,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            throw new ArgumentException("The search query must not be empty.", nameof(query));
        }

        var normalisedQuery = query.Trim();
        var stopwatch = Stopwatch.StartNew();
        var lmsCandidates = await lmsSearchClient.SearchAsync(
            normalisedQuery,
            cancellationToken);
        stopwatch.Stop();

        var results = lmsCandidates
            .Select(candidate => new SearchCandidateResult(
                referenceCodec.Encode(new SearchResultReferenceValue(
                    Guid.NewGuid().ToString("N"),
                    candidate.Identity)),
                candidate.Identity.Kind,
                candidate.Title,
                candidate.Artist,
                candidate.Album))
            .ToArray();

        logger.LogInformation(
            "LMS search for {Query} returned {ResultCount} candidates in {ElapsedMilliseconds} ms.",
            normalisedQuery,
            results.Length,
            stopwatch.ElapsedMilliseconds);

        return results;
    }
}
