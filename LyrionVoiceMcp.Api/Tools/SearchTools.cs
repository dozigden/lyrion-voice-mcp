using System.ComponentModel;
using System.Diagnostics;
using LyrionVoiceMcp.Abstractions;
using LyrionVoiceMcp.Contracts;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace LyrionVoiceMcp.Api.Tools;

[McpServerToolType]
public sealed class SearchTools(ISearchService searchService)
{
    private const string ReferenceGuidance =
        "Pass a browseRef to the browse tool to open that location in the library tree. Browse results can contain further browseRefs; pass those back to browse to continue navigating.";

    [McpServerTool(
        Name = "search",
        Title = "Search the Lyrion music library",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(SearchResponse))]
    [Description("Search for named artists, albums, tracks, or playlists. To constrain matching tracks by rating, supply both rating and ratingMatch. For rating-only exploration, use browse and open Ratings; '*' is not a wildcard.")]
    public async Task<CallToolResult> SearchAsync(
        [Description("Meaningful artist, album, track, or playlist name text to search for, up to 500 characters and 20 words. Wildcards are not supported.")] string query,
        [Description("Optional numeric track rating from 0 to 5, including decimals. Supply together with ratingMatch.")] decimal? rating = null,
        [Description("Optional rating comparison supplied with rating. Use exact for exactly that rating. Use at_least for that rating or higher; rating 4 with at_least means 4+.")] string? ratingMatch = null,
        CancellationToken cancellationToken = default)
    {
        var constraint = CreateRatingConstraint(rating, ratingMatch);
        if (constraint.Error is not null)
        {
            return ErrorResult(constraint.Error);
        }

        try
        {
            var outcome = await searchService.SearchAsync(
                new SearchCriteria(query, constraint.Value),
                cancellationToken);
            return outcome switch
            {
                SearchSucceeded succeeded => SuccessResult(MapResponse(succeeded.Results)),
                SearchRejected rejected => ErrorResult(rejected.Message),
                _ => throw new UnreachableException(
                    $"Unsupported search outcome {outcome.GetType().Name}.")
            };
        }
        catch (LmsRequestException exception)
        {
            return ErrorResult(exception.Message);
        }
    }

    private static CallToolResult SuccessResult(SearchResponse response)
    {
        var structuredContent = McpToolJson.Serialize(response);
        return new CallToolResult
        {
            Content = [new TextContentBlock { Text = structuredContent.GetRawText() }],
            StructuredContent = structuredContent
        };
    }

    private static CallToolResult ErrorResult(string message) =>
        new()
        {
            Content = [new TextContentBlock { Text = message }],
            IsError = true
        };

    private static SearchResponse MapResponse(
        IReadOnlyList<SearchCandidateResult> candidates) =>
        new(
            ReferenceGuidance,
            candidates
                .Where(candidate => candidate.Kind == MediaEntityKind.Artist)
                .Select(candidate => new SearchArtist(
                    candidate.Title,
                    candidate.Reference))
                .ToArray(),
            candidates
                .Where(candidate => candidate.Kind == MediaEntityKind.Album)
                .Select(candidate => new SearchAlbum(
                    candidate.Title,
                    candidate.Artist,
                    candidate.Reference,
                    candidate.Reference))
                .ToArray(),
            candidates
                .Where(candidate => candidate.Kind == MediaEntityKind.Track)
                .Select(candidate => new SearchTrack(
                    candidate.Title,
                    candidate.Artist,
                    candidate.Album,
                    candidate.NativeRating / 20m,
                    candidate.Reference))
                .ToArray(),
            candidates
                .Where(candidate => candidate.Kind == MediaEntityKind.Playlist)
                .Select(candidate => new SearchPlaylist(
                    candidate.Title,
                    candidate.Reference,
                    candidate.Reference))
                .ToArray());

    private static (RatingSearchConstraint? Value, string? Error) CreateRatingConstraint(
        decimal? rating,
        string? ratingMatch)
    {
        if (rating is null && ratingMatch is null)
        {
            return (null, null);
        }

        if (rating is null || ratingMatch is null)
        {
            return (null, "rating and ratingMatch must be supplied together.");
        }

        var match = ratingMatch switch
        {
            "exact" => RatingMatchMode.Exact,
            "at_least" => RatingMatchMode.AtLeast,
            _ => (RatingMatchMode?)null
        };
        if (match is null)
        {
            return (null, "ratingMatch must be exact or at_least.");
        }

        if (rating is < 0 or > 5)
        {
            return (null, "rating must be from 0 to 5.");
        }

        return (new RatingSearchConstraint(rating.Value, match.Value), null);
    }
}
