using LyrionVoiceMcp.Abstractions;
using LyrionVoiceMcp.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace LyrionVoiceMcp.Services.Tests;

public sealed class CatalogueRefreshServiceTests
{
    [Fact]
    public async Task ConcurrentRefreshShouldBeRejectedWhileTheFirstRefreshContinues()
    {
        // Arrange
        var reader = new BlockingCatalogueReader(CreateSnapshot());
        var store = new RecordingCatalogueStore();
        await using var provider = CreateProvider(reader);
        var service = new CatalogueRefreshService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            store,
            new AdvancingTimeProvider(),
            NullLogger<CatalogueRefreshService>.Instance);
        await service.StartAsync(TestContext.Current.CancellationToken);

        // Act
        var firstOutcome = await service.RefreshAsync(TestContext.Current.CancellationToken);
        await reader.Started.Task.WaitAsync(TestContext.Current.CancellationToken);
        var secondOutcome = await service.RefreshAsync(TestContext.Current.CancellationToken);
        reader.Release.SetResult();
        await store.Completed.Task.WaitAsync(TestContext.Current.CancellationToken);
        await service.StopAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.IsType<CatalogueRefreshAlreadyRunning>(secondOutcome);
        Assert.IsType<CatalogueRefreshStarted>(firstOutcome);
        Assert.Equal(1, reader.ReadCount);
        Assert.Equal(CatalogueRefreshRunStatus.Succeeded, store.LatestRefresh?.Status);
    }

    [Fact]
    public async Task FailedRefreshShouldRetainThePublishedGenerationAndRecordSanitisedStatus()
    {
        // Arrange
        var existing = new PublishedCatalogueGeneration(
            "generation-1", "development", "revision-1", "9.1.2", DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow,
            1, 1, 1, 1, 1, 0);
        var store = new RecordingCatalogueStore { PublishedGeneration = existing };
        await using var provider = CreateProvider(new FailingCatalogueReader());
        var service = new CatalogueRefreshService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            store,
            new AdvancingTimeProvider(),
            NullLogger<CatalogueRefreshService>.Instance);
        await service.StartAsync(TestContext.Current.CancellationToken);

        // Act
        var outcome = await service.RefreshAsync(TestContext.Current.CancellationToken);
        await store.Completed.Task.WaitAsync(TestContext.Current.CancellationToken);
        var status = await service.GetStatusAsync(TestContext.Current.CancellationToken);
        await service.StopAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.IsType<CatalogueRefreshStarted>(outcome);
        Assert.Equal(existing, status.PublishedGeneration);
        Assert.Equal(CatalogueRefreshRunStatus.Failed, status.LatestRefresh?.Status);
        Assert.Equal(
            "Catalogue refresh failed. See the service logs for details.",
            status.LatestRefresh?.FailureMessage);
        Assert.DoesNotContain("private detail", status.LatestRefresh?.FailureMessage, StringComparison.Ordinal);
    }

    private static CatalogueImportSnapshot CreateSnapshot() => new(
        new CatalogueImportSource("development", "lms", "9.1.2", "revision-2"),
        DateTimeOffset.UtcNow,
        null,
        [],
        [],
        [],
        [],
        [],
        []);

    private static ServiceProvider CreateProvider(ICatalogueSourceReader reader)
    {
        var services = new ServiceCollection();
        services.AddSingleton(reader);
        return services.BuildServiceProvider();
    }

    private sealed class BlockingCatalogueReader(CatalogueImportSnapshot snapshot) : ICatalogueSourceReader
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int ReadCount { get; private set; }

        public async Task<CatalogueImportSnapshot> ReadAsync(CancellationToken cancellationToken)
        {
            ReadCount++;
            Started.SetResult();
            await Release.Task.WaitAsync(cancellationToken);
            return snapshot;
        }
    }

    private sealed class FailingCatalogueReader : ICatalogueSourceReader
    {
        public Task<CatalogueImportSnapshot> ReadAsync(CancellationToken cancellationToken) =>
            Task.FromException<CatalogueImportSnapshot>(
                new InvalidOperationException("private detail from an upstream response"));
    }

    private sealed class RecordingCatalogueStore : IMediaCatalogueStore
    {
        public TaskCompletionSource Completed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public PublishedCatalogueGeneration? PublishedGeneration { get; set; }
        public CatalogueRefreshRun? LatestRefresh { get; private set; }

        public Task InitialiseAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<PublishedCatalogueGeneration?> GetPublishedGenerationAsync(
            CancellationToken cancellationToken) => Task.FromResult(PublishedGeneration);

        public Task<CatalogueRefreshRun?> GetLatestRefreshRunAsync(
            CancellationToken cancellationToken) => Task.FromResult(LatestRefresh);

        public Task BeginRefreshAsync(
            string refreshId,
            DateTimeOffset startedAt,
            CancellationToken cancellationToken)
        {
            LatestRefresh = new CatalogueRefreshRun(
                refreshId,
                CatalogueRefreshRunStatus.Running,
                startedAt,
                null,
                null,
                null,
                null);
            return Task.CompletedTask;
        }

        public Task<PublishedCatalogueGeneration> PublishAsync(
            CatalogueImportSnapshot snapshot,
            string refreshId,
            DateTimeOffset completedAt,
            long durationMilliseconds,
            CancellationToken cancellationToken)
        {
            PublishedGeneration = new PublishedCatalogueGeneration(
                "generation-2",
                snapshot.Source.Id,
                snapshot.Source.Revision,
                snapshot.Source.Version,
                snapshot.CapturedAt,
                snapshot.SourceLastScanAt,
                completedAt,
                snapshot.Artists.Count,
                snapshot.Albums.Count,
                snapshot.Genres.Count,
                snapshot.Tracks.Count,
                snapshot.VirtualLibraries.Count,
                snapshot.Warnings.Count);
            LatestRefresh = LatestRefresh! with
            {
                Status = CatalogueRefreshRunStatus.Succeeded,
                CompletedAt = completedAt,
                DurationMilliseconds = durationMilliseconds,
                PublishedGenerationId = PublishedGeneration.Id
            };
            Completed.TrySetResult();
            return Task.FromResult(PublishedGeneration);
        }

        public Task CompleteFailedRefreshAsync(
            string refreshId,
            CatalogueRefreshRunStatus status,
            DateTimeOffset completedAt,
            long durationMilliseconds,
            string failureMessage,
            CancellationToken cancellationToken)
        {
            LatestRefresh = LatestRefresh! with
            {
                Status = status,
                CompletedAt = completedAt,
                DurationMilliseconds = durationMilliseconds,
                FailureMessage = failureMessage
            };
            Completed.TrySetResult();
            return Task.CompletedTask;
        }
    }

    private sealed class AdvancingTimeProvider : TimeProvider
    {
        private DateTimeOffset current = DateTimeOffset.Parse("2026-08-15T10:00:00Z");

        public override DateTimeOffset GetUtcNow()
        {
            var value = current;
            current = current.AddSeconds(5);
            return value;
        }
    }
}
