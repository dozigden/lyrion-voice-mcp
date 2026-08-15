using LyrionVoiceMcp.Abstractions;
using LyrionVoiceMcp.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace LyrionVoiceMcp.Services.Tests;

public sealed class CatalogueRefreshServiceTests
{
    [Fact]
    public async Task ConcurrentRefreshShouldBeRejectedWhileTheFirstRefreshContinues()
    {
        // Arrange
        var reader = new BlockingCatalogueReader(CreateReadResult());
        var store = new RecordingCatalogueStore();
        await using var provider = CreateProvider(reader);
        var service = CreateService(provider, store);
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
    public async Task FailedRefreshShouldRetainTheLastSuccessfulSummaryAndSanitiseTheFailure()
    {
        // Arrange
        var existing = CreateSummary();
        var store = new RecordingCatalogueStore { Summary = existing };
        await using var provider = CreateProvider(new FailingCatalogueReader());
        var service = CreateService(provider, store);
        await service.StartAsync(TestContext.Current.CancellationToken);

        // Act
        var outcome = await service.RefreshAsync(TestContext.Current.CancellationToken);
        await store.Completed.Task.WaitAsync(TestContext.Current.CancellationToken);
        var status = await service.GetStatusAsync(TestContext.Current.CancellationToken);
        await service.StopAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.IsType<CatalogueRefreshStarted>(outcome);
        Assert.Equal(existing, status.Summary);
        Assert.Equal(CatalogueRefreshRunStatus.Failed, status.LatestRefresh?.Status);
        Assert.Equal(
            "Catalogue refresh failed. See the service logs for details.",
            status.LatestRefresh?.FailureMessage);
        Assert.DoesNotContain(
            "private detail",
            status.LatestRefresh?.FailureMessage,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task FailureRecordingErrorShouldNotFaultTheBackgroundService()
    {
        // Arrange
        var store = new RecordingCatalogueStore { ThrowWhenCompletingFailure = true };
        var logger = new RecordingLogger();
        await using var provider = CreateProvider(new FailingCatalogueReader());
        var service = CreateService(provider, store, logger);
        await service.StartAsync(TestContext.Current.CancellationToken);

        // Act
        await service.RefreshAsync(TestContext.Current.CancellationToken);
        await logger.TerminalRecordingFailure.Task.WaitAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.False(service.ExecuteTask?.IsCompleted ?? true);
        await service.StopAsync(TestContext.Current.CancellationToken);
    }

    private static CatalogueRefreshService CreateService(
        ServiceProvider provider,
        IMediaCatalogueStore store,
        ILogger<CatalogueRefreshService>? logger = null) => new(
        provider.GetRequiredService<IServiceScopeFactory>(),
        store,
        new AdvancingTimeProvider(),
        logger ?? NullLogger<CatalogueRefreshService>.Instance);

    private static CatalogueSourceReadResult CreateReadResult() => new(
        new CatalogueImportSource("development", "lms", "9.1.2", "revision-2"),
        DateTimeOffset.UtcNow,
        null,
        0,
        0,
        0,
        0,
        0,
        []);

    private static CatalogueSummary CreateSummary() => new(
        "development",
        "lms",
        "revision-1",
        "9.1.2",
        DateTimeOffset.UtcNow,
        null,
        DateTimeOffset.UtcNow,
        1,
        1,
        1,
        1,
        1,
        0);

    private static ServiceProvider CreateProvider(ICatalogueSourceReader reader)
    {
        var services = new ServiceCollection();
        services.AddSingleton(reader);
        return services.BuildServiceProvider();
    }

    private sealed class BlockingCatalogueReader(
        CatalogueSourceReadResult result) : ICatalogueSourceReader
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int ReadCount { get; private set; }

        public async Task<CatalogueSourceReadResult> ReadAsync(
            string refreshId,
            ICatalogueImportWriter writer,
            CancellationToken cancellationToken)
        {
            ReadCount++;
            Started.SetResult();
            await Release.Task.WaitAsync(cancellationToken);
            return result;
        }
    }

    private sealed class FailingCatalogueReader : ICatalogueSourceReader
    {
        public Task<CatalogueSourceReadResult> ReadAsync(
            string refreshId,
            ICatalogueImportWriter writer,
            CancellationToken cancellationToken) =>
            Task.FromException<CatalogueSourceReadResult>(
                new InvalidOperationException("private detail from an upstream response"));
    }

    private sealed class RecordingCatalogueStore : IMediaCatalogueStore
    {
        public TaskCompletionSource Completed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public CatalogueSummary? Summary { get; set; }
        public CatalogueRefreshRun? LatestRefresh { get; private set; }
        public bool ThrowWhenCompletingFailure { get; init; }

        public Task InitialiseAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<CatalogueSummary?> GetSummaryAsync(CancellationToken cancellationToken) =>
            Task.FromResult(Summary);
        public Task<CatalogueRefreshRun?> GetLatestRefreshRunAsync(CancellationToken cancellationToken) =>
            Task.FromResult(LatestRefresh);

        public Task BeginRefreshAsync(string refreshId, DateTimeOffset startedAt, CancellationToken cancellationToken)
        {
            LatestRefresh = new CatalogueRefreshRun(
                refreshId,
                CatalogueRefreshRunStatus.Running,
                startedAt,
                null,
                null,
                null,
                []);
            return Task.CompletedTask;
        }

        public Task<CatalogueSummary> CompleteRefreshAsync(
            string refreshId,
            CatalogueSourceReadResult source,
            DateTimeOffset completedAt,
            long durationMilliseconds,
            CancellationToken cancellationToken)
        {
            Summary = new CatalogueSummary(
                source.Source.Id,
                source.Source.Provider,
                source.Source.Revision,
                source.Source.Version,
                source.CapturedAt,
                source.SourceLastScanAt,
                completedAt,
                0,
                source.AlbumCount,
                source.GenreCount,
                source.TrackCount,
                source.VirtualLibraryCount,
                0);
            LatestRefresh = LatestRefresh! with
            {
                Status = CatalogueRefreshRunStatus.Succeeded,
                CompletedAt = completedAt,
                DurationMilliseconds = durationMilliseconds
            };
            Completed.TrySetResult();
            return Task.FromResult(Summary);
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
            return ThrowWhenCompletingFailure
                ? Task.FromException(new InvalidOperationException("Catalogue database unavailable."))
                : Task.CompletedTask;
        }

        public Task WriteAlbumsAsync(string refreshId, IReadOnlyList<CatalogueImportAlbum> albums, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task WriteGenresAsync(string refreshId, IReadOnlyList<CatalogueImportGenre> genres, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task WriteTracksAsync(string refreshId, IReadOnlyList<CatalogueImportTrack> tracks, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task WriteArtistsAsync(string refreshId, IReadOnlyList<CatalogueImportArtist> artists, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task WriteVirtualLibrariesAsync(string refreshId, IReadOnlyList<CatalogueImportVirtualLibrary> libraries, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task WriteVirtualLibraryTracksAsync(string refreshId, string librarySourceId, IReadOnlyList<string> trackSourceIds, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task AppendRefreshLogAsync(string refreshId, CatalogueRefreshLogLevel level, string message, int? processedCount, int? totalCount, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class RecordingLogger : ILogger<CatalogueRefreshService>
    {
        public TaskCompletionSource TerminalRecordingFailure { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (formatter(state, exception).Contains(
                    "could not record its terminal state",
                    StringComparison.Ordinal))
            {
                TerminalRecordingFailure.TrySetResult();
            }
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
