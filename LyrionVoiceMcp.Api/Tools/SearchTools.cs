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
        "When exactArtistMatch is present, the query resolved to that artist, artists is empty, and discographyBrowseRef opens every album credited to that album artist. Otherwise artists contains unresolved artist candidates. topTracks are relevant tracks rated 4 or higher; tracks are varied matches and exclude tracks already shown in topTracks. Pass a browseRef to browse to continue navigating.";

    [McpServerTool(
        Name = "search",
        Title = "Search the Lyrion music library",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(SearchResponse))]
    [Description("Search for artists, albums, tracks, or playlists by name. Reports a unique exact artist separately with a complete discography browse reference, returns relevant 4+ top tracks separately, and varies equally relevant track matches. Use rating and ratingMatch to narrow tracks. * is not a wildcard.")]
    public async Task<CallToolResult> SearchAsync(
        [Description("Artist, album, track, or playlist name text only, up to 500 characters and 20 words. Do not include ratings or search syntax; use rating and ratingMatch instead. Wildcards are not supported.")] string name,
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
                new SearchCriteria(name, constraint.Value),
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
