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
        var service = CreateService(
            catalogue,
            new StubPlaylistSearch([]),
            new ReferenceCodecTestContext().Search,
            observations,
            artistTracks);

        var succeeded = Assert.IsType<SearchSucceeded>(await service.SearchAsync(
            "The Imaginaries",
            TestContext.Current.CancellationToken));

        Assert.Equal("artist-1", artistTracks.ArtistId);
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
            });
    }

    [Theory]
    [InlineData(MediaEntityKind.Album)]
    [InlineData(MediaEntityKind.Track)]
    public async Task ExactMediaAmbiguityShouldKeepOrdinarySearchResults(
        MediaEntityKind conflictingKind)
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
            artistTracks);

        var succeeded = Assert.IsType<SearchSucceeded>(await service.SearchAsync(
            "Signals",
            TestContext.Current.CancellationToken));

        Assert.Null(artistTracks.ArtistId);
        Assert.Contains(
            succeeded.Results,
            candidate => candidate.Kind == conflictingKind
                && candidate.Title == "Signals");
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
    public async Task SearchShouldRejectWhitespaceWithoutCallingEitherResolver()
    {
        var catalogue = new StubCatalogueSearch([]);
        var playlists = new StubPlaylistSearch([]);
        var service = CreateService(
            catalogue,
            playlists,
            new ReferenceCodecTestContext().Search,
            new RecordingSearchObservationStore());

        var outcome = await service.SearchAsync("   ", TestContext.Current.CancellationToken);

        Assert.Equal(
            SearchRejectionReason.InvalidQuery,
            Assert.IsType<SearchRejected>(outcome).Reason);
        Assert.Null(catalogue.Query);
        Assert.Null(playlists.Query);
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
        Assert.Contains("Ratings", rejected.Message, StringComparison.Ordinal);
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
        ICatalogueArtistTrackResolver? artistTracks = null) => new(
            catalogue,
            artistTracks ?? new EmptyArtistTrackResolver(),
            playlists,
            codec,
            new SearchCandidateSelector(new Random(17)),
            new SearchObservationRecorder(
                observations,
                TimeProvider.System,
                NullLogger<SearchObservationRecorder>.Instance),
            NullLogger<SearchService>.Instance);

    private sealed class StubCatalogueSearch(
        IReadOnlyList<CatalogueSearchCandidate> results) : ICatalogueSearchResolver
    {
        public SearchResolverDescriptor Descriptor { get; } = new(
            "fictional-catalogue",
            "7");

        public string? Query { get; private set; }
        public RatingSearchConstraint? RatingConstraint { get; private set; }

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
    }

    private sealed class StubArtistTrackResolver(
        IReadOnlyList<CatalogueSearchCandidate> candidates) : ICatalogueArtistTrackResolver
    {
        public string? ArtistId { get; private set; }

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
