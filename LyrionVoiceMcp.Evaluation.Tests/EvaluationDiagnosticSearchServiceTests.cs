namespace LyrionVoiceMcp.Evaluation.Tests;

public sealed class EvaluationDiagnosticSearchServiceTests
{
    [Fact]
    public async Task SearchAsync_serialises_complete_measurement_windows()
    {
        var resolver = new BlockingDiagnosticResolver();
        var provider = new StubResolverProvider(resolver);
        await using var service = new EvaluationDiagnosticSearchService(provider);
        var request = new EvaluationDiagnosticSearchRequest("test", "query");

        var first = service.SearchAsync(request, TestContext.Current.CancellationToken);
        Assert.Equal(1, resolver.EntryCount);
        var second = service.SearchAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(1, resolver.EntryCount);
        resolver.ReleaseFirst();
        await Task.WhenAll(first, second);
        Assert.Equal(2, resolver.EntryCount);
        Assert.Equal(1, resolver.MaximumConcurrency);
    }

    [Fact]
    public async Task DisposeAsync_disposes_the_resolver_provider()
    {
        var provider = new StubResolverProvider(new BlockingDiagnosticResolver());
        var service = new EvaluationDiagnosticSearchService(provider);

        await service.DisposeAsync();

        Assert.True(provider.Disposed);
    }

    [Fact]
    public void Settings_default_the_index_beside_the_deployed_catalogue()
    {
        var root = Path.Combine(Path.GetTempPath(), "lyrion-api");
        var cataloguePath = Path.Combine(root, "data", "catalogue.db");

        var settings = EvaluationDiagnosticSettings.FromValues(
            root,
            cataloguePath,
            null);

        Assert.Equal(cataloguePath, settings.CataloguePath);
        Assert.Equal(Path.Combine(root, "data", "search-indexes"), settings.IndexDirectoryPath);
    }

    [Fact]
    public void Validation_rejects_an_unknown_resolver()
    {
        var request = new EvaluationDiagnosticSearchRequest("not-a-resolver", "Nite");

        var error = EvaluationDiagnosticSearchValidation.Validate(request);

        Assert.Contains("resolver", error, StringComparison.Ordinal);
    }

    private sealed class StubResolverProvider(IEvaluationDiagnosticSearchResolver resolver)
        : IEvaluationDiagnosticResolverProvider
    {
        public bool Disposed { get; private set; }

        public Task<ResolvedDiagnosticResolver> GetAsync(
            string name,
            CancellationToken cancellationToken) =>
            Task.FromResult(new ResolvedDiagnosticResolver(resolver, false));

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class BlockingDiagnosticResolver : IEvaluationDiagnosticSearchResolver
    {
        private readonly TaskCompletionSource releaseFirst = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private int activeCount;
        private int entryCount;
        private int maximumConcurrency;

        public string Name => "test";
        public string Version => "1";
        public EvaluationResolverMetrics Metrics { get; } = new(0, 0, 0);
        public int EntryCount => entryCount;
        public int MaximumConcurrency => maximumConcurrency;

        public Task<EvaluationSearchResponse> SearchAsync(
            string query,
            CancellationToken cancellationToken) =>
            Task.FromResult(new EvaluationSearchResponse([], null));

        public async Task<EvaluationDiagnosticSearchResponse> SearchDetailedAsync(
            string query,
            CancellationToken cancellationToken)
        {
            var entry = Interlocked.Increment(ref entryCount);
            var active = Interlocked.Increment(ref activeCount);
            InterlockedMax(ref maximumConcurrency, active);
            try
            {
                if (entry == 1)
                {
                    await releaseFirst.Task.WaitAsync(cancellationToken);
                }

                return new EvaluationDiagnosticSearchResponse(
                    Name,
                    Version,
                    Metrics,
                    0,
                    0,
                    0,
                    0,
                    [],
                    []);
            }
            finally
            {
                Interlocked.Decrement(ref activeCount);
            }
        }

        public void ReleaseFirst() => releaseFirst.SetResult();

        private static void InterlockedMax(ref int location, int value)
        {
            var current = Volatile.Read(ref location);
            while (value > current)
            {
                var observed = Interlocked.CompareExchange(ref location, value, current);
                if (observed == current)
                {
                    return;
                }

                current = observed;
            }
        }
    }
}
