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
        "When exactArtistMatch is present, the query resolved to that artist, artists is empty, the albums group is a varied discography preview for an unconstrained named search, and discographyBrowseRef opens every album credited to that album artist. Otherwise artists and albums contain ordinary search candidates. topTracks are relevant or discovered tracks rated 4 or higher; tracks are varied matches or discoveries and exclude tracks already shown in topTracks. Genre, year, and rating constraints apply to tracks; constrained searches do not return albums. Pass a browseRef to browse to continue navigating.";

    [McpServerTool(
        Name = "search",
        Title = "Search the Lyrion music library",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(SearchResponse))]
    [Description("Search the music library by optional name, exact genre, inclusive year range, rating, or a combination. Omit every input for broad varied track discovery. Reports a unique exact artist separately, returns 4+ top tracks separately, and varies track selections. Genre, years, and rating narrow tracks. * is not a wildcard.")]
    public async Task<CallToolResult> SearchAsync(
        [Description("Optional artist, album, track, or playlist name text, up to 500 characters and 20 words. Omit it or leave it blank for rating-, genre-, or year-filtered track discovery; omit every input for broad varied discovery. Do not include constraints or search syntax in the name. Wildcards are not supported.")] string? name = null,
        [Description("Optional single canonical genre name. Matching is case-insensitive but otherwise exact. Do not supply a list or put the genre in name.")] string? genre = null,
        [Description("Optional inclusive start year, supplied together with toYear. Four-digit years from 1000 through next year are accepted. Two digits use the most recent applicable century; for a decade use its first and last years, for example 90 and 99.")] int? fromYear = null,
        [Description("Optional inclusive end year, supplied together with fromYear. Reversed bounds are accepted and normalised. Four-digit years from 1000 through next year or two-digit years are accepted.")] int? toYear = null,
        [Description("Optional numeric track rating from 0 to 5, including decimals. Supply together with ratingMatch; do not put the rating in name.")] decimal? rating = null,
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
                new SearchCriteria(
                    name,
                    constraint.Value,
                    genre,
                    fromYear,
                    toYear),
                cancellationToken);
            return outcome switch
            {
                SearchSucceeded succeeded => SuccessResult(MapResponse(
                    succeeded.Results,
                    succeeded.TopTracks,
                    succeeded.ExactArtistMatch)),
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
        IReadOnlyList<SearchCandidateResult> candidates,
        IReadOnlyList<SearchCandidateResult> topTracks,
        ExactArtistMatchResult? exactArtistMatch) =>
        new(
            ReferenceGuidance,
            exactArtistMatch is null
                ? null
                : new SearchExactArtistMatch(
                    exactArtistMatch.Name,
                    exactArtistMatch.DiscographyReference),
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
            topTracks.Select(MapTrack).ToArray(),
            candidates
                .Where(candidate => candidate.Kind == MediaEntityKind.Track)
                .Select(MapTrack)
                .ToArray(),
            candidates
                .Where(candidate => candidate.Kind == MediaEntityKind.Playlist)
                .Select(candidate => new SearchPlaylist(
                    candidate.Title,
                    candidate.Reference,
                    candidate.Reference))
                .ToArray());

    private static SearchTrack MapTrack(SearchCandidateResult candidate) =>
        new(
            candidate.Title,
            candidate.Artist,
            candidate.Album,
            candidate.NativeRating / 20m,
            candidate.Reference);

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
