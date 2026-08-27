using LyrionVoiceMcp.Abstractions;
using LyrionVoiceMcp.Services;

namespace LyrionVoiceMcp.Services.Tests;

public sealed class BrowseServiceTests
{
    [Fact]
    public async Task RootShouldReturnTheEightAgreedLocalLibraryCategories()
    {
        // Arrange
        var lmsClient = new StubLmsBrowseClient(new LmsBrowsePage([], 0));
        var codec = new ReferenceCodecTestContext().Browse;
        var service = new BrowseService(
            lmsClient,
            NullRatingBrowseResolver.Instance,
            codec,
            new ReferenceCodecTestContext().Search,
            PassthroughCatalogueSearchAvailabilityService.Instance);

        // Act
        var outcome = await service.BrowseAsync(
            null,
            TestContext.Current.CancellationToken);

        // Assert
        var result = Assert.IsType<BrowseSucceeded>(outcome);
        Assert.Equal(
            [
                "Album artists",
                "Artists",
                "Albums",
                "Genres",
                "Playlists",
                "Ratings",
                "Recently added",
                "Years"
            ],
            result.Items.Select(item => item.Title));
        Assert.All(result.Items, item =>
        {
            Assert.Equal(BrowseItemKind.Category, item.Kind);
            Assert.True(item.HasBrowseReference);
            Assert.False(item.HasPlayReference);
            Assert.NotNull(codec.TryDecode(item.Reference)?.Target);
        });
        Assert.Null(result.Continuation);
        Assert.Null(lmsClient.Request);
    }

    [Fact]
    public async Task RatingsShouldExposeSixIntegerBuckets()
    {
        var codec = new ReferenceCodecTestContext().Browse;
        var service = new BrowseService(
            new StubLmsBrowseClient(new LmsBrowsePage([], 0)),
            NullRatingBrowseResolver.Instance,
            codec,
            new ReferenceCodecTestContext().Search,
            PassthroughCatalogueSearchAvailabilityService.Instance);
        var root = Assert.IsType<BrowseSucceeded>(await service.BrowseAsync(
            null,
            TestContext.Current.CancellationToken));
        var ratingReference = Assert.Single(
            root.Items,
            item => item.Title == "Ratings").Reference;

        var outcome = await service.BrowseAsync(
            ratingReference,
            TestContext.Current.CancellationToken);

        var result = Assert.IsType<BrowseSucceeded>(outcome);
        Assert.Equal(
            ["0", "1", "2", "3", "4", "5"],
            result.Items.Select(item => item.Title));
        Assert.All(result.Items, item =>
        {
            Assert.Equal(BrowseItemKind.Category, item.Kind);
            Assert.True(item.HasBrowseReference);
            Assert.False(item.HasPlayReference);
            Assert.Equal(
                BrowseTargetKind.RatingTracks,
                codec.TryDecode(item.Reference)?.Target?.Kind);
        });
    }

    [Fact]
    public async Task RatingBucketShouldReturnRatedPlayableTracksAndContinuation()
    {
        var ratingResolver = new StubRatingBrowseResolver(new RatingBrowsePage(
        [
            new RatingBrowseTrack(
                new MediaIdentity(MediaEntityKind.Track, "track-90"),
                "Ninety Point Signal",
                "The Imaginaries",
                "Imaginary Signals",
                90)
        ],
        true));
        var lmsClient = new StubLmsBrowseClient(new LmsBrowsePage([], 0));
        var codec = new ReferenceCodecTestContext().Browse;
        var service = new BrowseService(
            lmsClient,
            ratingResolver,
            codec,
            new ReferenceCodecTestContext().Search,
            PassthroughCatalogueSearchAvailabilityService.Instance);
        var reference = codec.Encode(new BrowseReferenceValue(
            new BrowseTarget(BrowseTargetKind.RatingTracks, "4", 0),
            null));

        var outcome = await service.BrowseAsync(
            reference,
            TestContext.Current.CancellationToken);

        var result = Assert.IsType<BrowseSucceeded>(outcome);
        var item = Assert.Single(result.Items);
        Assert.Equal(90, item.NativeRating);
        Assert.True(item.HasPlayReference);
        Assert.False(item.HasBrowseReference);
        Assert.Equal(
            new PlayableMedia(new MediaIdentity(MediaEntityKind.Track, "track-90")),
            codec.TryDecode(item.Reference)?.Media);
        Assert.Equal(4, ratingResolver.Bucket);
        Assert.Equal(0, ratingResolver.Offset);
        Assert.Equal(50, ratingResolver.Limit);
        Assert.Equal(1, codec.TryDecode(result.Continuation!)?.Target?.Offset);
        Assert.Null(lmsClient.Request);
    }

    [Fact]
    public async Task PreparingIndexShouldReturnTheCurrentAvailabilityMessage()
    {
        var codec = new ReferenceCodecTestContext().Browse;
        var service = new BrowseService(
            new StubLmsBrowseClient(new LmsBrowsePage([], 0)),
            new UnavailableRatingBrowseResolver(),
            codec,
            new ReferenceCodecTestContext().Search,
            new FixedCatalogueSearchAvailabilityService(
                "The search index is being prepared."));
        var reference = codec.Encode(new BrowseReferenceValue(
            new BrowseTarget(BrowseTargetKind.RatingTracks, "4", 0),
            null));

        var outcome = await service.BrowseAsync(
            reference,
            TestContext.Current.CancellationToken);

        var rejected = Assert.IsType<BrowseRejected>(outcome);
        Assert.Equal(BrowseRejectionReason.BrowseUnavailable, rejected.Reason);
        Assert.Equal("The search index is being prepared.", rejected.Message);
    }

    [Fact]
    public async Task AlbumPageShouldReturnDualPurposeReferencesAndContinuation()
    {
        // Arrange
        var lmsClient = new StubLmsBrowseClient(new LmsBrowsePage(
        [
            new LmsBrowseItem(
                BrowseItemKind.Album,
                "201",
                "Lantern Signals",
                "The Paper Comets",
                null),
            new LmsBrowseItem(
                BrowseItemKind.Album,
                "202",
                "Night Routes",
                "The Copper Lines",
                null)
        ],
        3));
        var codec = new ReferenceCodecTestContext().Browse;
        var service = new BrowseService(
            lmsClient,
            NullRatingBrowseResolver.Instance,
            codec,
            new ReferenceCodecTestContext().Search,
            PassthroughCatalogueSearchAvailabilityService.Instance);
        var reference = codec.Encode(new BrowseReferenceValue(
            new BrowseTarget(BrowseTargetKind.Albums, null, 0),
            null));

        // Act
        var outcome = await service.BrowseAsync(
            reference,
            TestContext.Current.CancellationToken);

        // Assert
        var result = Assert.IsType<BrowseSucceeded>(outcome);
        Assert.Equal(2, result.Items.Count);
        var firstReference = codec.TryDecode(result.Items[0].Reference);
        Assert.Equal(
            new BrowseTarget(BrowseTargetKind.AlbumTracks, "201", 0),
            firstReference?.Target);
        Assert.Equal(
            new PlayableMedia(new MediaIdentity(MediaEntityKind.Album, "201")),
            firstReference?.Media);
        Assert.True(result.Items[0].HasBrowseReference);
        Assert.True(result.Items[0].HasPlayReference);
        Assert.Equal(50, lmsClient.Request?.Limit);
        Assert.Equal(2, codec.TryDecode(result.Continuation!)?.Target?.Offset);
    }

    [Fact]
    public async Task AlbumArtistShouldBeBrowsableButNotPlayable()
    {
        // Arrange
        var lmsClient = new StubLmsBrowseClient(new LmsBrowsePage(
        [
            new LmsBrowseItem(
                BrowseItemKind.AlbumArtist,
                "101",
                "The Paper Comets",
                null,
                null)
        ],
        1));
        var codec = new ReferenceCodecTestContext().Browse;
        var service = new BrowseService(
            lmsClient,
            NullRatingBrowseResolver.Instance,
            codec,
            new ReferenceCodecTestContext().Search,
            PassthroughCatalogueSearchAvailabilityService.Instance);
        var reference = codec.Encode(new BrowseReferenceValue(
            new BrowseTarget(BrowseTargetKind.AlbumArtists, null, 0),
            null));

        // Act
        var outcome = await service.BrowseAsync(
            reference,
            TestContext.Current.CancellationToken);

        // Assert
        var item = Assert.Single(Assert.IsType<BrowseSucceeded>(outcome).Items);
        Assert.True(item.HasBrowseReference);
        Assert.False(item.HasPlayReference);
        var decoded = codec.TryDecode(item.Reference);
        Assert.Equal(
            new BrowseTarget(BrowseTargetKind.AlbumArtistAlbums, "101", 0),
            decoded?.Target);
        Assert.Null(decoded?.Media);
    }

    [Fact]
    public async Task TrackPageShouldReturnPlayableNonBrowsableItems()
    {
        // Arrange
        var lmsClient = new StubLmsBrowseClient(new LmsBrowsePage(
        [
            new LmsBrowseItem(
                BrowseItemKind.Track,
                "301",
                "First Light",
                "The Copper Lines",
                "Fictional Frequencies")
        ],
        1));
        var codec = new ReferenceCodecTestContext().Browse;
        var service = new BrowseService(
            lmsClient,
            NullRatingBrowseResolver.Instance,
            codec,
            new ReferenceCodecTestContext().Search,
            PassthroughCatalogueSearchAvailabilityService.Instance);
        var reference = codec.Encode(new BrowseReferenceValue(
            new BrowseTarget(BrowseTargetKind.AlbumTracks, "201", 0),
            new PlayableMedia(new MediaIdentity(MediaEntityKind.Album, "201"))));

        // Act
        var outcome = await service.BrowseAsync(
            reference,
            TestContext.Current.CancellationToken);

        // Assert
        var item = Assert.Single(Assert.IsType<BrowseSucceeded>(outcome).Items);
        Assert.False(item.HasBrowseReference);
        Assert.True(item.HasPlayReference);
        var decoded = codec.TryDecode(item.Reference);
        Assert.Null(decoded?.Target);
        Assert.Equal(
            new PlayableMedia(new MediaIdentity(MediaEntityKind.Track, "301")),
            decoded?.Media);
    }

    [Fact]
    public async Task PlayableOnlyReferenceShouldReturnANotBrowsableError()
    {
        // Arrange
        var lmsClient = new StubLmsBrowseClient(new LmsBrowsePage([], 0));
        var codec = new ReferenceCodecTestContext().Browse;
        var service = new BrowseService(
            lmsClient,
            NullRatingBrowseResolver.Instance,
            codec,
            new ReferenceCodecTestContext().Search,
            PassthroughCatalogueSearchAvailabilityService.Instance);
        var reference = codec.Encode(new BrowseReferenceValue(
            null,
            new PlayableMedia(new MediaIdentity(MediaEntityKind.Track, "301"))));

        // Act
        var outcome = await service.BrowseAsync(
            reference,
            TestContext.Current.CancellationToken);

        // Assert
        var rejection = Assert.IsType<BrowseRejected>(outcome);
        Assert.Equal(BrowseRejectionReason.NotBrowsable, rejection.Reason);
        Assert.Null(lmsClient.Request);
    }

    [Fact]
    public async Task ArtistSearchReferenceShouldBrowseAlbumsAndPropagateCorrelation()
    {
        // Arrange
        var lmsClient = new StubLmsBrowseClient(new LmsBrowsePage(
        [
            new LmsBrowseItem(
                BrowseItemKind.Album,
                "201",
                "Lantern Signals",
                "The Paper Comets",
                null)
        ],
        2));
        var browseCodec = new ReferenceCodecTestContext().Browse;
        var searchCodec = new ReferenceCodecTestContext().Search;
        var service = new BrowseService(
            lmsClient,
            NullRatingBrowseResolver.Instance,
            browseCodec,
            searchCodec,
            PassthroughCatalogueSearchAvailabilityService.Instance);
        var correlationId = "123456781234123412341234567890ab";
        var searchReference = searchCodec.Encode(
            new SearchResultReferenceValue(
                correlationId,
                new MediaIdentity(MediaEntityKind.Artist, "101")));

        // Act
        var outcome = await service.BrowseAsync(
            searchReference,
            TestContext.Current.CancellationToken);

        // Assert
        var result = Assert.IsType<BrowseSucceeded>(outcome);
        Assert.Equal(LmsBrowseQueryKind.ArtistAlbums, lmsClient.Request?.Kind);
        Assert.Equal("101", lmsClient.Request?.FilterId);
        Assert.Equal(
            correlationId,
            browseCodec.TryDecode(Assert.Single(result.Items).Reference)?.SearchCorrelationId);
        Assert.Equal(
            correlationId,
            browseCodec.TryDecode(result.Continuation!)?.SearchCorrelationId);
    }

    [Fact]
    public async Task DiscographyReferenceShouldBrowseOnlyAlbumArtistAlbums()
    {
        // Arrange
        var lmsClient = new StubLmsBrowseClient(new LmsBrowsePage(
        [
            new LmsBrowseItem(
                BrowseItemKind.Album,
                "201",
                "Fictional Frequencies",
                "The Paper Comets",
                null)
        ],
        2));
        var references = new ReferenceCodecTestContext();
        var correlationId = "123456781234123412341234567890ab";
        var reference = references.Browse.Encode(new BrowseReferenceValue(
            new BrowseTarget(BrowseTargetKind.AlbumArtistAlbums, "101", 0),
            null,
            correlationId));
        var service = new BrowseService(
            lmsClient,
            NullRatingBrowseResolver.Instance,
            references.Browse,
            references.Search,
            PassthroughCatalogueSearchAvailabilityService.Instance);

        // Act
        var outcome = await service.BrowseAsync(
            reference,
            TestContext.Current.CancellationToken);

        // Assert
        var result = Assert.IsType<BrowseSucceeded>(outcome);
        Assert.Equal(LmsBrowseQueryKind.AlbumArtistAlbums, lmsClient.Request?.Kind);
        Assert.Equal("101", lmsClient.Request?.FilterId);
        Assert.Equal(
            correlationId,
            references.Browse.TryDecode(Assert.Single(result.Items).Reference)
                ?.SearchCorrelationId);
        Assert.Equal(
            correlationId,
            references.Browse.TryDecode(result.Continuation!)?.SearchCorrelationId);
    }

    [Fact]
    public async Task TrackSearchReferenceShouldReturnANotBrowsableError()
    {
        // Arrange
        var lmsClient = new StubLmsBrowseClient(new LmsBrowsePage([], 0));
        var searchCodec = new ReferenceCodecTestContext().Search;
        var service = new BrowseService(
            lmsClient,
            NullRatingBrowseResolver.Instance,
            new ReferenceCodecTestContext().Browse,
            searchCodec,
            PassthroughCatalogueSearchAvailabilityService.Instance);
        var searchReference = searchCodec.Encode(new SearchResultReferenceValue(
            "123456781234123412341234567890ab",
            new MediaIdentity(MediaEntityKind.Track, "301")));

        // Act
        var outcome = await service.BrowseAsync(
            searchReference,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            BrowseRejectionReason.NotBrowsable,
            Assert.IsType<BrowseRejected>(outcome).Reason);
        Assert.Null(lmsClient.Request);
    }

    [Theory]
    [InlineData(MediaEntityKind.Album, LmsBrowseQueryKind.AlbumTracks)]
    [InlineData(MediaEntityKind.Playlist, LmsBrowseQueryKind.PlaylistTracks)]
    public async Task CollectionSearchReferenceShouldUseItsNaturalBrowseTarget(
        MediaEntityKind mediaKind,
        LmsBrowseQueryKind expectedQueryKind)
    {
        // Arrange
        var lmsClient = new StubLmsBrowseClient(new LmsBrowsePage([], 0));
        var searchCodec = new ReferenceCodecTestContext().Search;
        var service = new BrowseService(
            lmsClient,
            NullRatingBrowseResolver.Instance,
            new ReferenceCodecTestContext().Browse,
            searchCodec,
            PassthroughCatalogueSearchAvailabilityService.Instance);
        var searchReference = searchCodec.Encode(new SearchResultReferenceValue(
            "123456781234123412341234567890ab",
            new MediaIdentity(mediaKind, "201")));

        // Act
        var outcome = await service.BrowseAsync(
            searchReference,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.IsType<BrowseSucceeded>(outcome);
        Assert.Equal(expectedQueryKind, lmsClient.Request?.Kind);
        Assert.Equal("201", lmsClient.Request?.FilterId);
    }

    private sealed class StubLmsBrowseClient(LmsBrowsePage page) : ILmsBrowseClient
    {
        public LmsBrowseRequest? Request { get; private set; }

        public Task<LmsBrowsePage> BrowseAsync(
            LmsBrowseRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Request = request;
            return Task.FromResult(page);
        }
    }

    private sealed class NullRatingBrowseResolver : IRatingBrowseResolver
    {
        public static NullRatingBrowseResolver Instance { get; } = new();

        public Task<RatingBrowsePage> BrowseAsync(
            int bucket,
            int offset,
            int limit,
            CancellationToken cancellationToken) =>
            Task.FromResult(new RatingBrowsePage([], false));
    }

    private sealed class StubRatingBrowseResolver(RatingBrowsePage page)
        : IRatingBrowseResolver
    {
        public int? Bucket { get; private set; }
        public int? Offset { get; private set; }
        public int? Limit { get; private set; }

        public Task<RatingBrowsePage> BrowseAsync(
            int bucket,
            int offset,
            int limit,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Bucket = bucket;
            Offset = offset;
            Limit = limit;
            return Task.FromResult(page);
        }
    }

    private sealed class UnavailableRatingBrowseResolver : IRatingBrowseResolver
    {
        public Task<RatingBrowsePage> BrowseAsync(
            int bucket,
            int offset,
            int limit,
            CancellationToken cancellationToken) =>
            Task.FromException<RatingBrowsePage>(new CatalogueSearchUnavailableException(
                "The production catalogue search index has not been built."));
    }
}

public sealed class BrowseReferenceCodecTests
{
    [Fact]
    public void CodecShouldRoundTripNavigationAndPlaybackWithoutServerOrVersion()
    {
        // Arrange
        var codec = new ReferenceCodecTestContext().Browse;
        var expected = new BrowseReferenceValue(
            new BrowseTarget(BrowseTargetKind.AlbumTracks, "204", 50),
            new PlayableMedia(new MediaIdentity(MediaEntityKind.Album, "204")),
            "123456781234123412341234567890ab");

        // Act
        var reference = codec.Encode(expected);
        var decoded = codec.TryDecode(reference);

        // Assert
        Assert.StartsWith("album_", reference, StringComparison.Ordinal);
        Assert.Matches("^album_[0-9a-f]{16}$", reference);
        Assert.Equal(expected, decoded);
    }

    [Fact]
    public void CodecShouldUseReadableEntityAndLocationPrefixes()
    {
        var codec = new ReferenceCodecTestContext().Browse;
        static BrowseReferenceValue Target(
            BrowseTargetKind kind,
            string? filterId = null,
            int offset = 0) =>
            new(new BrowseTarget(kind, filterId, offset), null);
        static BrowseReferenceValue Media(MediaEntityKind kind) =>
            new(null, new PlayableMedia(new MediaIdentity(kind, "205")));
        var examples = new (BrowseReferenceValue Value, string Prefix)[]
        {
            (Target(BrowseTargetKind.AlbumArtists), "album_artists_"),
            (Target(BrowseTargetKind.Artists), "artists_"),
            (Target(BrowseTargetKind.Albums), "albums_"),
            (Target(BrowseTargetKind.Genres), "genres_"),
            (Target(BrowseTargetKind.Playlists), "playlists_"),
            (Target(BrowseTargetKind.RecentlyAddedAlbums), "recent_albums_"),
            (Target(BrowseTargetKind.Years), "years_"),
            (Target(BrowseTargetKind.RatingBuckets), "ratings_"),
            (Target(BrowseTargetKind.AlbumArtistAlbums, "204"), "album_artist_"),
            (Target(BrowseTargetKind.ArtistAlbums, "204"), "artist_"),
            (Target(BrowseTargetKind.GenreAlbums, "204"), "genre_"),
            (Target(BrowseTargetKind.YearAlbums, "204"), "year_"),
            (Target(BrowseTargetKind.RatingTracks, "4"), "rating_"),
            (Target(BrowseTargetKind.AlbumArtistAlbums, "204", 50), "albums_"),
            (Target(BrowseTargetKind.ArtistAlbums, "204", 50), "albums_"),
            (Target(BrowseTargetKind.GenreAlbums, "204", 50), "albums_"),
            (Target(BrowseTargetKind.YearAlbums, "204", 50), "albums_"),
            (Target(BrowseTargetKind.AlbumTracks, "204", 50), "tracks_"),
            (Target(BrowseTargetKind.PlaylistTracks, "204", 50), "tracks_"),
            (Target(BrowseTargetKind.RatingTracks, "4", 50), "tracks_"),
            (Media(MediaEntityKind.Album), "album_"),
            (Media(MediaEntityKind.Track), "track_"),
            (Media(MediaEntityKind.Playlist), "playlist_")
        };

        foreach (var example in examples)
        {
            var reference = codec.Encode(example.Value);

            Assert.StartsWith(
                example.Prefix,
                reference,
                StringComparison.Ordinal);
            Assert.Matches(
                $"^{example.Prefix}[0-9a-f]{{16}}$",
                reference);
            Assert.Equal(example.Value, codec.TryDecode(reference));
        }
    }

    [Fact]
    public void TryDecodeShouldRejectMissingRequiredRouteFilter()
    {
        // Arrange
        var codec = new ReferenceCodecTestContext().Browse;

        // Act
        var exception = Assert.Throws<ArgumentException>(() => codec.Encode(
            new BrowseReferenceValue(
                new BrowseTarget(BrowseTargetKind.AlbumTracks, null, 0),
                null)));

        // Assert
        Assert.Equal("value", exception.ParamName);
    }

    [Fact]
    public void CodecShouldRejectArtistPlaybackMedia()
    {
        var codec = new ReferenceCodecTestContext().Browse;

        var exception = Assert.Throws<ArgumentException>(() => codec.Encode(
            new BrowseReferenceValue(
                new BrowseTarget(BrowseTargetKind.ArtistAlbums, "204", 0),
                new PlayableMedia(new MediaIdentity(MediaEntityKind.Artist, "204")))));

        Assert.Equal("value", exception.ParamName);
    }

}

public sealed class PlayableReferenceResolverTests
{
    [Fact]
    public void ResolverShouldPreserveRealSearchProvenanceAndNotInventItForPureBrowse()
    {
        // Arrange
        var searchCodec = new ReferenceCodecTestContext().Search;
        var browseCodec = new ReferenceCodecTestContext().Browse;
        var resolver = new PlayableReferenceResolver(searchCodec, browseCodec);
        var identity = new MediaIdentity(MediaEntityKind.Album, "204");
        var correlationId = "123456781234123412341234567890ab";
        var searchReference = searchCodec.Encode(
            new SearchResultReferenceValue(correlationId, identity));
        var browseReference = browseCodec.Encode(
            new BrowseReferenceValue(
                new BrowseTarget(BrowseTargetKind.AlbumTracks, "204", 0),
                new PlayableMedia(identity)));
        var searchDerivedBrowseReference = browseCodec.Encode(
            new BrowseReferenceValue(
                new BrowseTarget(BrowseTargetKind.AlbumTracks, "204", 0),
                new PlayableMedia(identity),
                correlationId));

        // Act
        var searchValue = resolver.Resolve(searchReference);
        var browseValue = resolver.Resolve(browseReference);
        var searchDerivedBrowseValue = resolver.Resolve(searchDerivedBrowseReference);

        // Assert
        Assert.Equal(correlationId, searchValue?.SearchCorrelationId);
        Assert.Equal(new PlayableMedia(identity), searchValue?.Media);
        Assert.Null(browseValue?.SearchCorrelationId);
        Assert.Equal(new PlayableMedia(identity), browseValue?.Media);
        Assert.Equal(correlationId, searchDerivedBrowseValue?.SearchCorrelationId);
        Assert.Equal(new PlayableMedia(identity), searchDerivedBrowseValue?.Media);
    }

    [Fact]
    public void ResolverShouldRejectArtistSearchReferences()
    {
        // Arrange
        var searchCodec = new ReferenceCodecTestContext().Search;
        var resolver = new PlayableReferenceResolver(
            searchCodec,
            new ReferenceCodecTestContext().Browse);
        var artistMedia = new PlayableMedia(
            new MediaIdentity(MediaEntityKind.Artist, "204"));
        var searchReference = searchCodec.Encode(
            new SearchResultReferenceValue(
                "123456781234123412341234567890ab",
                artistMedia.Identity));

        // Act
        var searchResult = resolver.Resolve(searchReference);

        // Assert
        Assert.Null(searchResult);
    }
}
