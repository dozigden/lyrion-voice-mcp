using LyrionVoiceMcp.Abstractions;
using LyrionVoiceMcp.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace LyrionVoiceMcp.Services.Tests;

public sealed class SearchServiceTests
{
    [Fact]
    public async Task SearchShouldTrimQueryAndCreateDistinctOccurrenceReferences()
    {
        // Arrange
        var identity = new MediaIdentity(MediaEntityKind.Track, "51");
        var lmsClient = new StubLmsSearchClient(
            [
                new LmsSearchCandidate(identity, "Silver Static", "The Copper Lines", "Night Signals"),
                new LmsSearchCandidate(identity, "Silver Static", "The Copper Lines", "Night Signals")
            ]);
        var codec = new ReferenceCodecTestContext().Search;
        var service = new SearchService(
            lmsClient,
            codec,
            NullLogger<SearchService>.Instance);

        // Act
        var outcome = await service.SearchAsync(
            "  silver static  ",
            TestContext.Current.CancellationToken);

        // Assert
        var results = Assert.IsType<SearchSucceeded>(outcome).Results;
        Assert.Equal("silver static", lmsClient.Query);
        Assert.Equal(2, results.Count);
        Assert.NotEqual(results[0].Reference, results[1].Reference);
        Assert.Equal(identity, codec.TryDecode(results[0].Reference)?.Identity);
        Assert.Equal(identity, codec.TryDecode(results[1].Reference)?.Identity);
    }

    [Fact]
    public async Task SearchShouldRejectWhitespaceWithoutCallingLms()
    {
        // Arrange
        var lmsClient = new StubLmsSearchClient([]);
        var service = new SearchService(
            lmsClient,
            new ReferenceCodecTestContext().Search,
            NullLogger<SearchService>.Instance);

        // Act
        var outcome = await service.SearchAsync(
            "   ",
            TestContext.Current.CancellationToken);

        // Assert
        var rejection = Assert.IsType<SearchRejected>(outcome);
        Assert.Equal(SearchRejectionReason.InvalidQuery, rejection.Reason);
        Assert.Equal("The search query must not be empty.", rejection.Message);
        Assert.Null(lmsClient.Query);
    }

    [Fact]
    public async Task SearchShouldRecordOriginalQueryAndOrderedCorrelations()
    {
        // Arrange
        var store = new RecordingSearchObservationStore();
        var client = new StubLmsSearchClient([
            new LmsSearchCandidate(new MediaIdentity(MediaEntityKind.Artist, "7"), "ZYRAQ", null, null)
        ]);
        var service = new SearchService(
            client,
            new ReferenceCodecTestContext().Search,
            store,
            TimeProvider.System,
            NullLogger<SearchService>.Instance);

        // Act
        await service.SearchAsync("  zyrack  ", TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("  zyrack  ", store.Recorded?.OriginalQuery);
        Assert.Equal("zyrack", store.Recorded?.NormalisedQuery);
        Assert.Equal("lms-pass-through", store.Recorded?.Resolver);
        Assert.Equal(1, Assert.Single(store.Recorded!.Candidates).Position);
    }

    [Fact]
    public async Task FailedSearchShouldPreservePerRequestEvidenceAndRecoveredCandidates()
    {
        // Arrange
        var store = new RecordingSearchObservationStore();
        var response = new LmsSearchResponse(
            [new LmsSearchCandidate(new MediaIdentity(MediaEntityKind.Playlist, "9"), "Morning Signals", null, null)],
            [
                new LmsSearchRequestObservation(
                    "library", "[\"search\"]", LmsSearchRequestStatus.Failed, "Synthetic failure.", 8, 0),
                new LmsSearchRequestObservation(
                    "playlists", "[\"playlists\"]", LmsSearchRequestStatus.Completed, null, 4, 1)
            ],
            8);
        var service = new SearchService(
            new FailingLmsSearchClient(response),
            new ReferenceCodecTestContext().Search,
            store,
            TimeProvider.System,
            NullLogger<SearchService>.Instance);

        // Act
        await Assert.ThrowsAsync<LmsSearchFailedException>(() =>
            service.SearchAsync("signals", TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(SearchObservationStatus.Failed, store.Recorded?.Status);
        Assert.Equal(2, store.Recorded?.Requests.Count);
        Assert.Equal("Morning Signals", Assert.Single(store.Recorded!.Candidates).Title);
    }

    private sealed class StubLmsSearchClient(
        IReadOnlyList<LmsSearchCandidate> results) : ILmsSearchClient
    {
        public string? Query { get; private set; }

        public Task<LmsSearchResponse> SearchAsync(
            string query,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Query = query;
            return Task.FromResult(new LmsSearchResponse(results, [], 0));
        }
    }

    private sealed class FailingLmsSearchClient(LmsSearchResponse response) : ILmsSearchClient
    {
        public Task<LmsSearchResponse> SearchAsync(string query, CancellationToken cancellationToken) =>
            Task.FromException<LmsSearchResponse>(new LmsSearchFailedException(
                "LMS search failed for library.",
                response,
                new LmsRequestException("Synthetic failure.")));
    }
}

public sealed class SearchResultReferenceCodecTests
{
    [Fact]
    public void CodecShouldRoundTripCorrelationAndMediaIdentityWithoutServerOrVersion()
    {
        // Arrange
        var codec = new ReferenceCodecTestContext().Search;
        var expected = new SearchResultReferenceValue(
            "123456781234123412341234567890ab",
            new MediaIdentity(MediaEntityKind.Album, "204"));

        // Act
        var reference = codec.Encode(expected);
        var decoded = codec.TryDecode(reference);

        // Assert
        Assert.StartsWith("result_", reference, StringComparison.Ordinal);
        Assert.Equal(23, reference.Length);
        Assert.Equal(expected, decoded);
    }

    [Fact]
    public void TryDecodeShouldReturnNullForMalformedReference()
    {
        // Arrange
        var codec = new ReferenceCodecTestContext().Search;

        // Act
        var decoded = codec.TryDecode("result_not-base64");

        // Assert
        Assert.Null(decoded);
    }
}
