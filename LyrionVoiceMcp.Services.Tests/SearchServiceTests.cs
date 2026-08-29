using System.Runtime.CompilerServices;
using LyrionVoiceMcp.Abstractions;
using LyrionVoiceMcp.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace LyrionVoiceMcp.Services.Tests;

public sealed class SearchServiceTests
{
    [Fact]
    public async Task SearchShouldReturnCatalogueCandidatesBeforePlaylists()
    {
        var catalogue = new StubCatalogueSearch([
            new CatalogueSearchCandidate(
                new MediaIdentity(MediaEntityKind.Artist, "7"),
                "Copper Lines",
                null,
                null,
                1_040)
        ]);
        var playlists = new StubPlaylistSearch([
            new LmsSearchCandidate(
                new MediaIdentity(MediaEntityKind.Playlist, "9"),
                "Copper Evenings",
                null,
                null)
        ]);
        var codec = new ReferenceCodecTestContext().Search;
        var observations = new RecordingSearchObservationStore();
        var service = CreateService(catalogue, playlists, codec, observations);

        var outcome = await service.SearchAsync(
            "  copper  ",
            TestContext.Current.CancellationToken);

        var results = Assert.IsType<SearchSucceeded>(outcome).Results;
        Assert.Equal("copper", catalogue.Query);
        Assert.Equal("copper", playlists.Query);
        Assert.Collection(
            results,
            item => Assert.Equal(MediaEntityKind.Artist, item.Kind),
            item => Assert.Equal(MediaEntityKind.Playlist, item.Kind));
        var decoded = codec.TryDecode(results[0].Reference);
        Assert.Equal("7", decoded?.Identity.Id);
        Assert.Equal(
            observations.Recorded?.Candidates[0].CorrelationId,
            decoded?.CorrelationId);
    }

    [Fact]
    public async Task SearchShouldCarryNativeRatingsOnlyFromCatalogueTracks()
    {
        var service = CreateService(
            new StubCatalogueSearch([
                new CatalogueSearchCandidate(
                    new MediaIdentity(MediaEntityKind.Track, "track-7"),
                    "Rated Copper Signal",
                    "The Imaginaries",
                    "Imaginary Signals",
                    1_040,
                    90)
            ]),
            new StubPlaylistSearch([
                new LmsSearchCandidate(
                    new MediaIdentity(MediaEntityKind.Playlist, "playlist-9"),
                    "Copper Evenings",
                    null,
                    null)
            ]),
            new ReferenceCodecTestContext().Search,
            new RecordingSearchObservationStore());

        var outcome = await service.SearchAsync(
            "copper",
            TestContext.Current.CancellationToken);

        var succeeded = Assert.IsType<SearchSucceeded>(outcome);
        Assert.Equal(90, Assert.Single(succeeded.TopTracks).NativeRating);
        Assert.Equal(0, Assert.Single(succeeded.Results).NativeRating);
    }

    [Fact]
    public async Task TopTracksShouldNotPartitionHighRatingsOutOfOrdinaryTracks()
    {
        var candidates = Enumerable.Range(1, 10)
            .Select(index => new CatalogueSearchCandidate(
                new MediaIdentity(MediaEntityKind.Track, $"track-{index}"),
                $"Signal {index}",
                "The Imaginaries",
                $"Album {index}",
                1_120,
                index <= 6 ? 100 : 0))
            .ToArray();
        var service = CreateService(
            new StubCatalogueSearch(candidates),
            new StubPlaylistSearch([]),
            new ReferenceCodecTestContext().Search,
            new RecordingSearchObservationStore());

        var succeeded = Assert.IsType<SearchSucceeded>(await service.SearchAsync(
            "signal",
            TestContext.Current.CancellationToken));

        Assert.Equal(SearchResultPolicy.TopTrackLimit, succeeded.TopTracks.Count);
        Assert.All(succeeded.TopTracks, track => Assert.Equal(100, track.NativeRating));
        var tracks = succeeded.Results
            .Where(candidate => candidate.Kind == MediaEntityKind.Track)
            .ToArray();
        Assert.Contains(tracks, track => track.NativeRating == 100);
        Assert.Empty(tracks.Select(track => track.Title).Intersect(
            succeeded.TopTracks.Select(track => track.Title)));
    }

    [Fact]
    public async Task SearchShouldApplyIndependentTuneableLimitsByMediaKind()
    {
        var catalogueCandidates = Enumerable.Range(1, 6)
            .Select(index => new CatalogueSearchCandidate(
                new MediaIdentity(MediaEntityKind.Artist, $"artist-{index}"),
                $"Signal Artist {index}",
                null,
                null,
                1_000 - index))
            .Concat(Enumerable.Range(1, 6).Select(index => new CatalogueSearchCandidate(
                new MediaIdentity(MediaEntityKind.Album, $"album-{index}"),
                $"Signal Album {index}",
                "The Imaginaries",
                null,
                900 - index)))
            .Concat(Enumerable.Range(1, 31).Select(index => new CatalogueSearchCandidate(
                new MediaIdentity(MediaEntityKind.Track, $"track-{index}"),
                $"Signal Track {index}",
                "The Imaginaries",
                "Imaginary Signals",
                800 - index)))
            .ToArray();
        var playlistCandidates = Enumerable.Range(1, 6)
            .Select(index => new LmsSearchCandidate(
                new MediaIdentity(MediaEntityKind.Playlist, $"playlist-{index}"),
                $"Signal Playlist {index}",
                null,
                null))
            .ToArray();
        var service = CreateService(
            new StubCatalogueSearch(catalogueCandidates),
            new StubPlaylistSearch(playlistCandidates),
            new ReferenceCodecTestContext().Search,
            new RecordingSearchObservationStore());

        var outcome = await service.SearchAsync(
            "signal",
            TestContext.Current.CancellationToken);

        var results = Assert.IsType<SearchSucceeded>(outcome).Results;
        Assert.Equal(SearchResultPolicy.ArtistLimit, results.Count(item =>
            item.Kind == MediaEntityKind.Artist));
        Assert.Equal(SearchResultPolicy.AlbumLimit, results.Count(item =>
            item.Kind == MediaEntityKind.Album));
        Assert.Equal(SearchResultPolicy.TrackLimit, results.Count(item =>
            item.Kind == MediaEntityKind.Track));
        Assert.Equal(SearchResultPolicy.PlaylistLimit, results.Count(item =>
            item.Kind == MediaEntityKind.Playlist));
        Assert.Equal(
            [
                MediaEntityKind.Artist,
                MediaEntityKind.Album,
                MediaEntityKind.Track,
                MediaEntityKind.Playlist
            ],
            results.Select(item => item.Kind).Distinct());
    }

    [Fact]
    public async Task ExactArtistShouldExpandThroughCanonicalTracksBeyondSearchLanes()
    {
        var catalogue = new StubCatalogueSearch([
            new CatalogueSearchCandidate(
                new MediaIdentity(MediaEntityKind.Artist, "artist-1"),
                "The Imaginaries",
                null,
                null,
                1_300,
                IsExactTitleMatch: true)
        ]);
        var artistTracks = new StubArtistTrackResolver(
            Enumerable.Range(1, 250)
                .Select(index => new CatalogueSearchCandidate(
                    new MediaIdentity(MediaEntityKind.Track, $"track-{index}"),
                    $"Track {index}",
                    "The Imaginaries",
                    $"Album {(index - 1) / 10}",
                    1_120,
                    index <= 8 ? 100 : 0))
                .ToArray());
        var observations = new RecordingSearchObservationStore();
        var references = new ReferenceCodecTestContext();
        var service = CreateService(
            catalogue,
            new StubPlaylistSearch([]),
            references.Search,
            observations,
            artistTracks,
            references.Browse);

        var succeeded = Assert.IsType<SearchSucceeded>(await service.SearchAsync(
            "The Imaginaries",
            TestContext.Current.CancellationToken));

        Assert.Equal("artist-1", artistTracks.ArtistId);
        Assert.Equal("The Imaginaries", succeeded.ExactArtistMatch?.Name);
        Assert.DoesNotContain(succeeded.Results, candidate =>
            candidate.Kind == MediaEntityKind.Artist);
        var discography = references.Browse.TryDecode(
            succeeded.ExactArtistMatch!.DiscographyReference);
        Assert.Equal(
            new BrowseTarget(BrowseTargetKind.AlbumArtistAlbums, "artist-1", 0),
            discography?.Target);
        Assert.Null(discography?.Media);
        Assert.Equal(
            observations.Recorded?.Candidates[0].CorrelationId,
            discography?.SearchCorrelationId);
        Assert.Equal(
            "The Imaginaries",
            observations.Recorded?.Candidates[0].Title);
        Assert.True(observations.Recorded?.Candidates[0].IsExactArtistMatch);
        Assert.Equal(SearchResultPolicy.TopTrackLimit, succeeded.TopTracks.Count);
        Assert.All(succeeded.TopTracks, track => Assert.True(track.NativeRating >= 80));
        var tracks = succeeded.Results
            .Where(candidate => candidate.Kind == MediaEntityKind.Track)
            .ToArray();
        Assert.Equal(SearchResultPolicy.TrackLimit, tracks.Length);
        Assert.Empty(tracks.Select(track => track.Title).Intersect(
            succeeded.TopTracks.Select(track => track.Title)));
        Assert.Contains(
            tracks,
            track => int.Parse(track.Title["Track ".Length..]) > 80);
        Assert.Equal(
            1 + SearchResultPolicy.TopTrackLimit + SearchResultPolicy.TrackLimit,
            observations.Recorded?.Candidates.Count);
        Assert.Collection(
            observations.Recorded!.Requests,
            request => Assert.Equal("search:unconstrained", request.Command),
            request => Assert.Equal("search:top-rating", request.Command),
            request =>
            {
                Assert.Equal("catalogue-artist-tracks", request.Source);
                Assert.Equal("artist-tracks", request.Command);
                Assert.Equal(250, request.ResultCount);
            },
            request =>
            {
                Assert.Equal("catalogue-artist-albums", request.Source);
                Assert.Equal("artist-albums", request.Command);
                Assert.Equal(0, request.ResultCount);
            });
    }

    [Fact]
    public async Task ExactArtistShouldPinSelfTitledAlbumAndVaryRemainingPreview()
    {
        var catalogue = new StubCatalogueSearch([
            new CatalogueSearchCandidate(
                new MediaIdentity(MediaEntityKind.Artist, "artist-1"),
                "The Imaginaries",
                null,
                null,
                1_300,
                IsExactTitleMatch: true),
            new CatalogueSearchCandidate(
                new MediaIdentity(MediaEntityKind.Album, "self-titled"),
                "The Imaginaries",
                "The Imaginaries",
                null,
                1_300,
                IsExactTitleMatch: true,
                CanonicalAlbumArtistId: "artist-1")
        ]);
        var artistAlbums = new StubArtistAlbumResolver(
            Enumerable.Range(1, 11)
                .Select(index => Album($"album-{index}", $"Album {index}"))
                .Prepend(Album("self-titled", "The Imaginaries"))
                .ToArray());
        var observations = new RecordingSearchObservationStore();
        var references = new ReferenceCodecTestContext();
        var service = CreateService(
            catalogue,
            new StubPlaylistSearch([]),
            references.Search,
            observations,
            browseCodec: references.Browse,
            artistAlbums: artistAlbums);

        var first = Assert.IsType<SearchSucceeded>(await service.SearchAsync(
            "The Imaginaries",
            TestContext.Current.CancellationToken));
        var firstAlbums = first.Results
            .Where(candidate => candidate.Kind == MediaEntityKind.Album)
            .ToArray();
        var firstObservation = Assert.IsType<SearchObservation>(observations.Recorded);
        var second = Assert.IsType<SearchSucceeded>(await service.SearchAsync(
            "The Imaginaries",
            TestContext.Current.CancellationToken));
        var secondAlbums = second.Results
            .Where(candidate => candidate.Kind == MediaEntityKind.Album)
            .ToArray();

        Assert.Equal("artist-1", artistAlbums.ArtistId);
        Assert.Equal(SearchResultPolicy.AlbumLimit, firstAlbums.Length);
        Assert.Equal(SearchResultPolicy.AlbumLimit, secondAlbums.Length);
        Assert.Contains(firstAlbums, album => album.Title == "The Imaginaries");
        Assert.Contains(secondAlbums, album => album.Title == "The Imaginaries");
        Assert.False(firstAlbums
            .Where(album => album.Title != "The Imaginaries")
            .Select(album => album.Title)
            .ToHashSet(StringComparer.Ordinal)
            .SetEquals(secondAlbums
                .Where(album => album.Title != "The Imaginaries")
                .Select(album => album.Title)));
        var observedAlbumRequest = Assert.Single(
            firstObservation.Requests,
            request => request.Source == "catalogue-artist-albums");
        Assert.Equal(12, observedAlbumRequest.ResultCount);
        var observedAlbum = Assert.Single(
            firstObservation.Candidates,
            candidate => candidate.Identity.Kind == MediaEntityKind.Album
                && candidate.Title == "The Imaginaries");
        var selfTitled = Assert.Single(firstAlbums, album =>
            album.Title == "The Imaginaries");
        var reference = references.Search.TryDecode(selfTitled.Reference);
        Assert.Equal("self-titled", reference?.Identity.Id);
        Assert.Equal(observedAlbum.CorrelationId, reference?.CorrelationId);

        static CatalogueSearchCandidate Album(string id, string title) => new(
            new MediaIdentity(MediaEntityKind.Album, id),
            title,
            "The Imaginaries",
            null,
            1_300);
    }

    [Fact]
    public async Task ExactArtistShouldReturnEveryAlbumWhenTheDiscographyHasAtMostFive()
    {
        var artistAlbums = new StubArtistAlbumResolver([
            Album("album-1", "First Fiction"),
            Album("album-2", "Second Fiction"),
            Album("album-3", "Third Fiction")
        ]);
        var service = CreateService(
            new StubCatalogueSearch([
                new CatalogueSearchCandidate(
                    new MediaIdentity(MediaEntityKind.Artist, "artist-1"),
                    "The Imaginaries",
                    null,
                    null,
                    1_300,
                    IsExactTitleMatch: true)
            ]),
            new StubPlaylistSearch([]),
            new ReferenceCodecTestContext().Search,
            new RecordingSearchObservationStore(),
            artistAlbums: artistAlbums);

        var succeeded = Assert.IsType<SearchSucceeded>(await service.SearchAsync(
            "The Imaginaries",
            TestContext.Current.CancellationToken));

        Assert.Equal(
            ["First Fiction", "Second Fiction", "Third Fiction"],
            succeeded.Results
                .Where(candidate => candidate.Kind == MediaEntityKind.Album)
                .Select(candidate => candidate.Title)
                .Order());

        static CatalogueSearchCandidate Album(string id, string title) => new(
            new MediaIdentity(MediaEntityKind.Album, id),
            title,
            "The Imaginaries",
            null,
            1_300);
    }

    [Fact]
    public async Task AlignedSelfTitledAlbumShouldUseExactArtistDiscography()
    {
        var artistTracks = new StubArtistTrackResolver([]);
        var artistAlbums = new StubArtistAlbumResolver([
            Album("self-titled", "The Imaginaries"),
            Album("second", "Imaginary Signals")
        ]);
        var observations = new RecordingSearchObservationStore();
        var service = CreateService(
            new StubCatalogueSearch([
                new CatalogueSearchCandidate(
                    new MediaIdentity(MediaEntityKind.Artist, "artist-1"),
                    "The Imaginaries",
                    null,
                    null,
                    1_300,
                    IsExactTitleMatch: true),
                new CatalogueSearchCandidate(
                    new MediaIdentity(MediaEntityKind.Album, "self-titled"),
                    "The Imaginaries",
                    "The Imaginaries",
                    null,
                    1_300,
                    IsExactTitleMatch: true,
                    CanonicalAlbumArtistId: "artist-1")
            ]),
            new StubPlaylistSearch([]),
            new ReferenceCodecTestContext().Search,
            observations,
            artistTracks,
            artistAlbums: artistAlbums);

        var succeeded = Assert.IsType<SearchSucceeded>(await service.SearchAsync(
            "The Imaginaries",
            TestContext.Current.CancellationToken));

        Assert.Equal("artist-1", artistTracks.ArtistId);
        Assert.Equal("artist-1", artistAlbums.ArtistId);
        Assert.Equal("The Imaginaries", succeeded.ExactArtistMatch?.Name);
        Assert.DoesNotContain(succeeded.Results, candidate =>
            candidate.Kind == MediaEntityKind.Artist);
        Assert.Equal(
            ["Imaginary Signals", "The Imaginaries"],
            succeeded.Results
                .Where(candidate => candidate.Kind == MediaEntityKind.Album)
                .Select(candidate => candidate.Title)
                .Order());
        Assert.True(observations.Recorded?.Candidates[0].IsExactArtistMatch);

        static CatalogueSearchCandidate Album(string id, string title) => new(
            new MediaIdentity(MediaEntityKind.Album, id),
            title,
            "The Imaginaries",
            null,
            1_300);
    }

    [Fact]
    public async Task YearFilterShouldApplyAfterAlignedSelfTitledIdentityResolution()
    {
        var artistAlbums = new StubArtistAlbumResolver([
            new CatalogueSearchCandidate(
                new MediaIdentity(MediaEntityKind.Album, "in-range"),
                "Imaginary Signals",
                "The Imaginaries",
                null,
                1_300)
        ]);
        var service = CreateService(
            new StubCatalogueSearch([
                ExactArtist(),
                AlignedSelfTitledAlbum()
            ]),
            new StubPlaylistSearch([]),
            new ReferenceCodecTestContext().Search,
            new RecordingSearchObservationStore(),
            artistAlbums: artistAlbums);

        var succeeded = Assert.IsType<SearchSucceeded>(await service.SearchAsync(
            new SearchCriteria("The Imaginaries", FromYear: 2000, ToYear: 2009),
            TestContext.Current.CancellationToken));

        Assert.Equal("The Imaginaries", succeeded.ExactArtistMatch?.Name);
        Assert.Equal(
            "Imaginary Signals",
            Assert.Single(succeeded.Results, candidate =>
                candidate.Kind == MediaEntityKind.Album).Title);
        Assert.DoesNotContain(succeeded.Results, candidate =>
            candidate.Title == "The Imaginaries");
        Assert.Equal(
            new CatalogueAlbumSearchConstraint(2000, 2009),
            artistAlbums.Constraint);

        static CatalogueSearchCandidate ExactArtist() => new(
            new MediaIdentity(MediaEntityKind.Artist, "artist-1"),
            "The Imaginaries",
            null,
            null,
            1_300,
            IsExactTitleMatch: true);

        static CatalogueSearchCandidate AlignedSelfTitledAlbum() => new(
            new MediaIdentity(MediaEntityKind.Album, "self-titled"),
            "The Imaginaries",
            "The Imaginaries",
            null,
            1_300,
            IsExactTitleMatch: true,
            CanonicalAlbumArtistId: "artist-1");
    }

    [Fact]
    public async Task TrackFiltersShouldRetainAlignedIdentityWhenNoTracksQualify()
    {
        var artistTracks = new StubArtistTrackResolver([
            new CatalogueSearchCandidate(
                new MediaIdentity(MediaEntityKind.Track, "below-rating"),
                "Fictional Signal",
                "The Imaginaries",
                "The Imaginaries",
                1_120,
                80)
        ]);
        var artistAlbums = new StubArtistAlbumResolver([]);
        var service = CreateService(
            new StubCatalogueSearch([
                new CatalogueSearchCandidate(
                    new MediaIdentity(MediaEntityKind.Artist, "artist-1"),
                    "The Imaginaries",
                    null,
                    null,
                    1_300,
                    IsExactTitleMatch: true),
                new CatalogueSearchCandidate(
                    new MediaIdentity(MediaEntityKind.Album, "self-titled"),
                    "The Imaginaries",
                    "The Imaginaries",
                    null,
                    1_300,
                    IsExactTitleMatch: true,
                    CanonicalAlbumArtistId: "artist-1")
            ]),
            new StubPlaylistSearch([]),
            new ReferenceCodecTestContext().Search,
            new RecordingSearchObservationStore(),
            artistTracks,
            artistAlbums: artistAlbums);
        var rating = new RatingSearchConstraint(4.5m, RatingMatchMode.AtLeast);

        var succeeded = Assert.IsType<SearchSucceeded>(await service.SearchAsync(
            new SearchCriteria(
                "The Imaginaries",
                rating,
                "Ambient",
                2000,
                2009),
            TestContext.Current.CancellationToken));

        Assert.Equal("The Imaginaries", succeeded.ExactArtistMatch?.Name);
        Assert.Empty(succeeded.Results);
        Assert.Empty(succeeded.TopTracks);
        Assert.Equal(
            new CatalogueTrackSearchConstraint(rating, "AMBIENT", 2000, 2009),
            artistTracks.Constraint);
        Assert.Null(artistAlbums.ArtistId);
    }

    [Fact]
    public async Task TrackFiltersShouldNotHideAnUnalignedExactAlbumConflict()
    {
        var artistTracks = new StubArtistTrackResolver([]);
        var service = CreateService(
            new StubCatalogueSearch([
                new CatalogueSearchCandidate(
                    new MediaIdentity(MediaEntityKind.Artist, "artist-1"),
                    "The Imaginaries",
                    null,
                    null,
                    1_300,
                    IsExactTitleMatch: true),
                new CatalogueSearchCandidate(
                    new MediaIdentity(MediaEntityKind.Album, "conflict"),
                    "The Imaginaries",
                    "Another Artist",
                    null,
                    1_300,
                    IsExactTitleMatch: true,
                    CanonicalAlbumArtistId: "artist-2"),
                new CatalogueSearchCandidate(
                    new MediaIdentity(MediaEntityKind.Track, "rated-track"),
                    "Imaginary Signal",
                    "The Imaginaries",
                    "Imaginary Signals",
                    1_120,
                    100)
            ]),
            new StubPlaylistSearch([]),
            new ReferenceCodecTestContext().Search,
            new RecordingSearchObservationStore(),
            artistTracks);

        var succeeded = Assert.IsType<SearchSucceeded>(await service.SearchAsync(
            new SearchCriteria(
                "The Imaginaries",
                new RatingSearchConstraint(5, RatingMatchMode.Exact)),
            TestContext.Current.CancellationToken));

        Assert.Null(succeeded.ExactArtistMatch);
        Assert.Null(artistTracks.ArtistId);
        Assert.DoesNotContain(succeeded.Results, candidate =>
            candidate.Kind == MediaEntityKind.Album);
        Assert.Contains(succeeded.TopTracks, candidate =>
            candidate.Title == "Imaginary Signal");
    }

    [Theory]
    [InlineData(MediaEntityKind.Album)]
    [InlineData(MediaEntityKind.Track)]
    public async Task ExactMediaAmbiguityShouldKeepOrdinarySearchResults(
        MediaEntityKind conflictingKind)
    {
        var artistTracks = new StubArtistTrackResolver([]);
        var artistAlbums = new StubArtistAlbumResolver([]);
        var service = CreateService(
            new StubCatalogueSearch([
                new CatalogueSearchCandidate(
                    new MediaIdentity(MediaEntityKind.Artist, "artist-1"),
                    "Signals",
                    null,
                    null,
                    1_300,
                    IsExactTitleMatch: true),
                new CatalogueSearchCandidate(
                    new MediaIdentity(conflictingKind, "conflict-1"),
                    "Signals",
                    "Another Artist",
                    "Signal Album",
                    1_300,
                    IsExactTitleMatch: true)
            ]),
            new StubPlaylistSearch([]),
            new ReferenceCodecTestContext().Search,
            new RecordingSearchObservationStore(),
            artistTracks,
            artistAlbums: artistAlbums);

        var succeeded = Assert.IsType<SearchSucceeded>(await service.SearchAsync(
            "Signals",
            TestContext.Current.CancellationToken));

        Assert.Null(artistTracks.ArtistId);
        Assert.Null(artistAlbums.ArtistId);
        Assert.Null(succeeded.ExactArtistMatch);
        Assert.Contains(
            succeeded.Results,
            candidate => candidate.Kind == conflictingKind
                && candidate.Title == "Signals");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("artist-2")]
    public async Task UnalignedSelfTitledAlbumShouldKeepOrdinarySearchResults(
        string? canonicalAlbumArtistId)
    {
        var artistTracks = new StubArtistTrackResolver([]);
        var artistAlbums = new StubArtistAlbumResolver([]);
        var service = CreateService(
            new StubCatalogueSearch([
                new CatalogueSearchCandidate(
                    new MediaIdentity(MediaEntityKind.Artist, "artist-1"),
                    "The Imaginaries",
                    null,
                    null,
                    1_300,
                    IsExactTitleMatch: true),
                new CatalogueSearchCandidate(
                    new MediaIdentity(MediaEntityKind.Album, "album-1"),
                    "The Imaginaries",
                    "Another Artist",
                    null,
                    1_300,
                    IsExactTitleMatch: true,
                    CanonicalAlbumArtistId: canonicalAlbumArtistId)
            ]),
            new StubPlaylistSearch([]),
            new ReferenceCodecTestContext().Search,
            new RecordingSearchObservationStore(),
            artistTracks,
            artistAlbums: artistAlbums);

        var succeeded = Assert.IsType<SearchSucceeded>(await service.SearchAsync(
            "The Imaginaries",
            TestContext.Current.CancellationToken));

        Assert.Null(artistTracks.ArtistId);
        Assert.Null(artistAlbums.ArtistId);
        Assert.Null(succeeded.ExactArtistMatch);
        Assert.Contains(succeeded.Results, candidate =>
            candidate.Kind == MediaEntityKind.Album
            && candidate.Title == "The Imaginaries");
    }

    [Fact]
    public async Task DuplicateExactArtistsShouldNotTriggerCanonicalExpansion()
    {
        var artistTracks = new StubArtistTrackResolver([]);
        var service = CreateService(
            new StubCatalogueSearch([
                new CatalogueSearchCandidate(
                    new MediaIdentity(MediaEntityKind.Artist, "artist-1"),
                    "Signals",
                    null,
                    null,
                    1_300,
                    IsExactTitleMatch: true),
                new CatalogueSearchCandidate(
                    new MediaIdentity(MediaEntityKind.Artist, "artist-2"),
                    "Signals",
                    null,
                    null,
                    1_300,
                    IsExactTitleMatch: true)
            ]),
            new StubPlaylistSearch([]),
            new ReferenceCodecTestContext().Search,
            new RecordingSearchObservationStore(),
            artistTracks);

        var succeeded = Assert.IsType<SearchSucceeded>(await service.SearchAsync(
            "Signals",
            TestContext.Current.CancellationToken));

        Assert.Null(artistTracks.ArtistId);
        Assert.Null(succeeded.ExactArtistMatch);
        Assert.Equal(2, succeeded.Results.Count(candidate =>
            candidate.Kind == MediaEntityKind.Artist));
    }

    [Fact]
    public async Task ArtistExpansionFailureShouldBeRecordedAsCatalogueEvidence()
    {
        var failure = new InvalidOperationException("Synthetic artist expansion failure.");
        var observations = new RecordingSearchObservationStore();
        var service = CreateService(
            new StubCatalogueSearch([
                new CatalogueSearchCandidate(
                    new MediaIdentity(MediaEntityKind.Artist, "artist-1"),
                    "The Imaginaries",
                    null,
                    null,
                    1_300,
                    IsExactTitleMatch: true)
            ]),
            new StubPlaylistSearch([]),
            new ReferenceCodecTestContext().Search,
            observations,
            new FailingArtistTrackResolver(failure));

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.SearchAsync(
                "The Imaginaries",
                TestContext.Current.CancellationToken));

        Assert.Same(failure, thrown);
        Assert.Equal(SearchObservationStatus.Failed, observations.Recorded?.Status);
        var request = Assert.Single(
            observations.Recorded!.Requests,
            request => request.Source == "catalogue-artist-tracks");
        Assert.Equal(LmsSearchRequestStatus.Failed, request.Status);
        Assert.Equal(failure.Message, request.FailureMessage);
    }

    [Fact]
    public async Task RatingSearchShouldConstrainCatalogueTracksAndSkipPlaylists()
    {
        var catalogue = new StubCatalogueSearch([
            new CatalogueSearchCandidate(
                new MediaIdentity(MediaEntityKind.Artist, "artist-1"),
                "Rated Copper Artist",
                null,
                null,
                1_050),
            new CatalogueSearchCandidate(
                new MediaIdentity(MediaEntityKind.Track, "track-90"),
                "Rated Copper Signal",
                "The Imaginaries",
                "Imaginary Signals",
                1_040,
                90)
        ]);
        var playlists = new StubPlaylistSearch([
            new LmsSearchCandidate(
                new MediaIdentity(MediaEntityKind.Playlist, "playlist-9"),
                "Copper Evenings",
                null,
                null)
        ]);
        var observations = new RecordingSearchObservationStore();
        var service = CreateService(
            catalogue,
            playlists,
            new ReferenceCodecTestContext().Search,
            observations);
        var constraint = new RatingSearchConstraint(4.5m, RatingMatchMode.AtLeast);

        var outcome = await service.SearchAsync(
            new SearchCriteria("copper", constraint),
            TestContext.Current.CancellationToken);

        var succeeded = Assert.IsType<SearchSucceeded>(outcome);
        Assert.Empty(succeeded.Results);
        var result = Assert.Single(succeeded.TopTracks);
        Assert.Equal(MediaEntityKind.Track, result.Kind);
        Assert.Equal(constraint, catalogue.RatingConstraint);
        Assert.Null(playlists.Query);
        Assert.Equal(constraint, observations.Recorded?.RatingConstraint);
        Assert.Equal(MediaEntityKind.Track, observations.Recorded?.RequestedKind);
        Assert.Equal("catalogue", observations.Recorded?.Provider);
        Assert.Equal(4.5m, Assert.Single(observations.Recorded!.Candidates).Rating);
    }

    [Fact]
    public async Task NameFreeGenreAndYearSearchShouldStreamTracksAndRecordNormalisation()
    {
        var trackResolver = new StubTrackResolver([
            new CatalogueSearchCandidate(
                new MediaIdentity(MediaEntityKind.Track, "track-1"),
                "Century Pop",
                "The Imaginaries",
                "Imaginary Signals",
                1_120,
                90)
        ]);
        var playlists = new StubPlaylistSearch([]);
        var albumResolver = new StubAlbumResolver([
            new CatalogueSearchCandidate(
                new MediaIdentity(MediaEntityKind.Album, "album-1"),
                "Century Pop Album",
                "The Imaginaries",
                null,
                1_300)
        ]);
        var observations = new RecordingSearchObservationStore();
        var service = CreateService(
            new StubCatalogueSearch([]),
            playlists,
            new ReferenceCodecTestContext().Search,
            observations,
            tracks: trackResolver,
            albums: albumResolver);

        var succeeded = Assert.IsType<SearchSucceeded>(await service.SearchAsync(
            new SearchCriteria(null, Genre: "  Pop  ", FromYear: 99, ToYear: 0),
            TestContext.Current.CancellationToken));

        Assert.Single(succeeded.TopTracks);
        Assert.Equal("POP", trackResolver.Constraint?.GenreKey);
        Assert.Equal(1999, trackResolver.Constraint?.FromYear);
        Assert.Equal(2000, trackResolver.Constraint?.ToYear);
        Assert.Null(albumResolver.Constraint);
        Assert.Null(playlists.Query);
        Assert.Equal("Pop", observations.Recorded?.Genre);
        Assert.Equal(99, observations.Recorded?.RequestedFromYear);
        Assert.Equal(0, observations.Recorded?.RequestedToYear);
        Assert.Equal(1999, observations.Recorded?.EffectiveFromYear);
        Assert.Equal(2000, observations.Recorded?.EffectiveToYear);
        Assert.Equal(MediaEntityKind.Track, observations.Recorded?.RequestedKind);
        Assert.Equal(
            SearchObservationInterpretation.NameFreeFiltered,
            observations.Recorded?.Interpretation);
        Assert.Equal("catalogue", observations.Recorded?.Provider);
        var request = Assert.Single(observations.Recorded!.Requests);
        Assert.Equal("catalogue-filtered-tracks", request.Source);
        Assert.Equal("filtered-tracks", request.Command);
    }

    [Fact]
    public async Task NameFreeYearSearchShouldReturnBoundedAlbumsAndTracks()
    {
        var trackResolver = new StubTrackResolver([
            new CatalogueSearchCandidate(
                new MediaIdentity(MediaEntityKind.Track, "track-1"),
                "Century Signal",
                "The Imaginaries",
                "Imaginary Signals",
                1_120,
                60)
        ]);
        var albumResolver = new StubAlbumResolver(
            Enumerable.Range(1, 7)
                .Select(index => new CatalogueSearchCandidate(
                    new MediaIdentity(MediaEntityKind.Album, $"album-{index}"),
                    $"Fictional Album {index}",
                    "The Imaginaries",
                    null,
                    1_300))
                .ToArray());
        var observations = new RecordingSearchObservationStore();
        var references = new ReferenceCodecTestContext();
        var service = CreateService(
            new StubCatalogueSearch([]),
            new StubPlaylistSearch([]),
            references.Search,
            observations,
            tracks: trackResolver,
            albums: albumResolver);

        var succeeded = Assert.IsType<SearchSucceeded>(await service.SearchAsync(
            new SearchCriteria(null, FromYear: 1990, ToYear: 1999),
            TestContext.Current.CancellationToken));

        Assert.Equal(
            SearchResultPolicy.AlbumLimit,
            succeeded.Results.Count(candidate => candidate.Kind == MediaEntityKind.Album));
        Assert.Single(succeeded.Results, candidate => candidate.Kind == MediaEntityKind.Track);
        Assert.Empty(succeeded.TopTracks);
        Assert.Equal(
            new CatalogueAlbumSearchConstraint(1990, 1999),
            albumResolver.Constraint);
        Assert.Null(observations.Recorded?.RequestedKind);
        Assert.Collection(
            observations.Recorded!.Requests,
            request => Assert.Equal("catalogue-filtered-tracks", request.Source),
            request => Assert.Equal("catalogue-filtered-albums", request.Source));
        var returnedAlbum = succeeded.Results.First(candidate =>
            candidate.Kind == MediaEntityKind.Album);
        var observedAlbum = Assert.Single(observations.Recorded.Candidates, candidate =>
            candidate.Title == returnedAlbum.Title);
        Assert.Equal(
            observedAlbum.CorrelationId,
            references.Search.TryDecode(returnedAlbum.Reference)?.CorrelationId);
    }

    [Fact]
    public async Task RequiredYearAlbumFailureShouldFailRatherThanWidenTheSearch()
    {
        var failure = new InvalidOperationException("Synthetic album-year failure.");
        var observations = new RecordingSearchObservationStore();
        var service = CreateService(
            new StubCatalogueSearch([]),
            new StubPlaylistSearch([]),
            new ReferenceCodecTestContext().Search,
            observations,
            tracks: new StubTrackResolver([]),
            albums: new FailingAlbumResolver(failure));

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.SearchAsync(
                new SearchCriteria(null, FromYear: 1990, ToYear: 1999),
                TestContext.Current.CancellationToken));

        Assert.Same(failure, thrown);
        Assert.Equal(SearchObservationStatus.Failed, observations.Recorded?.Status);
        Assert.Contains(observations.Recorded!.Requests, request =>
            request.Source == "catalogue-filtered-albums"
            && request.Status == LmsSearchRequestStatus.Failed);
    }

    [Theory]
    [InlineData(1990, null)]
    [InlineData(null, 1999)]
    [InlineData(999, 2000)]
    [InlineData(2000, 999)]
    public async Task InvalidYearRangesShouldBeRejectedBeforeRetrieval(
        int? fromYear,
        int? toYear)
    {
        var catalogue = new StubCatalogueSearch([]);
        var trackResolver = new StubTrackResolver([]);
        var service = CreateService(
            catalogue,
            new StubPlaylistSearch([]),
            new ReferenceCodecTestContext().Search,
            new RecordingSearchObservationStore(),
            tracks: trackResolver);

        var outcome = await service.SearchAsync(
            new SearchCriteria(null, Genre: "Pop", FromYear: fromYear, ToYear: toYear),
            TestContext.Current.CancellationToken);

        Assert.IsType<SearchRejected>(outcome);
        Assert.Null(catalogue.Query);
        Assert.Null(trackResolver.Constraint);
    }

    [Fact]
    public async Task NamedYearSearchShouldReturnMatchingAlbumAndTrackGroups()
    {
        var catalogue = new StubCatalogueSearch([
            new CatalogueSearchCandidate(
                new MediaIdentity(MediaEntityKind.Album, "album-1"),
                "Copper Century",
                "The Imaginaries",
                null,
                1_300),
            new CatalogueSearchCandidate(
                new MediaIdentity(MediaEntityKind.Track, "track-1"),
                "Copper Century",
                "The Imaginaries",
                "Fictional Frequencies",
                1_120,
                60)
        ]);
        var service = CreateService(
            catalogue,
            new StubPlaylistSearch([]),
            new ReferenceCodecTestContext().Search,
            new RecordingSearchObservationStore());

        var succeeded = Assert.IsType<SearchSucceeded>(await service.SearchAsync(
            new SearchCriteria("Copper Century", FromYear: 1990, ToYear: 1999),
            TestContext.Current.CancellationToken));

        Assert.Single(succeeded.Results, candidate =>
            candidate.Kind == MediaEntityKind.Album);
        Assert.Single(succeeded.Results, candidate =>
            candidate.Kind == MediaEntityKind.Track);
        Assert.Equal(
            new CatalogueAlbumSearchConstraint(1990, 1999),
            catalogue.AlbumConstraint);
    }

    [Fact]
    public async Task RatingAndYearSearchShouldNotWidenCriteriaToAlbums()
    {
        var catalogue = new StubCatalogueSearch([
            new CatalogueSearchCandidate(
                new MediaIdentity(MediaEntityKind.Album, "album-1"),
                "Copper Century",
                "The Imaginaries",
                null,
                1_300),
            new CatalogueSearchCandidate(
                new MediaIdentity(MediaEntityKind.Track, "track-1"),
                "Copper Century",
                "The Imaginaries",
                "Fictional Frequencies",
                1_120,
                100)
        ]);
        var service = CreateService(
            catalogue,
            new StubPlaylistSearch([]),
            new ReferenceCodecTestContext().Search,
            new RecordingSearchObservationStore());

        var succeeded = Assert.IsType<SearchSucceeded>(await service.SearchAsync(
            new SearchCriteria(
                "Copper Century",
                new RatingSearchConstraint(5, RatingMatchMode.Exact),
                FromYear: 1990,
                ToYear: 1999),
            TestContext.Current.CancellationToken));

        Assert.DoesNotContain(succeeded.Results, candidate =>
            candidate.Kind == MediaEntityKind.Album);
        Assert.Null(catalogue.CatalogueConstraint?.AlbumConstraint);
    }

    [Fact]
    public async Task YearConstrainedExactArtistShouldReturnCanonicalAlbumsInRange()
    {
        var catalogue = new StubCatalogueSearch([
            new CatalogueSearchCandidate(
                new MediaIdentity(MediaEntityKind.Artist, "artist-1"),
                "The Imaginaries",
                null,
                null,
                1_300,
                IsExactTitleMatch: true)
        ]);
        var artistAlbums = new StubArtistAlbumResolver([
            new CatalogueSearchCandidate(
                new MediaIdentity(MediaEntityKind.Album, "album-1"),
                "Fictional Frequencies",
                "The Imaginaries",
                null,
                1_300)
        ]);
        var service = CreateService(
            catalogue,
            new StubPlaylistSearch([]),
            new ReferenceCodecTestContext().Search,
            new RecordingSearchObservationStore(),
            artistAlbums: artistAlbums);

        var succeeded = Assert.IsType<SearchSucceeded>(await service.SearchAsync(
            new SearchCriteria("The Imaginaries", FromYear: 2000, ToYear: 2009),
            TestContext.Current.CancellationToken));

        Assert.Equal("The Imaginaries", succeeded.ExactArtistMatch?.Name);
        Assert.Single(succeeded.Results, candidate =>
            candidate.Kind == MediaEntityKind.Album);
        Assert.Equal("artist-1", artistAlbums.ArtistId);
        Assert.Equal(
            new CatalogueAlbumSearchConstraint(2000, 2009),
            artistAlbums.Constraint);
    }

    [Fact]
    public async Task RatingConstrainedExactArtistShouldRetainResolvedInterpretation()
    {
        // Arrange
        var catalogue = new StubCatalogueSearch([
            new CatalogueSearchCandidate(
                new MediaIdentity(MediaEntityKind.Artist, "artist-1"),
                "The Imaginaries",
                null,
                null,
                1_300,
                IsExactTitleMatch: true)
        ]);
        var artistTracks = new StubArtistTrackResolver([
            Track("track-100", 100),
            Track("track-90", 90),
            Track("track-80", 80),
            Track("track-0", 0)
        ]);
        var artistAlbums = new StubArtistAlbumResolver([
            new CatalogueSearchCandidate(
                new MediaIdentity(MediaEntityKind.Album, "album-1"),
                "Fictional Frequencies",
                "The Imaginaries",
                null,
                1_300)
        ]);
        var playlists = new StubPlaylistSearch([]);
        var references = new ReferenceCodecTestContext();
        var service = CreateService(
            catalogue,
            playlists,
            references.Search,
            new RecordingSearchObservationStore(),
            artistTracks,
            references.Browse,
            artistAlbums);
        var constraint = new RatingSearchConstraint(4.5m, RatingMatchMode.AtLeast);

        // Act
        var succeeded = Assert.IsType<SearchSucceeded>(await service.SearchAsync(
            new SearchCriteria("The Imaginaries", constraint),
            TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal("The Imaginaries", succeeded.ExactArtistMatch?.Name);
        Assert.DoesNotContain(succeeded.Results, candidate =>
            candidate.Kind is MediaEntityKind.Artist or MediaEntityKind.Album);
        Assert.All(
            succeeded.TopTracks.Concat(succeeded.Results.Where(candidate =>
                candidate.Kind == MediaEntityKind.Track)),
            candidate => Assert.True(candidate.NativeRating >= 90));
        Assert.Null(playlists.Query);
        Assert.Null(artistAlbums.ArtistId);

        static CatalogueSearchCandidate Track(string id, int rating) => new(
            new MediaIdentity(MediaEntityKind.Track, id),
            $"Track {id}",
            "The Imaginaries",
            "Fictional Frequencies",
            1_100,
            rating);
    }

    [Fact]
    public async Task InvalidRatingConstraintShouldBeRejectedBeforeRetrieval()
    {
        var catalogue = new StubCatalogueSearch([]);
        var playlists = new StubPlaylistSearch([]);
        var service = CreateService(
            catalogue,
            playlists,
            new ReferenceCodecTestContext().Search,
            new RecordingSearchObservationStore());

        var outcome = await service.SearchAsync(
            new SearchCriteria(
                "copper",
                new RatingSearchConstraint(5.01m, RatingMatchMode.Exact)),
            TestContext.Current.CancellationToken);

        Assert.Equal(
            SearchRejectionReason.InvalidQuery,
            Assert.IsType<SearchRejected>(outcome).Reason);
        Assert.Null(catalogue.Query);
        Assert.Null(playlists.Query);
    }

    [Fact]
    public async Task WhitespaceNameShouldPerformBroadDiscoveryWithoutNamedResolvers()
    {
        var catalogue = new StubCatalogueSearch([]);
        var playlists = new StubPlaylistSearch([]);
        var tracks = new StubTrackResolver(
            Enumerable.Range(1, 50)
                .Select(index => new CatalogueSearchCandidate(
                    new MediaIdentity(MediaEntityKind.Track, $"track-{index}"),
                    $"Fictional Track {index}",
                    "The Imaginaries",
                    "Imaginary Signals",
                    1_120,
                    index <= 10 ? 100 : 60))
                .ToArray());
        var observations = new RecordingSearchObservationStore();
        var service = CreateService(
            catalogue,
            playlists,
            new ReferenceCodecTestContext().Search,
            observations,
            tracks: tracks);

        var outcome = await service.SearchAsync("   ", TestContext.Current.CancellationToken);

        var succeeded = Assert.IsType<SearchSucceeded>(outcome);
        Assert.Equal(SearchResultPolicy.TopTrackLimit, succeeded.TopTracks.Count);
        Assert.Equal(SearchResultPolicy.TrackLimit, succeeded.Results.Count);
        Assert.Empty(succeeded.TopTracks.Select(result => result.Reference)
            .Intersect(succeeded.Results.Select(result => result.Reference)));
        Assert.Null(catalogue.Query);
        Assert.Null(playlists.Query);
        Assert.Equal(new CatalogueTrackSearchConstraint(), tracks.Constraint);
        Assert.Equal(
            SearchObservationInterpretation.BroadDiscovery,
            observations.Recorded?.Interpretation);
        Assert.Equal(MediaEntityKind.Track, observations.Recorded?.RequestedKind);
        Assert.Equal("catalogue", observations.Recorded?.Provider);
        var request = Assert.Single(observations.Recorded!.Requests);
        Assert.Equal("catalogue-broad-tracks", request.Source);
        Assert.Equal("broad-tracks", request.Command);
        Assert.Equal(50, request.ResultCount);
    }

    [Fact]
    public async Task RatingOnlySearchShouldUseStrictNameFreeConstraint()
    {
        var constraint = new RatingSearchConstraint(4.5m, RatingMatchMode.AtLeast);
        var tracks = new StubTrackResolver([
            new CatalogueSearchCandidate(
                new MediaIdentity(MediaEntityKind.Track, "track-1"),
                "Fictional Favourite",
                "The Imaginaries",
                "Imaginary Signals",
                1_120,
                90)
        ]);
        var observations = new RecordingSearchObservationStore();
        var service = CreateService(
            new StubCatalogueSearch([]),
            new StubPlaylistSearch([]),
            new ReferenceCodecTestContext().Search,
            observations,
            tracks: tracks);

        var succeeded = Assert.IsType<SearchSucceeded>(await service.SearchAsync(
            new SearchCriteria(null, constraint),
            TestContext.Current.CancellationToken));

        Assert.Single(succeeded.TopTracks);
        Assert.Empty(succeeded.Results);
        Assert.Equal(constraint, tracks.Constraint?.RatingConstraint);
        Assert.Null(tracks.Constraint?.GenreKey);
        Assert.Null(tracks.Constraint?.FromYear);
        Assert.Null(tracks.Constraint?.ToYear);
        Assert.Equal(
            SearchObservationInterpretation.NameFreeFiltered,
            observations.Recorded?.Interpretation);
    }

    [Theory]
    [InlineData("*")]
    [InlineData("---")]
    public async Task SearchShouldRejectQueriesWithoutSearchableTextBeforeRetrieval(
        string query)
    {
        var catalogue = new StubCatalogueSearch([]);
        var playlists = new StubPlaylistSearch([]);
        var service = CreateService(
            catalogue,
            playlists,
            new ReferenceCodecTestContext().Search,
            new RecordingSearchObservationStore());

        var outcome = await service.SearchAsync(
            query,
            TestContext.Current.CancellationToken);

        var rejected = Assert.IsType<SearchRejected>(outcome);
        Assert.Equal(SearchRejectionReason.InvalidQuery, rejected.Reason);
        Assert.Contains("not a wildcard", rejected.Message, StringComparison.Ordinal);
        Assert.Contains("Omit name", rejected.Message, StringComparison.Ordinal);
        Assert.Null(catalogue.Query);
        Assert.Null(playlists.Query);
    }

    [Fact]
    public async Task SearchShouldRejectMoreThanTwentyNormalisedTokensBeforeRetrieval()
    {
        var catalogue = new StubCatalogueSearch([]);
        var playlists = new StubPlaylistSearch([]);
        var service = CreateService(
            catalogue,
            playlists,
            new ReferenceCodecTestContext().Search,
            new RecordingSearchObservationStore());
        var query = string.Join('-', Enumerable.Repeat("signal", 21));

        var outcome = await service.SearchAsync(query, TestContext.Current.CancellationToken);

        var rejected = Assert.IsType<SearchRejected>(outcome);
        Assert.Equal(SearchRejectionReason.InvalidQuery, rejected.Reason);
        Assert.Contains("20 words", rejected.Message, StringComparison.Ordinal);
        Assert.Null(catalogue.Query);
        Assert.Null(playlists.Query);
    }

    [Fact]
    public async Task SearchShouldRejectMoreThanFiveHundredCharactersBeforeRetrieval()
    {
        var catalogue = new StubCatalogueSearch([]);
        var playlists = new StubPlaylistSearch([]);
        var service = CreateService(
            catalogue,
            playlists,
            new ReferenceCodecTestContext().Search,
            new RecordingSearchObservationStore());

        var outcome = await service.SearchAsync(
            new string('x', 501),
            TestContext.Current.CancellationToken);

        var rejected = Assert.IsType<SearchRejected>(outcome);
        Assert.Equal(SearchRejectionReason.InvalidQuery, rejected.Reason);
        Assert.Contains("500 characters", rejected.Message, StringComparison.Ordinal);
        Assert.Null(catalogue.Query);
        Assert.Null(playlists.Query);
    }

    [Fact]
    public async Task SearchShouldApplyNameLengthLimitBeforeTrimming()
    {
        var catalogue = new StubCatalogueSearch([]);
        var playlists = new StubPlaylistSearch([]);
        var service = CreateService(
            catalogue,
            playlists,
            new ReferenceCodecTestContext().Search,
            new RecordingSearchObservationStore());

        var outcome = await service.SearchAsync(
            new string(' ', 500) + "x",
            TestContext.Current.CancellationToken);

        var rejected = Assert.IsType<SearchRejected>(outcome);
        Assert.Contains("500 characters", rejected.Message, StringComparison.Ordinal);
        Assert.Null(catalogue.Query);
        Assert.Null(playlists.Query);
    }

    [Fact]
    public async Task SearchShouldApplyGenreLengthLimitBeforeTrimming()
    {
        var catalogue = new StubCatalogueSearch([]);
        var tracks = new StubTrackResolver([]);
        var service = CreateService(
            catalogue,
            new StubPlaylistSearch([]),
            new ReferenceCodecTestContext().Search,
            new RecordingSearchObservationStore(),
            tracks: tracks);

        var outcome = await service.SearchAsync(
            new SearchCriteria(null, Genre: new string(' ', 500) + "x"),
            TestContext.Current.CancellationToken);

        var rejected = Assert.IsType<SearchRejected>(outcome);
        Assert.Contains("500 characters", rejected.Message, StringComparison.Ordinal);
        Assert.Null(catalogue.Query);
        Assert.Null(tracks.Constraint);
    }

    [Theory]
    [InlineData("Copper Lines tracks rating 5")]
    [InlineData("Copper Lines 5 star tracks")]
    [InlineData("Copper Lines tracks 4+")]
    public async Task SearchShouldRejectRatingSyntaxInTheMediaNameBeforeRetrieval(
        string query)
    {
        var catalogue = new StubCatalogueSearch([]);
        var playlists = new StubPlaylistSearch([]);
        var service = CreateService(
            catalogue,
            playlists,
            new ReferenceCodecTestContext().Search,
            new RecordingSearchObservationStore());

        var outcome = await service.SearchAsync(
            query,
            TestContext.Current.CancellationToken);

        var rejected = Assert.IsType<SearchRejected>(outcome);
        Assert.Equal(SearchRejectionReason.InvalidQuery, rejected.Reason);
        Assert.Contains("media-name text only", rejected.Message, StringComparison.Ordinal);
        Assert.Contains("ratingMatch", rejected.Message, StringComparison.Ordinal);
        Assert.Null(catalogue.Query);
        Assert.Null(playlists.Query);
    }

    [Fact]
    public async Task SearchShouldAllowOrdinaryNumericMediaNames()
    {
        var catalogue = new StubCatalogueSearch([]);
        var service = CreateService(
            catalogue,
            new StubPlaylistSearch([]),
            new ReferenceCodecTestContext().Search,
            new RecordingSearchObservationStore());

        var outcome = await service.SearchAsync(
            "1984 Copper Lines",
            TestContext.Current.CancellationToken);

        Assert.IsType<SearchSucceeded>(outcome);
        Assert.Equal("1984 Copper Lines", catalogue.Query);
    }

    [Fact]
    public async Task MissingProductionIndexShouldReturnAnExplicitRejection()
    {
        var service = CreateService(
            new UnavailableCatalogueSearch(),
            new StubPlaylistSearch([]),
            new ReferenceCodecTestContext().Search,
            new RecordingSearchObservationStore());

        var outcome = await service.SearchAsync("signals", TestContext.Current.CancellationToken);

        var rejected = Assert.IsType<SearchRejected>(outcome);
        Assert.Equal(SearchRejectionReason.SearchUnavailable, rejected.Reason);
        Assert.Contains("has not been built", rejected.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PreparingCatalogueShouldReturnTheCurrentAvailabilityMessage()
    {
        var service = CreateService(
            new UnavailableCatalogueSearch(),
            new StubPlaylistSearch([]),
            new ReferenceCodecTestContext().Search,
            new RecordingSearchObservationStore(),
            searchAvailability: new FixedCatalogueSearchAvailabilityService(
                "The music catalogue is being prepared."));

        var outcome = await service.SearchAsync("signals", TestContext.Current.CancellationToken);

        var rejected = Assert.IsType<SearchRejected>(outcome);
        Assert.Equal("The music catalogue is being prepared.", rejected.Message);
    }

    [Fact]
    public async Task ObservationPersistenceFailureShouldNotFailSuccessfulSearch()
    {
        var observations = new RecordingSearchObservationStore
        {
            RecordFailure = new InvalidOperationException("Synthetic persistence failure.")
        };
        var service = CreateService(
            new StubCatalogueSearch([
                new CatalogueSearchCandidate(
                    new MediaIdentity(MediaEntityKind.Artist, "7"),
                    "Copper Lines",
                    null,
                    null,
                    1_040)
            ]),
            new StubPlaylistSearch([]),
            new ReferenceCodecTestContext().Search,
            observations);

        var outcome = await service.SearchAsync(
            "copper",
            TestContext.Current.CancellationToken);

        Assert.Single(Assert.IsType<SearchSucceeded>(outcome).Results);
    }

    [Fact]
    public async Task PlaylistFailureShouldPreserveCatalogueCandidatesInTheObservation()
    {
        var store = new RecordingSearchObservationStore();
        var catalogue = new StubCatalogueSearch([
            new CatalogueSearchCandidate(
                new MediaIdentity(MediaEntityKind.Track, "51"),
                "Silver Static",
                "The Copper Lines",
                "Night Signals",
                1_300)
        ]);
        var response = new LmsSearchResponse(
            [],
            [new LmsSearchRequestObservation(
                "playlists",
                "[\"playlists\"]",
                LmsSearchRequestStatus.Failed,
                "Synthetic failure.",
                4,
                0)],
            4);
        var service = CreateService(
            catalogue,
            new FailingPlaylistSearch(response),
            new ReferenceCodecTestContext().Search,
            store);

        await Assert.ThrowsAsync<LmsSearchFailedException>(() =>
            service.SearchAsync("signals", TestContext.Current.CancellationToken));

        Assert.Equal(SearchObservationStatus.Failed, store.Recorded?.Status);
        Assert.Equal(catalogue.Descriptor.Name, store.Recorded?.Resolver);
        Assert.Equal(catalogue.Descriptor.Version, store.Recorded?.ResolverVersion);
        Assert.Equal("Silver Static", Assert.Single(store.Recorded!.Candidates).Title);
        Assert.Equal(3, store.Recorded.Requests.Count);
    }

    [Fact]
    public async Task UnexpectedPlaylistFailureShouldBeRethrownAndRecordedWithSourceEvidence()
    {
        var store = new RecordingSearchObservationStore();
        var failure = new InvalidOperationException("Synthetic unexpected failure.");
        var service = CreateService(
            new StubCatalogueSearch([
                new CatalogueSearchCandidate(
                    new MediaIdentity(MediaEntityKind.Track, "51"),
                    "Silver Static",
                    "The Copper Lines",
                    "Night Signals",
                    1_300)
            ]),
            new UnexpectedlyFailingPlaylistSearch(failure),
            new ReferenceCodecTestContext().Search,
            store);

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.SearchAsync("signals", TestContext.Current.CancellationToken));

        Assert.Same(failure, thrown);
        Assert.Equal("Silver Static", Assert.Single(store.Recorded!.Candidates).Title);
        Assert.Collection(
            store.Recorded.Requests,
            request =>
            {
                Assert.Equal("catalogue-index", request.Source);
                Assert.Equal("search:unconstrained", request.Command);
                Assert.Equal(LmsSearchRequestStatus.Completed, request.Status);
            },
            request =>
            {
                Assert.Equal("catalogue-index", request.Source);
                Assert.Equal("search:top-rating", request.Command);
                Assert.Equal(LmsSearchRequestStatus.Completed, request.Status);
            },
            request =>
            {
                Assert.Equal("playlists", request.Source);
                Assert.Equal(LmsSearchRequestStatus.Failed, request.Status);
                Assert.Equal(failure.Message, request.FailureMessage);
            });
    }

    private static SearchService CreateService(
        ICatalogueSearchResolver catalogue,
        ILmsPlaylistSearchClient playlists,
        ISearchResultReferenceCodec codec,
        ISearchObservationStore observations,
        ICatalogueArtistTrackResolver? artistTracks = null,
        IBrowseReferenceCodec? browseCodec = null,
        ICatalogueArtistAlbumResolver? artistAlbums = null,
        ICatalogueTrackResolver? tracks = null,
        ICatalogueAlbumResolver? albums = null,
        ICatalogueSearchAvailabilityService? searchAvailability = null) => new(
            catalogue,
            artistTracks ?? new EmptyArtistTrackResolver(),
            tracks ?? new EmptyTrackResolver(),
            albums ?? new EmptyAlbumResolver(),
            artistAlbums ?? new EmptyArtistAlbumResolver(),
            playlists,
            codec,
            browseCodec ?? new ReferenceCodecTestContext().Browse,
            new SearchCandidateSelector(new Random(17)),
            new SearchObservationRecorder(
                observations,
                TimeProvider.System,
                NullLogger<SearchObservationRecorder>.Instance),
            TimeProvider.System,
            searchAvailability ?? PassthroughCatalogueSearchAvailabilityService.Instance,
            NullLogger<SearchService>.Instance);

    private sealed class StubCatalogueSearch(
        IReadOnlyList<CatalogueSearchCandidate> results) : ICatalogueSearchResolver
    {
        public SearchResolverDescriptor Descriptor { get; } = new(
            "fictional-catalogue",
            "7");

        public string? Query { get; private set; }
        public RatingSearchConstraint? RatingConstraint { get; private set; }
        public CatalogueTrackSearchConstraint? Constraint { get; private set; }
        public CatalogueSearchConstraint? CatalogueConstraint { get; private set; }
        public CatalogueAlbumSearchConstraint? AlbumConstraint { get; private set; }

        public Task<CatalogueSearchResponse> SearchAsync(
            string query,
            CancellationToken cancellationToken)
        {
            Query = query;
            return Task.FromResult(new CatalogueSearchResponse(results, 1, 1));
        }

        public Task<CatalogueSearchResponse> SearchAsync(
            string query,
            RatingSearchConstraint? ratingConstraint,
            CancellationToken cancellationToken)
        {
            RatingConstraint = ratingConstraint;
            Query = query;
            return Task.FromResult(new CatalogueSearchResponse(
                ratingConstraint is null
                    ? results
                    : results.Where(candidate =>
                        candidate.Identity.Kind != MediaEntityKind.Track
                        || Matches(candidate.NativeRating, ratingConstraint)).ToArray(),
                1,
                1));
        }

        public Task<CatalogueSearchResponse> SearchAsync(
            string query,
            CatalogueTrackSearchConstraint? constraint,
            CancellationToken cancellationToken)
        {
            Constraint = constraint;
            return SearchAsync(query, constraint?.RatingConstraint, cancellationToken);
        }

        public Task<CatalogueSearchResponse> SearchAsync(
            string query,
            CatalogueSearchConstraint? constraint,
            CancellationToken cancellationToken)
        {
            CatalogueConstraint = constraint;
            Constraint = constraint?.TrackConstraint;
            RatingConstraint = constraint?.TrackConstraint.RatingConstraint;
            if (constraint?.AlbumConstraint is not null)
            {
                AlbumConstraint = constraint.AlbumConstraint;
            }

            Query = query;
            var candidates = results.AsEnumerable();
            if (constraint is not null)
            {
                candidates = candidates.Where(candidate =>
                    candidate.Identity.Kind == MediaEntityKind.Track
                    || (constraint.AlbumConstraint is not null
                        && candidate.Identity.Kind == MediaEntityKind.Album));
                if (constraint.TrackConstraint.RatingConstraint is not null)
                {
                    candidates = candidates.Where(candidate =>
                        candidate.Identity.Kind != MediaEntityKind.Track
                        || Matches(
                            candidate.NativeRating,
                            constraint.TrackConstraint.RatingConstraint));
                }
            }

            return Task.FromResult(new CatalogueSearchResponse(candidates.ToArray(), 1, 1));
        }

        private static bool Matches(
            int nativeRating,
            RatingSearchConstraint constraint)
        {
            var nativeThreshold = constraint.Rating * 20;
            return constraint.Match switch
            {
                RatingMatchMode.Exact => decimal.IsInteger(nativeThreshold)
                    && nativeRating == decimal.ToInt32(nativeThreshold),
                RatingMatchMode.AtLeast => nativeRating >= decimal.ToInt32(
                    decimal.Ceiling(nativeThreshold)),
                _ => false
            };
        }
    }

    private sealed class EmptyArtistTrackResolver : ICatalogueArtistTrackResolver
    {
        public async IAsyncEnumerable<CatalogueSearchCandidate> ReadArtistTracksAsync(
            string artistId,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.CompletedTask;
            yield break;
        }

        public async IAsyncEnumerable<CatalogueSearchCandidate> ReadArtistTracksAsync(
            string artistId,
            CatalogueTrackSearchConstraint? constraint,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.CompletedTask;
            yield break;
        }
    }

    private sealed class EmptyTrackResolver : ICatalogueTrackResolver
    {
        public async IAsyncEnumerable<CatalogueSearchCandidate> ReadTracksAsync(
            CatalogueTrackSearchConstraint constraint,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.CompletedTask;
            yield break;
        }
    }

    private sealed class StubTrackResolver(
        IReadOnlyList<CatalogueSearchCandidate> candidates) : ICatalogueTrackResolver
    {
        public CatalogueTrackSearchConstraint? Constraint { get; private set; }

        public async IAsyncEnumerable<CatalogueSearchCandidate> ReadTracksAsync(
            CatalogueTrackSearchConstraint constraint,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            Constraint = constraint;
            foreach (var candidate in candidates)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return candidate;
                await Task.Yield();
            }
        }
    }

    private sealed class EmptyAlbumResolver : ICatalogueAlbumResolver
    {
        public async IAsyncEnumerable<CatalogueSearchCandidate> ReadAlbumsAsync(
            CatalogueAlbumSearchConstraint constraint,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.CompletedTask;
            yield break;
        }
    }

    private sealed class StubAlbumResolver(
        IReadOnlyList<CatalogueSearchCandidate> candidates) : ICatalogueAlbumResolver
    {
        public CatalogueAlbumSearchConstraint? Constraint { get; private set; }

        public async IAsyncEnumerable<CatalogueSearchCandidate> ReadAlbumsAsync(
            CatalogueAlbumSearchConstraint constraint,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            Constraint = constraint;
            foreach (var candidate in candidates)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return candidate;
                await Task.Yield();
            }
        }
    }

    private sealed class FailingAlbumResolver(Exception failure) : ICatalogueAlbumResolver
    {
        public async IAsyncEnumerable<CatalogueSearchCandidate> ReadAlbumsAsync(
            CatalogueAlbumSearchConstraint constraint,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.Yield();
            cancellationToken.ThrowIfCancellationRequested();
            if (constraint.FromYear > 0)
            {
                throw failure;
            }

            yield break;
        }
    }

    private sealed class EmptyArtistAlbumResolver : ICatalogueArtistAlbumResolver
    {
        public async IAsyncEnumerable<CatalogueSearchCandidate> ReadArtistAlbumsAsync(
            string artistId,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.CompletedTask;
            yield break;
        }
    }

    private sealed class StubArtistAlbumResolver(
        IReadOnlyList<CatalogueSearchCandidate> candidates) : ICatalogueArtistAlbumResolver
    {
        public string? ArtistId { get; private set; }
        public CatalogueAlbumSearchConstraint? Constraint { get; private set; }

        public async IAsyncEnumerable<CatalogueSearchCandidate> ReadArtistAlbumsAsync(
            string artistId,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            ArtistId = artistId;
            foreach (var candidate in candidates)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return candidate;
                await Task.Yield();
            }
        }

        public async IAsyncEnumerable<CatalogueSearchCandidate> ReadArtistAlbumsAsync(
            string artistId,
            CatalogueAlbumSearchConstraint? constraint,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            ArtistId = artistId;
            Constraint = constraint;
            foreach (var candidate in candidates)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return candidate;
                await Task.Yield();
            }
        }
    }

    private sealed class StubArtistTrackResolver(
        IReadOnlyList<CatalogueSearchCandidate> candidates) : ICatalogueArtistTrackResolver
    {
        public string? ArtistId { get; private set; }
        public CatalogueTrackSearchConstraint? Constraint { get; private set; }

        public async IAsyncEnumerable<CatalogueSearchCandidate> ReadArtistTracksAsync(
            string artistId,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            ArtistId = artistId;
            foreach (var candidate in candidates)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return candidate;
                await Task.Yield();
            }
        }

        public async IAsyncEnumerable<CatalogueSearchCandidate> ReadArtistTracksAsync(
            string artistId,
            CatalogueTrackSearchConstraint? constraint,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            ArtistId = artistId;
            Constraint = constraint;
            foreach (var candidate in candidates)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (constraint?.RatingConstraint is null
                    || Matches(candidate.NativeRating, constraint.RatingConstraint))
                {
                    yield return candidate;
                }

                await Task.Yield();
            }
        }

        private static bool Matches(
            int nativeRating,
            RatingSearchConstraint constraint) =>
            constraint.Match == RatingMatchMode.Exact
                ? nativeRating == decimal.ToInt32(constraint.Rating * 20m)
                : nativeRating >= decimal.ToInt32(constraint.Rating * 20m);
    }

    private sealed class FailingArtistTrackResolver(
        Exception failure) : ICatalogueArtistTrackResolver
    {
        public async IAsyncEnumerable<CatalogueSearchCandidate> ReadArtistTracksAsync(
            string artistId,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.Yield();
            cancellationToken.ThrowIfCancellationRequested();
            if (!string.IsNullOrEmpty(artistId))
            {
                throw failure;
            }

            yield break;
        }
    }

    private sealed class UnavailableCatalogueSearch : ICatalogueSearchResolver
    {
        public SearchResolverDescriptor Descriptor { get; } = new(
            "unavailable-catalogue",
            "3");

        public Task<CatalogueSearchResponse> SearchAsync(
            string query,
            CancellationToken cancellationToken) =>
            Task.FromException<CatalogueSearchResponse>(
                new CatalogueSearchUnavailableException(
                    "The production catalogue search index has not been built."));

        public Task<CatalogueSearchResponse> SearchAsync(
            string query,
            RatingSearchConstraint? ratingConstraint,
            CancellationToken cancellationToken) =>
            SearchAsync(query, cancellationToken);
    }

    private sealed class StubPlaylistSearch(
        IReadOnlyList<LmsSearchCandidate> results) : ILmsPlaylistSearchClient
    {
        public string? Query { get; private set; }

        public Task<LmsSearchResponse> SearchPlaylistsAsync(
            string query,
            CancellationToken cancellationToken)
        {
            Query = query;
            return Task.FromResult(new LmsSearchResponse(results, [], 1));
        }
    }

    private sealed class FailingPlaylistSearch(
        LmsSearchResponse response) : ILmsPlaylistSearchClient
    {
        public Task<LmsSearchResponse> SearchPlaylistsAsync(
            string query,
            CancellationToken cancellationToken) =>
            Task.FromException<LmsSearchResponse>(new LmsSearchFailedException(
                "LMS search failed for playlists.",
                response,
                new LmsRequestException("Synthetic failure.")));
    }

    private sealed class UnexpectedlyFailingPlaylistSearch(
        Exception failure) : ILmsPlaylistSearchClient
    {
        public Task<LmsSearchResponse> SearchPlaylistsAsync(
            string query,
            CancellationToken cancellationToken) =>
            Task.FromException<LmsSearchResponse>(failure);
    }
}

public sealed class SearchResultReferenceCodecTests
{
    [Theory]
    [InlineData(MediaEntityKind.Artist, "artist_")]
    [InlineData(MediaEntityKind.Album, "album_")]
    [InlineData(MediaEntityKind.Track, "track_")]
    [InlineData(MediaEntityKind.Playlist, "playlist_")]
    public void CodecShouldUseTheEntityPrefixAndRoundTripTheValue(
        MediaEntityKind kind,
        string expectedPrefix)
    {
        var codec = new ReferenceCodecTestContext().Search;
        var expected = new SearchResultReferenceValue(
            "123456781234123412341234567890ab",
            new MediaIdentity(kind, "204"));

        var reference = codec.Encode(expected);
        var decoded = codec.TryDecode(reference);

        Assert.StartsWith(expectedPrefix, reference, StringComparison.Ordinal);
        Assert.Matches($"^{expectedPrefix}[0-9a-f]{{16}}$", reference);
        Assert.Equal(expected, decoded);
    }

    [Fact]
    public void TryDecodeShouldReturnNullForMalformedReference()
    {
        var decoded = new ReferenceCodecTestContext().Search.TryDecode("album_not-a-handle");

        Assert.Null(decoded);
    }
}
