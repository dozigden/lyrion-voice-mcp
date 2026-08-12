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
        var codec = new SearchResultReferenceCodec();
        var service = new SearchService(
            lmsClient,
            codec,
            NullLogger<SearchService>.Instance);

        // Act
        var results = await service.SearchAsync(
            "  silver static  ",
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("silver static", lmsClient.Query);
        Assert.Equal(2, results.Count);
        Assert.NotEqual(results[0].Reference, results[1].Reference);
        Assert.Equal(identity, codec.Decode(results[0].Reference).Identity);
        Assert.Equal(identity, codec.Decode(results[1].Reference).Identity);
    }

    [Fact]
    public async Task SearchShouldRejectWhitespaceWithoutCallingLms()
    {
        // Arrange
        var lmsClient = new StubLmsSearchClient([]);
        var service = new SearchService(
            lmsClient,
            new SearchResultReferenceCodec(),
            NullLogger<SearchService>.Instance);

        // Act
        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            service.SearchAsync("   ", TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal("query", exception.ParamName);
        Assert.Null(lmsClient.Query);
    }

    private sealed class StubLmsSearchClient(
        IReadOnlyList<LmsSearchCandidate> results) : ILmsSearchClient
    {
        public string? Query { get; private set; }

        public Task<IReadOnlyList<LmsSearchCandidate>> SearchAsync(
            string query,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Query = query;
            return Task.FromResult(results);
        }
    }
}

public sealed class SearchResultReferenceCodecTests
{
    [Fact]
    public void CodecShouldRoundTripCorrelationAndMediaIdentityWithoutServerOrVersion()
    {
        // Arrange
        var codec = new SearchResultReferenceCodec();
        var expected = new SearchResultReferenceValue(
            "123456781234123412341234567890ab",
            new MediaIdentity(MediaEntityKind.Album, "204"));

        // Act
        var reference = codec.Encode(expected);
        var decoded = codec.Decode(reference);

        // Assert
        Assert.StartsWith("result_", reference, StringComparison.Ordinal);
        Assert.DoesNotContain("server", reference, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("version", reference, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(expected, decoded);
    }

    [Fact]
    public void DecodeShouldRejectMalformedReference()
    {
        // Arrange
        var codec = new SearchResultReferenceCodec();

        // Act
        var exception = Assert.Throws<FormatException>(() => codec.Decode("result_not-base64"));

        // Assert
        Assert.Equal("The search-result reference is invalid.", exception.Message);
    }
}
