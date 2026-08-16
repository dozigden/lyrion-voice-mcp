using LyrionVoiceMcp.Abstractions;
using LyrionVoiceMcp.Services;

namespace LyrionVoiceMcp.Services.Tests;

public sealed class BrowseServiceTests
{
    [Fact]
    public async Task RootShouldReturnTheSevenAgreedLocalLibraryCategories()
    {
        // Arrange
        var lmsClient = new StubLmsBrowseClient(new LmsBrowsePage([], 0));
        var codec = new ReferenceCodecTestContext().Browse;
        var service = new BrowseService(
            lmsClient,
            codec,
            new ReferenceCodecTestContext().Search);

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
                "Recently added",
                "Years"
            ],
            result.Items.Select(item => item.Title));
        Assert.All(result.Items, item =>
        {
            Assert.Equal(BrowseItemKind.Category, item.Kind);
            Assert.True(item.Browsable);
            Assert.False(item.Playable);
            Assert.NotNull(codec.TryDecode(item.Reference)?.Target);
        });
        Assert.Null(result.Continuation);
        Assert.Null(lmsClient.Request);
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
            codec,
            new ReferenceCodecTestContext().Search);
        var reference = codec.Encode(new BrowseReferenceValue(
            new BrowseTarget(LmsBrowseQueryKind.Albums, null, 0),
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
            new BrowseTarget(LmsBrowseQueryKind.AlbumTracks, "201", 0),
            firstReference?.Target);
        Assert.Equal(
            new PlayableMedia(new MediaIdentity(MediaEntityKind.Album, "201")),
            firstReference?.Media);
        Assert.True(result.Items[0].Browsable);
        Assert.True(result.Items[0].Playable);
        Assert.Equal(50, lmsClient.Request?.Limit);
        Assert.Equal(2, codec.TryDecode(result.Continuation!)?.Target?.Offset);
    }

    [Fact]
    public async Task AlbumArtistShouldPreserveItsRoleInThePlayableReference()
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
            codec,
            new ReferenceCodecTestContext().Search);
        var reference = codec.Encode(new BrowseReferenceValue(
            new BrowseTarget(LmsBrowseQueryKind.AlbumArtists, null, 0),
            null));

        // Act
        var outcome = await service.BrowseAsync(
            reference,
            TestContext.Current.CancellationToken);

        // Assert
        var item = Assert.Single(Assert.IsType<BrowseSucceeded>(outcome).Items);
        Assert.True(item.Browsable);
        Assert.True(item.Playable);
        var decoded = codec.TryDecode(item.Reference);
        Assert.Equal(
            new BrowseTarget(LmsBrowseQueryKind.AlbumArtistAlbums, "101", 0),
            decoded?.Target);
        Assert.Equal(
            new PlayableMedia(
                new MediaIdentity(MediaEntityKind.Artist, "101"),
                ArtistSelectionScope.AlbumArtist),
            decoded?.Media);
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
            codec,
            new ReferenceCodecTestContext().Search);
        var reference = codec.Encode(new BrowseReferenceValue(
            new BrowseTarget(LmsBrowseQueryKind.AlbumTracks, "201", 0),
            new PlayableMedia(new MediaIdentity(MediaEntityKind.Album, "201"))));

        // Act
        var outcome = await service.BrowseAsync(
            reference,
            TestContext.Current.CancellationToken);

        // Assert
        var item = Assert.Single(Assert.IsType<BrowseSucceeded>(outcome).Items);
        Assert.False(item.Browsable);
        Assert.True(item.Playable);
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
            codec,
            new ReferenceCodecTestContext().Search);
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
        var service = new BrowseService(lmsClient, browseCodec, searchCodec);
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
    public async Task TrackSearchReferenceShouldReturnANotBrowsableError()
    {
        // Arrange
        var lmsClient = new StubLmsBrowseClient(new LmsBrowsePage([], 0));
        var searchCodec = new ReferenceCodecTestContext().Search;
        var service = new BrowseService(
            lmsClient,
            new ReferenceCodecTestContext().Browse,
            searchCodec);
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
            new ReferenceCodecTestContext().Browse,
            searchCodec);
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
}

public sealed class BrowseReferenceCodecTests
{
    [Fact]
    public void CodecShouldRoundTripNavigationAndPlaybackWithoutServerOrVersion()
    {
        // Arrange
        var codec = new ReferenceCodecTestContext().Browse;
        var expected = new BrowseReferenceValue(
            new BrowseTarget(LmsBrowseQueryKind.AlbumTracks, "204", 50),
            new PlayableMedia(new MediaIdentity(MediaEntityKind.Album, "204")),
            "123456781234123412341234567890ab");

        // Act
        var reference = codec.Encode(expected);
        var decoded = codec.TryDecode(reference);

        // Assert
        Assert.StartsWith("browse_", reference, StringComparison.Ordinal);
        Assert.Equal(23, reference.Length);
        Assert.Equal(expected, decoded);
    }

    [Fact]
    public void TryDecodeShouldRejectMissingRequiredRouteFilter()
    {
        // Arrange
        var codec = new ReferenceCodecTestContext().Browse;

        // Act
        var exception = Assert.Throws<ArgumentException>(() => codec.Encode(
            new BrowseReferenceValue(
                new BrowseTarget(LmsBrowseQueryKind.AlbumTracks, null, 0),
                null)));

        // Assert
        Assert.Equal("value", exception.ParamName);
    }

    [Fact]
    public void CodecShouldRoundTripAnAlbumArtistPlaybackConstraint()
    {
        // Arrange
        var codec = new ReferenceCodecTestContext().Browse;
        var expected = new BrowseReferenceValue(
            new BrowseTarget(LmsBrowseQueryKind.AlbumArtistAlbums, "204", 0),
            new PlayableMedia(
                new MediaIdentity(MediaEntityKind.Artist, "204"),
                ArtistSelectionScope.AlbumArtist));

        // Act
        var decoded = codec.TryDecode(codec.Encode(expected));

        // Assert
        Assert.Equal(expected, decoded);
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
                new BrowseTarget(LmsBrowseQueryKind.AlbumTracks, "204", 0),
                new PlayableMedia(identity)));
        var searchDerivedBrowseReference = browseCodec.Encode(
            new BrowseReferenceValue(
                new BrowseTarget(LmsBrowseQueryKind.AlbumTracks, "204", 0),
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
    public void ResolverShouldPreserveAnAlbumArtistSelectionConstraint()
    {
        // Arrange
        var browseCodec = new ReferenceCodecTestContext().Browse;
        var resolver = new PlayableReferenceResolver(
            new ReferenceCodecTestContext().Search,
            browseCodec);
        var expected = new PlayableMedia(
            new MediaIdentity(MediaEntityKind.Artist, "204"),
            ArtistSelectionScope.AlbumArtist);
        var reference = browseCodec.Encode(new BrowseReferenceValue(
            new BrowseTarget(LmsBrowseQueryKind.AlbumArtistAlbums, "204", 0),
            expected));

        // Act
        var resolved = resolver.Resolve(reference);

        // Assert
        Assert.Equal(expected, resolved?.Media);
    }
}
