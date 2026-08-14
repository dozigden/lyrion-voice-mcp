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
    [Description("Search the whole configured Lyrion Music Server library for artists, albums, tracks, and playlists.")]
    public async Task<CallToolResult> SearchAsync(
        [Description("The artist, album, track, or playlist text to search for.")] string query,
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

    private static ContractSearchCandidate MapCandidate(SearchCandidateResult candidate) =>
        new(
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
}
