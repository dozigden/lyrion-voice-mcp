using LyrionVoiceMcp.Abstractions;
using LyrionVoiceMcp.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace LyrionVoiceMcp.Services.Tests;

public sealed class CatalogueSearchAvailabilityServiceTests
{
    private const string Fallback = "The production catalogue search index has not been built.";
    private static readonly DateTimeOffset Now =
        DateTimeOffset.Parse("2026-08-27T08:00:00Z");

    [Fact]
    public async Task PendingCatalogueRefreshShouldDescribeCataloguePreparation()
    {
        var service = CreateService(catalogueJob: CreateJob(JobStatus.Pending));

        var message = await service.DescribeUnavailableAsync(
            Fallback,
            TestContext.Current.CancellationToken);

        Assert.Equal(
            "The music catalogue is being prepared; search will become available after indexing completes.",
            message);
    }

    [Fact]
    public async Task PendingIndexRebuildShouldDescribeIndexPreparation()
    {
        var service = CreateService(indexJob: CreateJob(JobStatus.Pending));

        var message = await service.DescribeUnavailableAsync(
            Fallback,
            TestContext.Current.CancellationToken);

        Assert.Equal(
            "The music catalogue has been imported and the search index is being prepared; try again later.",
            message);
    }

    [Fact]
    public async Task NoActivePreparationShouldPreserveTheResolverMessage()
    {
        var service = CreateService(indexJob: CreateJob(JobStatus.Failed));

        var message = await service.DescribeUnavailableAsync(
            Fallback,
            TestContext.Current.CancellationToken);

        Assert.Equal(Fallback, message);
    }

    [Fact]
    public async Task StatusFailureShouldPreserveTheResolverMessage()
    {
        var service = new CatalogueSearchAvailabilityService(
            new StubCatalogueLifecycleService(null),
            new FailingCatalogueRefreshService(),
            new StubSearchIndexService(null),
            NullLogger<CatalogueSearchAvailabilityService>.Instance);

        var message = await service.DescribeUnavailableAsync(
            Fallback,
            TestContext.Current.CancellationToken);

        Assert.Equal(Fallback, message);
    }

    private static CatalogueSearchAvailabilityService CreateService(
        CatalogueState? state = null,
        Job? catalogueJob = null,
        Job? indexJob = null) => new(
            new StubCatalogueLifecycleService(state),
            new StubCatalogueRefreshService(catalogueJob),
            new StubSearchIndexService(indexJob),
            NullLogger<CatalogueSearchAvailabilityService>.Instance);

    private static Job CreateJob(JobStatus status) => new(
        42,
        JobTypes.CatalogueRefresh,
        status,
        Now,
        "{}",
        "{}",
        status == JobStatus.Failed ? "Synthetic failure." : null,
        status == JobStatus.Pending ? null : Now,
        status is JobStatus.Pending or JobStatus.Running ? null : Now,
        "fictional:preparation:42",
        Now,
        Now);

    private sealed class StubCatalogueLifecycleService(CatalogueState? state)
        : ICatalogueLifecycleService
    {
        public Task<CatalogueState?> GetStateAsync(CancellationToken cancellationToken) =>
            Task.FromResult(state);

        public Task<CatalogueSummary?> GetSummaryAsync(CancellationToken cancellationToken) =>
            Task.FromResult(state?.Summary);

        public Task RecoverInterruptedRefreshAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task BeginRefreshAsync(
            string refreshId,
            DateTimeOffset startedAt,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<CatalogueRefreshCompletion> CompleteRefreshAsync(
            string refreshId,
            CatalogueSourceReadResult source,
            DateTimeOffset completedAt,
            int existingWarningCount,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task FinishRefreshAsync(
            string refreshId,
            CatalogueStateStatus status,
            DateTimeOffset completedAt,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class StubCatalogueRefreshService(Job? job) : ICatalogueRefreshService
    {
        public Task<CatalogueStatus> GetStatusAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new CatalogueStatus(null, job));

        public Task<CatalogueRefreshOutcome> RefreshAsync(
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<CatalogueRefreshOutcome> RefreshOnStartupAsync(
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class FailingCatalogueRefreshService : ICatalogueRefreshService
    {
        public Task<CatalogueStatus> GetStatusAsync(CancellationToken cancellationToken) =>
            Task.FromException<CatalogueStatus>(new InvalidOperationException(
                "Synthetic status failure."));

        public Task<CatalogueRefreshOutcome> RefreshAsync(
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<CatalogueRefreshOutcome> RefreshOnStartupAsync(
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class StubSearchIndexService(Job? job) : ISearchIndexService
    {
        public Task<SearchIndexStatus> GetAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new SearchIndexStatus("fictional-index", null, job));

        public Task<SearchIndexRebuildOutcome> RebuildAsync(
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<long?> EnqueueForCatalogueAsync(
            string catalogueRefreshId,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<long?> EnqueueForStartupAsync(
            string catalogueRefreshId,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
