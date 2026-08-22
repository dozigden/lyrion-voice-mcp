using System.ComponentModel;
using System.Diagnostics;
using LyrionVoiceMcp.Abstractions;
using LyrionVoiceMcp.Contracts;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using ContractBrowseEntityKind = LyrionVoiceMcp.Contracts.BrowseEntityKind;
using ContractBrowseItem = LyrionVoiceMcp.Contracts.BrowseItem;

namespace LyrionVoiceMcp.Api.Tools;

[McpServerToolType]
public sealed class BrowseTools(IBrowseService browseService)
{
    [McpServerTool(
        Name = "browse",
        Title = "Browse the Lyrion music library",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(BrowseResponse))]
    [Description("Browse the configured Lyrion Music Server's local library. Omit the reference to list the browse roots, or pass a browsable search or browse reference to descend.")]
    public async Task<CallToolResult> BrowseAsync(
        [Description("An opaque browsable result or continuation reference returned by search or browse. Omit it to list the browse roots.")] string? reference = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var outcome = await browseService.BrowseAsync(reference, cancellationToken);
            return outcome switch
            {
                BrowseSucceeded succeeded => SuccessResult(new BrowseResponse(
                    succeeded.Items.Select(MapItem).ToArray(),
                    succeeded.Continuation)),
                BrowseRejected rejected => ErrorResult(rejected.Message),
                _ => throw new UnreachableException(
                    $"Unsupported browse outcome {outcome.GetType().Name}.")
            };
        }
        catch (LmsRequestException exception)
        {
            return ErrorResult(exception.Message);
        }
    }

    private static CallToolResult SuccessResult(BrowseResponse response)
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

    private static ContractBrowseItem MapItem(BrowseItemResult item)
    {
        var result = new ContractBrowseItem(
            item.Reference,
            item.Kind switch
            {
                BrowseItemKind.Category => ContractBrowseEntityKind.Category,
                BrowseItemKind.AlbumArtist => ContractBrowseEntityKind.AlbumArtist,
                BrowseItemKind.Artist => ContractBrowseEntityKind.Artist,
                BrowseItemKind.Album => ContractBrowseEntityKind.Album,
                BrowseItemKind.Genre => ContractBrowseEntityKind.Genre,
                BrowseItemKind.Playlist => ContractBrowseEntityKind.Playlist,
                BrowseItemKind.Track => ContractBrowseEntityKind.Track,
                BrowseItemKind.Year => ContractBrowseEntityKind.Year,
                _ => throw new InvalidOperationException(
                    $"Unsupported browse item kind {item.Kind}.")
            },
            item.Title,
            item.Artist,
            item.Album,
            item.Browsable,
            item.Playable);

        return item.NativeRating is null
            ? result
            : result with { Rating = item.NativeRating.Value / 20m };
    }
}
