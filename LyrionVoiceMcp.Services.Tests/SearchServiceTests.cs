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
        var service = CreateService(catalogue, playlists, codec, new RecordingSearchObservationStore());

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
        Assert.Equal("7", codec.TryDecode(results[0].Reference)?.Identity.Id);
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
    }

    private static SearchService CreateService(
        ICatalogueSearchResolver catalogue,
        ILmsPlaylistSearchClient playlists,
        ISearchResultReferenceCodec codec,
        ISearchObservationStore observations) => new(
            catalogue,
            playlists,
            codec,
            observations,
            TimeProvider.System,
            NullLogger<SearchService>.Instance);

    private sealed class StubCatalogueSearch(
        IReadOnlyList<CatalogueSearchCandidate> results) : ICatalogueSearchResolver
    {
        public SearchResolverDescriptor Descriptor { get; } = new(
            "fictional-catalogue",
            "7");

        public string? Query { get; private set; }

        public Task<CatalogueSearchResponse> SearchAsync(
            string query,
            CancellationToken cancellationToken)
        {
            Query = query;
            return Task.FromResult(new CatalogueSearchResponse(results, 1, 1));
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
}

public sealed class SearchResultReferenceCodecTests
{
    [Fact]
    public void CodecShouldRoundTripCorrelationAndMediaIdentityWithoutServerOrVersion()
    {
        var codec = new ReferenceCodecTestContext().Search;
        var expected = new SearchResultReferenceValue(
            "123456781234123412341234567890ab",
            new MediaIdentity(MediaEntityKind.Album, "204"));

        var reference = codec.Encode(expected);
        var decoded = codec.TryDecode(reference);

        Assert.StartsWith("result_", reference, StringComparison.Ordinal);
        Assert.Equal(23, reference.Length);
        Assert.Equal(expected, decoded);
    }

    [Fact]
    public void TryDecodeShouldReturnNullForMalformedReference()
    {
        var decoded = new ReferenceCodecTestContext().Search.TryDecode("result_not-base64");

        Assert.Null(decoded);
    }
}
