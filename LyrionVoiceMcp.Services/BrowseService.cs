using LyrionVoiceMcp.Abstractions;

namespace LyrionVoiceMcp.Services;

public sealed class BrowseService(
    ILmsBrowseClient lmsBrowseClient,
    IRatingBrowseResolver ratingBrowseResolver,
    IBrowseReferenceCodec browseReferenceCodec,
    ISearchResultReferenceCodec searchReferenceCodec,
    ICatalogueSearchAvailabilityService searchAvailability) : IBrowseService
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
        if (target.Kind == BrowseTargetKind.RatingBuckets)
        {
            return new BrowseSucceeded(RatingBucketItems(), null);
        }

        if (target.Kind == BrowseTargetKind.RatingTracks)
        {
            return await BrowseRatingTracksAsync(target, cancellationToken);
        }

        var page = await lmsBrowseClient.BrowseAsync(
            new LmsBrowseRequest(
                ToLmsQueryKind(target.Kind),
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
        RootItem("Album artists", BrowseTargetKind.AlbumArtists),
        RootItem("Artists", BrowseTargetKind.Artists),
        RootItem("Albums", BrowseTargetKind.Albums),
        RootItem("Genres", BrowseTargetKind.Genres),
        RootItem("Playlists", BrowseTargetKind.Playlists),
        RootItem("Ratings", BrowseTargetKind.RatingBuckets),
        RootItem("Recently added", BrowseTargetKind.RecentlyAddedAlbums),
        RootItem("Years", BrowseTargetKind.Years)
    ];

    private BrowseItemResult RootItem(string title, BrowseTargetKind kind) =>
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

    private IReadOnlyList<BrowseItemResult> RatingBucketItems() =>
        Enumerable.Range(0, 6)
            .Select(bucket => new BrowseItemResult(
                browseReferenceCodec.Encode(new BrowseReferenceValue(
                    new BrowseTarget(
                        BrowseTargetKind.RatingTracks,
                        bucket.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        0),
                    null)),
                BrowseItemKind.Category,
                bucket.ToString(System.Globalization.CultureInfo.InvariantCulture),
                null,
                null,
                true,
                false))
            .ToArray();

    private async Task<BrowseOutcome> BrowseRatingTracksAsync(
        BrowseTarget target,
        CancellationToken cancellationToken)
    {
        if (!int.TryParse(
            target.FilterId,
            System.Globalization.NumberStyles.None,
            System.Globalization.CultureInfo.InvariantCulture,
            out var bucket)
            || bucket is < 0 or > 5)
        {
            return new BrowseRejected(
                BrowseRejectionReason.InvalidReference,
                "The rating browse reference is invalid.");
        }

        RatingBrowsePage page;
        try
        {
            page = await ratingBrowseResolver.BrowseAsync(
                bucket,
                target.Offset,
                PageSize,
                cancellationToken);
        }
        catch (CatalogueSearchUnavailableException exception)
        {
            return new BrowseRejected(
                BrowseRejectionReason.BrowseUnavailable,
                await searchAvailability.DescribeUnavailableAsync(
                    exception.Message,
                    cancellationToken));
        }

        var items = page.Items.Select(item => new BrowseItemResult(
            browseReferenceCodec.Encode(new BrowseReferenceValue(
                null,
                new PlayableMedia(item.Identity))),
            BrowseItemKind.Track,
            item.Title,
            item.Artist,
            item.Album,
            false,
            true,
            item.NativeRating)).ToArray();
        var continuation = page.HasMore
            ? browseReferenceCodec.Encode(new BrowseReferenceValue(
                target with { Offset = target.Offset + items.Length },
                null))
            : null;
        return new BrowseSucceeded(items, continuation);
    }

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
                PlayableMedia(searchReference.Identity),
                searchReference.CorrelationId);
    }

    private static BrowseTarget? TargetForSearchIdentity(MediaIdentity identity) =>
        identity.Kind switch
        {
            MediaEntityKind.Artist =>
                new BrowseTarget(BrowseTargetKind.ArtistAlbums, identity.Id, 0),
            MediaEntityKind.Album =>
                new BrowseTarget(BrowseTargetKind.AlbumTracks, identity.Id, 0),
            MediaEntityKind.Playlist =>
                new BrowseTarget(BrowseTargetKind.PlaylistTracks, identity.Id, 0),
            MediaEntityKind.Track => null,
            _ => throw new InvalidOperationException(
                $"Unsupported search media kind {identity.Kind}.")
        };

    private static BrowseTarget? NextTarget(LmsBrowseItem item) => item.Kind switch
    {
        BrowseItemKind.AlbumArtist =>
            new BrowseTarget(BrowseTargetKind.AlbumArtistAlbums, item.Id, 0),
        BrowseItemKind.Artist =>
            new BrowseTarget(BrowseTargetKind.ArtistAlbums, item.Id, 0),
        BrowseItemKind.Album =>
            new BrowseTarget(BrowseTargetKind.AlbumTracks, item.Id, 0),
        BrowseItemKind.Genre =>
            new BrowseTarget(BrowseTargetKind.GenreAlbums, item.Id, 0),
        BrowseItemKind.Playlist =>
            new BrowseTarget(BrowseTargetKind.PlaylistTracks, item.Id, 0),
        BrowseItemKind.Year =>
            new BrowseTarget(BrowseTargetKind.YearAlbums, item.Id, 0),
        BrowseItemKind.Track => null,
        _ => throw new InvalidOperationException(
            $"Unsupported LMS browse item kind {item.Kind}.")
    };

    private static PlayableMedia? PlayableMedia(LmsBrowseItem item) => item.Kind switch
    {
        BrowseItemKind.AlbumArtist or BrowseItemKind.Artist => null,
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

    private static PlayableMedia? PlayableMedia(MediaIdentity identity) =>
        identity.Kind == MediaEntityKind.Artist
            ? null
            : new PlayableMedia(identity);

    private static LmsBrowseQueryKind ToLmsQueryKind(BrowseTargetKind kind) => kind switch
    {
        BrowseTargetKind.AlbumArtists => LmsBrowseQueryKind.AlbumArtists,
        BrowseTargetKind.Artists => LmsBrowseQueryKind.Artists,
        BrowseTargetKind.Albums => LmsBrowseQueryKind.Albums,
        BrowseTargetKind.Genres => LmsBrowseQueryKind.Genres,
        BrowseTargetKind.Playlists => LmsBrowseQueryKind.Playlists,
        BrowseTargetKind.RecentlyAddedAlbums => LmsBrowseQueryKind.RecentlyAddedAlbums,
        BrowseTargetKind.Years => LmsBrowseQueryKind.Years,
        BrowseTargetKind.AlbumArtistAlbums => LmsBrowseQueryKind.AlbumArtistAlbums,
        BrowseTargetKind.ArtistAlbums => LmsBrowseQueryKind.ArtistAlbums,
        BrowseTargetKind.GenreAlbums => LmsBrowseQueryKind.GenreAlbums,
        BrowseTargetKind.YearAlbums => LmsBrowseQueryKind.YearAlbums,
        BrowseTargetKind.AlbumTracks => LmsBrowseQueryKind.AlbumTracks,
        BrowseTargetKind.PlaylistTracks => LmsBrowseQueryKind.PlaylistTracks,
        _ => throw new InvalidOperationException($"Unsupported LMS browse target {kind}.")
    };
}
