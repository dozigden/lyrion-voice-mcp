using LyrionVoiceMcp.Abstractions;

namespace LyrionVoiceMcp.Services;

public sealed class BrowseService(
    ILmsBrowseClient lmsBrowseClient,
    IBrowseReferenceCodec browseReferenceCodec,
    ISearchResultReferenceCodec searchReferenceCodec) : IBrowseService
{
    private const int PageSize = 50;

    public async Task<BrowseOutcome> BrowseAsync(
        string? reference,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (reference is null)
        {
            return new BrowseSucceeded(RootItems(), null);
        }

        var decoded = DecodeReference(reference);
        if (decoded is null)
        {
            return new BrowseRejected(
                BrowseRejectionReason.InvalidReference,
                "The browse reference is invalid.");
        }

        if (decoded.Target is null)
        {
            return new BrowseRejected(
                BrowseRejectionReason.NotBrowsable,
                "The selected browse item cannot be browsed.");
        }

        var target = decoded.Target;
        var page = await lmsBrowseClient.BrowseAsync(
            new LmsBrowseRequest(
                target.Kind,
                target.FilterId,
                target.Offset,
                PageSize),
            cancellationToken);
        var items = page.Items
            .Select(item => MapItem(item, decoded.SearchCorrelationId))
            .ToArray();
        if (page.Items.Count == 0 && target.Offset < page.TotalCount)
        {
            throw new LmsRequestException(
                "LMS browse returned no items before the end of the result set.");
        }

        var nextOffset = target.Offset + page.Items.Count;
        var continuation = nextOffset < page.TotalCount
            ? browseReferenceCodec.Encode(new BrowseReferenceValue(
                target with { Offset = nextOffset },
                null,
                decoded.SearchCorrelationId))
            : null;
        return new BrowseSucceeded(items, continuation);
    }

    private IReadOnlyList<BrowseItemResult> RootItems() =>
    [
        RootItem("Album artists", LmsBrowseQueryKind.AlbumArtists),
        RootItem("Artists", LmsBrowseQueryKind.Artists),
        RootItem("Albums", LmsBrowseQueryKind.Albums),
        RootItem("Genres", LmsBrowseQueryKind.Genres),
        RootItem("Playlists", LmsBrowseQueryKind.Playlists),
        RootItem("Recently added", LmsBrowseQueryKind.RecentlyAddedAlbums),
        RootItem("Years", LmsBrowseQueryKind.Years)
    ];

    private BrowseItemResult RootItem(string title, LmsBrowseQueryKind kind) =>
        new(
            browseReferenceCodec.Encode(new BrowseReferenceValue(
                new BrowseTarget(kind, null, 0),
                null)),
            BrowseItemKind.Category,
            title,
            null,
            null,
            true,
            false);

    private BrowseItemResult MapItem(
        LmsBrowseItem item,
        string? searchCorrelationId)
    {
        var target = NextTarget(item);
        var media = PlayableMedia(item);
        return new BrowseItemResult(
            browseReferenceCodec.Encode(new BrowseReferenceValue(
                target,
                media,
                searchCorrelationId)),
            item.Kind,
            item.Title,
            item.Artist,
            item.Album,
            target is not null,
            media is not null);
    }

    private BrowseReferenceValue? DecodeReference(string reference)
    {
        var browseReference = browseReferenceCodec.TryDecode(reference);
        if (browseReference is not null)
        {
            return browseReference;
        }

        var searchReference = searchReferenceCodec.TryDecode(reference);
        return searchReference is null
            ? null
            : new BrowseReferenceValue(
                TargetForSearchIdentity(searchReference.Identity),
                new PlayableMedia(searchReference.Identity),
                searchReference.CorrelationId);
    }

    private static BrowseTarget? TargetForSearchIdentity(MediaIdentity identity) =>
        identity.Kind switch
        {
            MediaEntityKind.Artist =>
                new BrowseTarget(LmsBrowseQueryKind.ArtistAlbums, identity.Id, 0),
            MediaEntityKind.Album =>
                new BrowseTarget(LmsBrowseQueryKind.AlbumTracks, identity.Id, 0),
            MediaEntityKind.Playlist =>
                new BrowseTarget(LmsBrowseQueryKind.PlaylistTracks, identity.Id, 0),
            MediaEntityKind.Track => null,
            _ => throw new InvalidOperationException(
                $"Unsupported search media kind {identity.Kind}.")
        };

    private static BrowseTarget? NextTarget(LmsBrowseItem item) => item.Kind switch
    {
        BrowseItemKind.AlbumArtist =>
            new BrowseTarget(LmsBrowseQueryKind.AlbumArtistAlbums, item.Id, 0),
        BrowseItemKind.Artist =>
            new BrowseTarget(LmsBrowseQueryKind.ArtistAlbums, item.Id, 0),
        BrowseItemKind.Album =>
            new BrowseTarget(LmsBrowseQueryKind.AlbumTracks, item.Id, 0),
        BrowseItemKind.Genre =>
            new BrowseTarget(LmsBrowseQueryKind.GenreAlbums, item.Id, 0),
        BrowseItemKind.Playlist =>
            new BrowseTarget(LmsBrowseQueryKind.PlaylistTracks, item.Id, 0),
        BrowseItemKind.Year =>
            new BrowseTarget(LmsBrowseQueryKind.YearAlbums, item.Id, 0),
        BrowseItemKind.Track => null,
        _ => throw new InvalidOperationException(
            $"Unsupported LMS browse item kind {item.Kind}.")
    };

    private static PlayableMedia? PlayableMedia(LmsBrowseItem item) => item.Kind switch
    {
        BrowseItemKind.AlbumArtist =>
            new PlayableMedia(
                new MediaIdentity(MediaEntityKind.Artist, item.Id),
                ArtistSelectionScope.AlbumArtist),
        BrowseItemKind.Artist =>
            new PlayableMedia(new MediaIdentity(MediaEntityKind.Artist, item.Id)),
        BrowseItemKind.Album =>
            new PlayableMedia(new MediaIdentity(MediaEntityKind.Album, item.Id)),
        BrowseItemKind.Playlist =>
            new PlayableMedia(new MediaIdentity(MediaEntityKind.Playlist, item.Id)),
        BrowseItemKind.Track =>
            new PlayableMedia(new MediaIdentity(MediaEntityKind.Track, item.Id)),
        BrowseItemKind.Genre or BrowseItemKind.Year => null,
        _ => throw new InvalidOperationException(
            $"Unsupported LMS browse item kind {item.Kind}.")
    };
}
