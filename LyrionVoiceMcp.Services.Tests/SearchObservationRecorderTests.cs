using LyrionVoiceMcp.Abstractions;
using Microsoft.Extensions.Logging.Abstractions;

namespace LyrionVoiceMcp.Services.Tests;

public sealed class SearchObservationRecorderTests
{
    private static readonly DateTimeOffset Now = new(
        2026,
        8,
        17,
        20,
        30,
        0,
        TimeSpan.Zero);

    [Fact]
    public async Task CompletedSearchShouldRetainResolverSourcesTimingsAndCandidates()
    {
        var store = new RecordingSearchObservationStore();
        var recorder = new SearchObservationRecorder(
            store,
            new FixedTimeProvider(Now),
            NullLogger<SearchObservationRecorder>.Instance);
        var descriptor = new SearchResolverDescriptor("fictional-resolver", "7");
        var context = recorder.Begin("  copper  ", "copper", descriptor);
        var catalogueRequests = new LmsSearchRequestObservation[]
        {
            new(
                "catalogue-index",
                "search:unconstrained",
                LmsSearchRequestStatus.Completed,
                null,
                10,
                1),
            new(
                "catalogue-artist-tracks",
                "artist-tracks",
                LmsSearchRequestStatus.Completed,
                null,
                5,
                30),
            new(
                "catalogue-artist-albums",
                "artist-albums",
                LmsSearchRequestStatus.Completed,
                null,
                3,
                12)
        };
        var playlists = new LmsSearchResponse(
            [],
            [new LmsSearchRequestObservation(
                "playlists",
                "[\"playlists\"]",
                LmsSearchRequestStatus.Completed,
                null,
                12,
                0)],
            12);
        var candidates = new SearchCandidateOccurrence[]
        {
            new(
                1,
                "candidate-correlation",
                new MediaIdentity(MediaEntityKind.Artist, "artist-7"),
                "Copper Lines",
                null,
                null,
                IsExactArtistMatch: true,
                MatchSignal: "roman_cardinal_equivalent")
        };

        await recorder.RecordCompletedAsync(
            context,
            catalogueRequests,
            playlists,
            candidates,
            20,
            TestContext.Current.CancellationToken);

        var recorded = Assert.IsType<SearchObservation>(store.Recorded);
        Assert.Equal(Now, recorded.CreatedAt);
        Assert.Equal("  copper  ", recorded.OriginalQuery);
        Assert.Equal("copper", recorded.NormalisedQuery);
        Assert.Equal(descriptor.Name, recorded.Resolver);
        Assert.Equal(descriptor.Version, recorded.ResolverVersion);
        Assert.Equal(SearchObservationInterpretation.Named, recorded.Interpretation);
        Assert.Equal(SearchObservationStatus.Completed, recorded.Status);
        Assert.Equal(20, recorded.TotalDurationMilliseconds);
        Assert.Equal(20, recorded.RetrievalDurationMilliseconds);
        Assert.Equal(0, recorded.ProcessingDurationMilliseconds);
        Assert.Collection(
            recorded.Requests,
            request =>
            {
                Assert.Equal("catalogue-index", request.Source);
                Assert.Equal("search:unconstrained", request.Command);
                Assert.Equal(1, request.ResultCount);
            },
            request =>
            {
                Assert.Equal("catalogue-artist-tracks", request.Source);
                Assert.Equal(30, request.ResultCount);
            },
            request =>
            {
                Assert.Equal("catalogue-artist-albums", request.Source);
                Assert.Equal(12, request.ResultCount);
            },
            request => Assert.Equal("playlists", request.Source));
        var candidate = Assert.Single(recorded.Candidates);
        Assert.Equal("candidate-correlation", candidate.CorrelationId);
        Assert.Equal("Copper Lines", candidate.Title);
        Assert.True(candidate.IsExactArtistMatch);
        Assert.Equal("roman_cardinal_equivalent", candidate.MatchSignal);
    }

    [Fact]
    public async Task CatalogueFailureShouldRetainCompletedPlaylistEvidence()
    {
        var store = new RecordingSearchObservationStore();
        var recorder = new SearchObservationRecorder(
            store,
            new FixedTimeProvider(Now),
            NullLogger<SearchObservationRecorder>.Instance);
        var descriptor = new SearchResolverDescriptor("fictional-resolver", "7");
        var context = recorder.Begin("copper", "copper", descriptor);
        var playlists = new LmsSearchResponse(
            [new LmsSearchCandidate(
                new MediaIdentity(MediaEntityKind.Playlist, "playlist-9"),
                "Copper Evenings",
                null,
                null)],
            [new LmsSearchRequestObservation(
                "playlists",
                "[\"playlists\"]",
                LmsSearchRequestStatus.Completed,
                null,
                12,
                1)],
            12);

        await recorder.RecordCatalogueFailureAsync(
            context,
            [new LmsSearchRequestObservation(
                "catalogue-index",
                "search:unconstrained",
                LmsSearchRequestStatus.Failed,
                "Synthetic catalogue failure.",
                15,
                0)],
            new InvalidOperationException("Synthetic catalogue failure."),
            15,
            playlists,
            TestContext.Current.CancellationToken);

        var recorded = Assert.IsType<SearchObservation>(store.Recorded);
        Assert.Equal(SearchObservationStatus.Failed, recorded.Status);
        Assert.Equal("Synthetic catalogue failure.", recorded.FailureMessage);
        Assert.Equal(15, recorded.RetrievalDurationMilliseconds);
        Assert.Equal(0, recorded.ProcessingDurationMilliseconds);
        Assert.Collection(
            recorded.Requests,
            request =>
            {
                Assert.Equal("catalogue-index", request.Source);
                Assert.Equal(LmsSearchRequestStatus.Failed, request.Status);
            },
            request =>
            {
                Assert.Equal("playlists", request.Source);
                Assert.Equal(LmsSearchRequestStatus.Completed, request.Status);
            });
        Assert.Equal("Copper Evenings", Assert.Single(recorded.Candidates).Title);
    }

    [Fact]
    public async Task NameFreeSearchShouldRecordTheSlowerConcurrentRetrievalDuration()
    {
        var store = new RecordingSearchObservationStore();
        var recorder = new SearchObservationRecorder(
            store,
            new FixedTimeProvider(Now),
            NullLogger<SearchObservationRecorder>.Instance);
        var context = recorder.Begin(
            string.Empty,
            string.Empty,
            new SearchResolverDescriptor("fictional-resolver", "7"),
            yearRange: new YearSearchRange(1990, 1999, 1990, 1999),
            interpretation: SearchObservationInterpretation.NameFreeFiltered,
            includesAlbums: true);
        var catalogueRequests = new LmsSearchRequestObservation[]
        {
            new(
                "catalogue-filtered-tracks",
                "filtered-tracks",
                LmsSearchRequestStatus.Completed,
                null,
                40,
                10),
            new(
                "catalogue-filtered-albums",
                "filtered-albums",
                LmsSearchRequestStatus.Completed,
                null,
                70,
                5)
        };

        await recorder.RecordCompletedAsync(
            context,
            catalogueRequests,
            null,
            [],
            100,
            TestContext.Current.CancellationToken);

        var recorded = Assert.IsType<SearchObservation>(store.Recorded);
        Assert.Equal(70, recorded.RetrievalDurationMilliseconds);
        Assert.Equal(30, recorded.ProcessingDurationMilliseconds);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
