using LyrionVoiceMcp.Abstractions;
using LyrionVoiceMcp.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace LyrionVoiceMcp.Services.Tests;

public sealed class StartupReadinessServiceTests
{
    private static readonly DateTimeOffset Now =
        DateTimeOffset.Parse("2026-08-23T10:00:00Z");

    [Fact]
    public async Task MissingCatalogueShouldRemainUnbuiltWhenSourceIsNotConfigured()
    {
        // Arrange
        var refresh = new RecordingCatalogueRefreshService();
        var indexes = new RecordingSearchIndexService();
        var service = CreateService(null, refresh, indexes, sourceConfigured: false);

        // Act
        await service.CheckAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(0, refresh.StartupRefreshCount);
        Assert.Null(indexes.StartupCatalogueRefreshId);
    }

    [Fact]
    public async Task MissingCatalogueShouldQueueRefreshWhenSourceIsConfigured()
    {
        // Arrange
        var refresh = new RecordingCatalogueRefreshService();
        var indexes = new RecordingSearchIndexService();
        var service = CreateService(null, refresh, indexes, sourceConfigured: true);

        // Act
        await service.CheckAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(1, refresh.StartupRefreshCount);
        Assert.Null(indexes.StartupCatalogueRefreshId);
    }

    [Fact]
    public async Task MatchingCatalogueAndIndexShouldRequireNoWork()
    {
        // Arrange
        var state = CreateCatalogueState("job-42");
        var refresh = new RecordingCatalogueRefreshService();
        var indexes = new RecordingSearchIndexService(CreateArtifact("job-42"));
        var service = CreateService(state, refresh, indexes, sourceConfigured: true);

        // Act
        await service.CheckAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(0, refresh.StartupRefreshCount);
        Assert.Null(indexes.StartupCatalogueRefreshId);
    }

    [Fact]
    public async Task MissingIndexShouldQueueRecoveryForSuccessfulCatalogue()
    {
        // Arrange
        var state = CreateCatalogueState("job-42");
        var refresh = new RecordingCatalogueRefreshService();
        var indexes = new RecordingSearchIndexService();
        var service = CreateService(state, refresh, indexes, sourceConfigured: true);

        // Act
        await service.CheckAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("job-42", indexes.StartupCatalogueRefreshId);
        Assert.Equal(0, refresh.StartupRefreshCount);
    }

    [Fact]
    public async Task IndexForOlderCatalogueShouldQueueRecoveryForCurrentCatalogue()
    {
        // Arrange
        var state = CreateCatalogueState("job-42");
        var refresh = new RecordingCatalogueRefreshService();
        var indexes = new RecordingSearchIndexService(CreateArtifact("job-37"));
        var service = CreateService(state, refresh, indexes, sourceConfigured: true);

        // Act
        await service.CheckAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("job-42", indexes.StartupCatalogueRefreshId);
    }

    [Theory]
    [InlineData(CatalogueStateStatus.Failed)]
    [InlineData(CatalogueStateStatus.Cancelled)]
    [InlineData(CatalogueStateStatus.Interrupted)]
    public async Task UnsuccessfulCatalogueShouldBeRebuiltWhenSourceIsConfigured(
        CatalogueStateStatus status)
    {
        // Arrange
        var state = new CatalogueState(
            "job-42",
            status,
            Now,
            Now,
            null);
        var refresh = new RecordingCatalogueRefreshService();
        var indexes = new RecordingSearchIndexService();
        var service = CreateService(state, refresh, indexes, sourceConfigured: true);

        // Act
        await service.CheckAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(1, refresh.StartupRefreshCount);
        Assert.Null(indexes.StartupCatalogueRefreshId);
    }

    private static StartupReadinessService CreateService(
        CatalogueState? state,
        RecordingCatalogueRefreshService refresh,
        RecordingSearchIndexService indexes,
        bool sourceConfigured) => new(
            new StubCatalogueLifecycleService(state),
            refresh,
            indexes,
            new CatalogueInitialisationPolicy(sourceConfigured),
            NullLogger<StartupReadinessService>.Instance);

    private static CatalogueState CreateCatalogueState(string refreshId) => new(
        refreshId,
        CatalogueStateStatus.Succeeded,
        Now,
        Now,
        new CatalogueSummary(
            "fictional",
            "lms",
            "revision-1",
            "9.1.2",
            Now,
            null,
            Now,
            2,
            3,
            4,
            20,
            1,
            0));

    private static SearchIndexArtifact CreateArtifact(string refreshId) => new(
        "fictional-index",
        "1",
        refreshId,
        Now,
        29,
        50,
        1024);

    private static Job CreateJob(JobStatus status) => new(
        101,
        JobTypes.CatalogueRefresh,
        status,
        Now,
        "{}",
        "{}",
        null,
        status == JobStatus.Pending ? null : Now,
        null,
        "startup:catalogue.refresh:test",
        Now,
        Now);

    private sealed class StubCatalogueLifecycleService(CatalogueState? state)
        : ICatalogueLifecycleService
    {
        public Task RecoverInterruptedRefreshAsync(CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task<CatalogueState?> GetStateAsync(CancellationToken cancellationToken) =>
            Task.FromResult(state);

        public Task<CatalogueSummary?> GetSummaryAsync(CancellationToken cancellationToken) =>
            Task.FromResult(state?.Summary);

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

    private sealed class RecordingCatalogueRefreshService(Job? latestRefresh = null)
        : ICatalogueRefreshService
    {
        public int StartupRefreshCount { get; private set; }

        public Task<CatalogueStatus> GetStatusAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new CatalogueStatus(null, latestRefresh));

        public Task<CatalogueRefreshOutcome> RefreshAsync(
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<CatalogueRefreshOutcome> RefreshOnStartupAsync(
            CancellationToken cancellationToken)
        {
            StartupRefreshCount++;
            return Task.FromResult<CatalogueRefreshOutcome>(new CatalogueRefreshStarted(
                new CatalogueStatus(null, CreateJob(JobStatus.Pending))));
        }
    }

    private sealed class RecordingSearchIndexService(SearchIndexArtifact? artifact = null)
        : ISearchIndexService
    {
        public string? StartupCatalogueRefreshId { get; private set; }

        public Task<SearchIndexStatus> GetAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new SearchIndexStatus("fictional-index", artifact, null));

        public Task<SearchIndexRebuildOutcome> RebuildAsync(
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<long?> EnqueueForCatalogueAsync(
            string catalogueRefreshId,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<long?> EnqueueForStartupAsync(
            string catalogueRefreshId,
            CancellationToken cancellationToken)
        {
            StartupCatalogueRefreshId = catalogueRefreshId;
            return Task.FromResult<long?>(102);
        }
    }
}
