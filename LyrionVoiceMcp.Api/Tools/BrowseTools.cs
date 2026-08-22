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
    private const string ReferenceGuidance =
        "Pass a browseRef to the browse tool to open that location in the library tree. Browse results can contain further browseRefs; pass those back to browse to continue navigating.";

    [McpServerTool(
        Name = "browse",
        Title = "Browse the Lyrion music library",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(BrowseResponse))]
    [Description("Browse the configured Lyrion Music Server's local-library tree. Omit browseRef to list its roots, including Ratings, or pass a browseRef returned by search or browse to descend.")]
    public async Task<CallToolResult> BrowseAsync(
        [Description("An opaque browseRef returned by search or browse. Omit it to list the browse roots; pass returned browseRefs back to browse to continue through the tree.")] string? browseRef = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var outcome = await browseService.BrowseAsync(browseRef, cancellationToken);
            return outcome switch
            {
                BrowseSucceeded succeeded => SuccessResult(new BrowseResponse(
                    ReferenceGuidance,
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
            item.Album)
        {
            BrowseRef = item.HasBrowseReference ? item.Reference : null,
            PlayRef = item.HasPlayReference ? item.Reference : null
        };

        return item.NativeRating is null
            ? result
            : result with { Rating = item.NativeRating.Value / 20m };
    }
}
