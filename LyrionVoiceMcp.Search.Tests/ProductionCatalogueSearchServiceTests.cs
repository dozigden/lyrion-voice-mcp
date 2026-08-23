using LyrionVoiceMcp.Abstractions;

namespace LyrionVoiceMcp.Search.Tests;

public sealed class ProductionCatalogueSearchServiceTests : IAsyncLifetime
{
    private readonly string directory = Path.Combine(
        Path.GetTempPath(),
        $"lvm-search-tests-{Guid.NewGuid():N}");

    [Fact]
    public async Task SearchWithoutPublishedIndexShouldBeUnavailable()
    {
        await using var service = CreateService(new DocumentSource([]));

        await Assert.ThrowsAsync<CatalogueSearchUnavailableException>(() =>
            service.SearchAsync("fictional", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task RebuildShouldRequestBoundedBatchesAndPublishSearchableReferences()
    {
        var source = new DocumentSource([
            new CatalogueSearchDocument(
                new MediaIdentity(MediaEntityKind.Artist, "artist-5"),
                "Quartz 5",
                null,
                null),
            new CatalogueSearchDocument(
                new MediaIdentity(MediaEntityKind.Artist, "artist-heart"),
                "Quartz Heart",
                null,
                null)
        ]);
        await using var service = CreateService(source);

        var rebuilt = await service.RebuildAsync(
            "refresh-1",
            41,
            NullProgress.Instance,
            TestContext.Current.CancellationToken);
        var response = await service.SearchAsync(
            "quartz five",
            TestContext.Current.CancellationToken);

        Assert.Equal(500, source.RequestedBatchSize);
        Assert.Equal(2, rebuilt.Artifact.CandidateCount);
        Assert.Equal("artist-5", response.Candidates[0].Identity.Id);
        Assert.Equal(service.Descriptor.Name, rebuilt.Artifact.Resolver);
        Assert.Equal(service.Descriptor.Version, rebuilt.Artifact.ResolverVersion);

        var diagnostics = await service.SearchDetailedAsync(
            "quartz five",
            TestContext.Current.CancellationToken);
        Assert.Equal(service.Descriptor.Name, diagnostics.Resolver);
        Assert.Equal(service.Descriptor.Version, diagnostics.ResolverVersion);
        Assert.True(File.Exists(Path.Combine(directory, "current.json")));
    }

    [Fact]
    public async Task RebuildShouldPreserveNativeTrackRatingsWithoutChangingRanking()
    {
        var source = new DocumentSource([
            new CatalogueSearchDocument(
                new MediaIdentity(MediaEntityKind.Track, "rated-track"),
                "Fictional Rated Signal",
                "The Imaginaries",
                "Imaginary Signals",
                67),
            new CatalogueSearchDocument(
                new MediaIdentity(MediaEntityKind.Track, "unrated-track"),
                "Fictional Unrated Signal",
                "The Imaginaries",
                "Imaginary Signals")
        ]);
        await using var service = CreateService(source);

        var rebuilt = await service.RebuildAsync(
            "refresh-ratings",
            42,
            NullProgress.Instance,
            TestContext.Current.CancellationToken);
        var rated = await service.SearchAsync(
            "rated signal",
            TestContext.Current.CancellationToken);
        var unrated = await service.SearchAsync(
            "unrated signal",
            TestContext.Current.CancellationToken);

        Assert.Equal("5", rebuilt.Artifact.ResolverVersion);
        Assert.Equal(
            67,
            Assert.Single(rated.Candidates, candidate =>
                candidate.Identity.Id == "rated-track").NativeRating);
        Assert.Equal(0, Assert.Single(unrated.Candidates, candidate =>
            candidate.Identity.Id == "unrated-track").NativeRating);
        Assert.True(rated.Candidates[0].Score > 0);
        Assert.True(unrated.Candidates[0].Score > 0);
    }

    [Fact]
    public async Task RatingSearchShouldSupportExactAndAtLeastMatches()
    {
        var source = new DocumentSource([
            Track("zero", "Rating Signal Zero", 0),
            Track("four", "Rating Signal Four", 80),
            Track("four-half", "Rating Signal Four Half", 90),
            Track("five", "Rating Signal Five", 100),
            new CatalogueSearchDocument(
                new MediaIdentity(MediaEntityKind.Artist, "artist"),
                "Rating Signal Artist",
                null,
                null)
        ]);
        await using var service = CreateService(source);
        await RebuildAsync(service, "refresh-rating-search", 44);

        var exactFour = await service.SearchAsync(
            "rating signal",
            new RatingSearchConstraint(4m, RatingMatchMode.Exact),
            TestContext.Current.CancellationToken);
        var atLeastFour = await service.SearchAsync(
            "rating signal",
            new RatingSearchConstraint(4m, RatingMatchMode.AtLeast),
            TestContext.Current.CancellationToken);
        var exactZero = await service.SearchAsync(
            "rating signal",
            new RatingSearchConstraint(0m, RatingMatchMode.Exact),
            TestContext.Current.CancellationToken);
        var atLeastZero = await service.SearchAsync(
            "rating signal",
            new RatingSearchConstraint(0m, RatingMatchMode.AtLeast),
            TestContext.Current.CancellationToken);
        var diagnostics = await service.SearchDetailedAsync(
            "rating signal four half",
            new RatingSearchConstraint(4m, RatingMatchMode.AtLeast),
            TestContext.Current.CancellationToken);

        Assert.Equal(["four"], Ids(exactFour));
        Assert.Equal(["five", "four", "four-half"], Ids(atLeastFour).Order());
        Assert.Equal(["zero"], Ids(exactZero));
        Assert.Equal(
            ["five", "four", "four-half", "zero"],
            Ids(atLeastZero).Order());
        Assert.Equal(
            new RatingSearchConstraint(4m, RatingMatchMode.AtLeast),
            diagnostics.RatingConstraint);
        Assert.Equal(4.5m, diagnostics.Results[0].Rating);
    }

    [Fact]
    public async Task DecimalRatingSearchShouldRespectTheNativeIntegerScale()
    {
        var source = new DocumentSource([
            Track("ninety", "Decimal Rating Signal", 90),
            Track("ninety-one", "Decimal Rating Signal", 91)
        ]);
        await using var service = CreateService(source);
        await RebuildAsync(service, "refresh-decimal-search", 45);

        var exactRepresentable = await service.SearchAsync(
            "decimal rating signal",
            new RatingSearchConstraint(4.5m, RatingMatchMode.Exact),
            TestContext.Current.CancellationToken);
        var exactUnrepresentable = await service.SearchAsync(
            "decimal rating signal",
            new RatingSearchConstraint(4.51m, RatingMatchMode.Exact),
            TestContext.Current.CancellationToken);
        var atLeast = await service.SearchAsync(
            "decimal rating signal",
            new RatingSearchConstraint(4.51m, RatingMatchMode.AtLeast),
            TestContext.Current.CancellationToken);

        Assert.Equal(["ninety"], Ids(exactRepresentable));
        Assert.Empty(exactUnrepresentable.Candidates);
        Assert.Equal(["ninety-one"], Ids(atLeast));
    }

    [Fact]
    public async Task ProductionSearchShouldApplyIndependentResultLimitsByKind()
    {
        var documents = Enumerable.Range(1, 6)
            .Select(index => new CatalogueSearchDocument(
                new MediaIdentity(MediaEntityKind.Artist, $"artist-{index}"),
                $"Bounded Signal Artist {index}",
                null,
                null))
            .Concat(Enumerable.Range(1, 6).Select(index => new CatalogueSearchDocument(
                new MediaIdentity(MediaEntityKind.Album, $"album-{index}"),
                $"Bounded Signal Album {index}",
                "The Imaginaries",
                null)))
            .Concat(Enumerable.Range(1, 81).Select(index => new CatalogueSearchDocument(
                new MediaIdentity(MediaEntityKind.Track, $"track-{index}"),
                $"Bounded Signal Track {index}",
                "The Imaginaries",
                "Imaginary Signals")))
            .ToArray();
        await using var service = CreateService(new DocumentSource(documents));
        await RebuildAsync(service, "refresh-independent-limits", 48);

        var response = await service.SearchAsync(
            "bounded signal",
            TestContext.Current.CancellationToken);

        Assert.Equal(SearchResultPolicy.ArtistLimit, response.Candidates.Count(candidate =>
            candidate.Identity.Kind == MediaEntityKind.Artist));
        Assert.Equal(SearchResultPolicy.AlbumLimit, response.Candidates.Count(candidate =>
            candidate.Identity.Kind == MediaEntityKind.Album));
        Assert.InRange(
            response.Candidates.Count(candidate =>
                candidate.Identity.Kind == MediaEntityKind.Track),
            SearchResultPolicy.TrackLimit + 1,
            SearchResultPolicy.TrackCandidateLimit);
    }

    [Fact]
    public async Task PublishedIndexShouldStreamTracksByCanonicalArtistIdentity()
    {
        var source = new DocumentSource([
            new CatalogueSearchDocument(
                new MediaIdentity(MediaEntityKind.Track, "artist-track"),
                "An Unrelated Title",
                "The Imaginaries",
                "Imaginary Signals",
                90,
                ["artist-1"]),
            new CatalogueSearchDocument(
                new MediaIdentity(MediaEntityKind.Track, "other-track"),
                "Another Unrelated Title",
                "Another Artist",
                "Other Signals",
                100,
                ["artist-2"])
        ]);
        await using var service = CreateService(source);
        await RebuildAsync(service, "refresh-artist-tracks", 49);

        var candidates = new List<CatalogueSearchCandidate>();
        await foreach (var candidate in service.ReadArtistTracksAsync(
            "artist-1",
            TestContext.Current.CancellationToken))
        {
            candidates.Add(candidate);
        }

        var result = Assert.Single(candidates);
        Assert.Equal("artist-track", result.Identity.Id);
        Assert.Equal(90, result.NativeRating);
        Assert.Equal(1_120, result.Score);
    }

    [Fact]
    public async Task PublishedIndexShouldStreamOnlyAlbumsWithTheCanonicalAlbumArtistIdentity()
    {
        var source = new DocumentSource([
            new CatalogueSearchDocument(
                new MediaIdentity(MediaEntityKind.Album, "album-artist-album"),
                "Fictional Frequencies",
                "The Imaginaries",
                null,
                ArtistIds: ["artist-1"]),
            new CatalogueSearchDocument(
                new MediaIdentity(MediaEntityKind.Album, "guest-appearance-album"),
                "Various Fiction",
                "Various Artists",
                null,
                ArtistIds: ["artist-2"]),
            new CatalogueSearchDocument(
                new MediaIdentity(MediaEntityKind.Track, "guest-track"),
                "Imaginary Guest Signal",
                "The Imaginaries",
                "Various Fiction",
                ArtistIds: ["artist-1"])
        ]);
        await using var service = CreateService(source);
        await RebuildAsync(service, "refresh-artist-albums", 50);

        var candidates = new List<CatalogueSearchCandidate>();
        await foreach (var candidate in service.ReadArtistAlbumsAsync(
            "artist-1",
            TestContext.Current.CancellationToken))
        {
            candidates.Add(candidate);
        }

        var result = Assert.Single(candidates);
        Assert.Equal("album-artist-album", result.Identity.Id);
        Assert.Equal("The Imaginaries", result.Artist);
        Assert.Equal(1_300, result.Score);
    }

    [Fact]
    public async Task RatingConstraintShouldBeAppliedBeforeRetrievalLaneLimits()
    {
        var documents = Enumerable.Range(0, 81)
            .Select(index => Track(
                $"below-{index}",
                $"Copper Rating Signal {index}",
                79))
            .Append(Track("eligible", "Copper Rating Signal Eligible", 80))
            .ToArray();
        await using var service = CreateService(new DocumentSource(documents));
        await RebuildAsync(service, "refresh-rating-limits", 46);

        var response = await service.SearchAsync(
            "copper rating signal",
            new RatingSearchConstraint(4m, RatingMatchMode.AtLeast),
            TestContext.Current.CancellationToken);

        Assert.Equal(["eligible"], Ids(response));
    }

    [Fact]
    public async Task RatingBrowseShouldFloorBucketsAndOrderByNativeRatingThenTitle()
    {
        var source = new DocumentSource([
            Track("zero", "Zero", 0),
            Track("nineteen", "Nineteen", 19),
            Track("twenty", "Twenty", 20),
            Track("seventy-nine", "Zulu", 79),
            Track("seventy-eight-b", "Beta", 78),
            Track("seventy-eight-a", "Alpha", 78),
            Track("ninety-nine", "Ninety Nine", 99),
            Track("hundred", "Hundred", 100)
        ]);
        await using var service = CreateService(source);
        await RebuildAsync(service, "refresh-rating-browse", 47);

        var zero = await service.BrowseAsync(
            0,
            0,
            50,
            TestContext.Current.CancellationToken);
        var three = await service.BrowseAsync(
            3,
            0,
            2,
            TestContext.Current.CancellationToken);
        var threeContinued = await service.BrowseAsync(
            3,
            2,
            2,
            TestContext.Current.CancellationToken);
        var four = await service.BrowseAsync(
            4,
            0,
            50,
            TestContext.Current.CancellationToken);
        var five = await service.BrowseAsync(
            5,
            0,
            50,
            TestContext.Current.CancellationToken);

        Assert.Equal(["nineteen", "zero"], zero.Items.Select(item => item.Identity.Id));
        Assert.Equal(
            ["seventy-nine", "seventy-eight-a"],
            three.Items.Select(item => item.Identity.Id));
        Assert.True(three.HasMore);
        Assert.Equal(
            ["seventy-eight-b"],
            threeContinued.Items.Select(item => item.Identity.Id));
        Assert.False(threeContinued.HasMore);
        Assert.Equal(["ninety-nine"], four.Items.Select(item => item.Identity.Id));
        Assert.Equal(["hundred"], five.Items.Select(item => item.Identity.Id));
    }

    [Fact]
    public void NumericTokensShouldContributeSpokenPhoneticEvidence()
    {
        var numeric = PhuzzyText.DoubleMetaphoneCodes("quartz 5");
        var spoken = PhuzzyText.DoubleMetaphoneCodes("quartz five");
        var discarded = PhuzzyText.DoubleMetaphoneCodes("quartz");

        Assert.True(numeric.Overlaps(spoken));
        Assert.False(numeric.SetEquals(discarded));
    }

    [Fact]
    public async Task RebuildShouldKeepServingThePreviousPublishedGeneration()
    {
        var source = new SequencedSource();
        await using var service = CreateService(source);
        await service.RebuildAsync(
            "refresh-1",
            51,
            NullProgress.Instance,
            TestContext.Current.CancellationToken);

        var rebuild = service.RebuildAsync(
            "refresh-2",
            52,
            NullProgress.Instance,
            TestContext.Current.CancellationToken);
        await source.SecondBuildStarted.Task.WaitAsync(
            TestContext.Current.CancellationToken);
        var duringBuild = await service.SearchAsync(
            "amber signal",
            TestContext.Current.CancellationToken);
        source.ReleaseSecondBuild.TrySetResult();
        await rebuild;
        var afterBuild = await service.SearchAsync(
            "violet signal",
            TestContext.Current.CancellationToken);

        Assert.Equal("amber", duringBuild.Candidates[0].Identity.Id);
        Assert.Equal("violet", afterBuild.Candidates[0].Identity.Id);
    }

    [Fact]
    public async Task VersionFourArtifactShouldBeIncompatibleWithArtistAlbumExpansion()
    {
        var source = new DocumentSource([
            new CatalogueSearchDocument(
                new MediaIdentity(MediaEntityKind.Track, "rated-track"),
                "Fictional Rated Signal",
                "The Imaginaries",
                "Imaginary Signals",
                80)
        ]);
        await using (var service = CreateService(source))
        {
            await service.RebuildAsync(
                "refresh-ratings",
                43,
                NullProgress.Instance,
                TestContext.Current.CancellationToken);
        }

        var generationDirectory = Assert.Single(
            Directory.GetDirectories(directory, "generation-*"));
        var manifestPath = Path.Combine(generationDirectory, "manifest.json");
        var manifest = await File.ReadAllTextAsync(
            manifestPath,
            TestContext.Current.CancellationToken);
        Assert.Contains("\"resolverVersion\":\"5\"", manifest, StringComparison.Ordinal);
        await File.WriteAllTextAsync(
            manifestPath,
            manifest.Replace(
                "\"resolverVersion\":\"5\"",
                "\"resolverVersion\":\"4\"",
                StringComparison.Ordinal),
            TestContext.Current.CancellationToken);
        await using var restarted = CreateService(source);

        await Assert.ThrowsAsync<CatalogueSearchUnavailableException>(() =>
            restarted.SearchAsync("rated signal", TestContext.Current.CancellationToken));
    }

    public ValueTask InitializeAsync()
    {
        Directory.CreateDirectory(directory);
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }

        return ValueTask.CompletedTask;
    }

    private ProductionCatalogueSearchService CreateService(
        ICatalogueSearchDocumentSource source) => new(
            new ProductionSearchSettings(directory),
            source,
            TimeProvider.System);

    private static CatalogueSearchDocument Track(
        string id,
        string title,
        int nativeRating) => new(
            new MediaIdentity(MediaEntityKind.Track, id),
            title,
            "The Imaginaries",
            "Imaginary Signals",
            nativeRating);

    private static IEnumerable<string> Ids(CatalogueSearchResponse response) =>
        response.Candidates.Select(candidate => candidate.Identity.Id);

    private static Task<SearchIndexRebuildResult> RebuildAsync(
        ProductionCatalogueSearchService service,
        string refreshId,
        long jobId) =>
        service.RebuildAsync(
            refreshId,
            jobId,
            NullProgress.Instance,
            TestContext.Current.CancellationToken);

    private sealed class DocumentSource(
        IReadOnlyList<CatalogueSearchDocument> documents) : ICatalogueSearchDocumentSource
    {
        public int? RequestedBatchSize { get; private set; }

        public async IAsyncEnumerable<CatalogueSearchDocumentBatch> ReadBatchesAsync(
            string catalogueRefreshId,
            int batchSize,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            RequestedBatchSize = batchSize;
            for (var offset = 0; offset < documents.Count; offset += batchSize)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return new CatalogueSearchDocumentBatch(
                    catalogueRefreshId,
                    documents.Skip(offset).Take(batchSize).ToArray());
                await Task.Yield();
            }
        }
    }

    private sealed class NullProgress : ISearchIndexProgress
    {
        public static NullProgress Instance { get; } = new();

        public Task ReportAsync(
            string message,
            object? data,
            CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class SequencedSource : ICatalogueSearchDocumentSource
    {
        private int calls;

        public TaskCompletionSource SecondBuildStarted { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseSecondBuild { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public async IAsyncEnumerable<CatalogueSearchDocumentBatch> ReadBatchesAsync(
            string catalogueRefreshId,
            int batchSize,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            var call = Interlocked.Increment(ref calls);
            if (call == 2)
            {
                SecondBuildStarted.TrySetResult();
                await ReleaseSecondBuild.Task.WaitAsync(cancellationToken);
            }

            var identity = call == 1 ? "amber" : "violet";
            yield return new CatalogueSearchDocumentBatch(
                catalogueRefreshId,
                [new CatalogueSearchDocument(
                    new MediaIdentity(MediaEntityKind.Artist, identity),
                    $"{identity} signal",
                    null,
                    null)]);
        }
    }
}
