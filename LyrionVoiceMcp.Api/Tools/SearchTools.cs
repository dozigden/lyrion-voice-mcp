using System.ComponentModel;
using System.Diagnostics;
using LyrionVoiceMcp.Abstractions;
using LyrionVoiceMcp.Contracts;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using ContractSearchCandidate = LyrionVoiceMcp.Contracts.SearchCandidate;
using ContractSearchEntityKind = LyrionVoiceMcp.Contracts.SearchEntityKind;

namespace LyrionVoiceMcp.Api.Tools;

[McpServerToolType]
public sealed class SearchTools(ISearchService searchService)
{
    [McpServerTool(
        Name = "search",
        Title = "Search the Lyrion music library",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(SearchResponse))]
    [Description("Search the whole configured Lyrion Music Server library. Optionally constrain track results by an exact or minimum numeric 0 to 5 rating.")]
    public async Task<CallToolResult> SearchAsync(
        [Description("The artist, album, track, or playlist text to search for, up to 500 characters and 20 words.")] string query,
        [Description("Optional numeric track rating from 0 to 5. Supply together with ratingMatch.")] decimal? rating = null,
        [Description("Optional rating comparison: exact or at_least. Supply together with rating.")] string? ratingMatch = null,
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
                SearchSucceeded succeeded => SuccessResult(
                    new SearchResponse(succeeded.Results.Select(MapCandidate).ToArray())),
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

    private static ContractSearchCandidate MapCandidate(SearchCandidateResult candidate)
    {
        var result = new ContractSearchCandidate(
            candidate.Reference,
            candidate.Kind switch
            {
                MediaEntityKind.Artist => ContractSearchEntityKind.Artist,
                MediaEntityKind.Album => ContractSearchEntityKind.Album,
                MediaEntityKind.Track => ContractSearchEntityKind.Track,
                MediaEntityKind.Playlist => ContractSearchEntityKind.Playlist,
                _ => throw new InvalidOperationException(
                    $"Unsupported search entity kind {candidate.Kind}.")
            },
            candidate.Title,
            candidate.Artist,
            candidate.Album);

        return candidate.Kind == MediaEntityKind.Track
            ? result with { Rating = candidate.NativeRating / 20m }
            : result;
    }

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
