using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
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
    [Description("Search the whole configured Lyrion Music Server library for artists, albums, tracks, and playlists. Track results include a 0 to 5 rating string or 'unrated'.")]
    public async Task<CallToolResult> SearchAsync(
        [Description("The artist, album, track, or playlist text to search for, up to 500 characters and 20 words.")] string query,
        CancellationToken cancellationToken)
    {
        try
        {
            var outcome = await searchService.SearchAsync(query, cancellationToken);
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
            ? result with { Rating = FormatRating(candidate.NativeRating) }
            : result;
    }

    private static string FormatRating(int? nativeRating)
    {
        if (nativeRating is null or 0)
        {
            return "unrated";
        }

        if (nativeRating is < 0 or > 100)
        {
            throw new InvalidOperationException(
                "A track search result contained a rating outside the LMS 0 to 100 scale.");
        }

        return (nativeRating.Value / 20m).ToString("0.##", CultureInfo.InvariantCulture);
    }
}
