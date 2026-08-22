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

        Assert.Equal("2", rebuilt.Artifact.ResolverVersion);
        Assert.Equal(
            67,
            Assert.Single(rated.Candidates, candidate =>
                candidate.Identity.Id == "rated-track").NativeRating);
        Assert.Null(Assert.Single(unrated.Candidates, candidate =>
            candidate.Identity.Id == "unrated-track").NativeRating);
        Assert.True(rated.Candidates[0].Score > 0);
        Assert.True(unrated.Candidates[0].Score > 0);
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
    public async Task VersionOneArtifactShouldBeIncompatibleWithTheRatingIndex()
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
        Assert.Contains("\"resolverVersion\":\"2\"", manifest, StringComparison.Ordinal);
        await File.WriteAllTextAsync(
            manifestPath,
            manifest.Replace(
                "\"resolverVersion\":\"2\"",
                "\"resolverVersion\":\"1\"",
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
