using System.ComponentModel;
using LyrionVoiceMcp.Abstractions;
using LyrionVoiceMcp.Contracts;
using ModelContextProtocol;
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
        UseStructuredContent = true)]
    [Description("Search the whole configured Lyrion Music Server library for artists, albums, tracks, and playlists.")]
    public async Task<SearchResponse> SearchAsync(
        [Description("The artist, album, track, or playlist text to search for.")] string query,
        CancellationToken cancellationToken)
    {
        try
        {
            var results = await searchService.SearchAsync(query, cancellationToken);
            return new SearchResponse(results.Select(MapCandidate).ToArray());
        }
        catch (ArgumentException exception)
        {
            throw new McpException(exception.Message);
        }
        catch (LmsRequestException exception)
        {
            throw new McpException(exception.Message);
        }
    }

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
