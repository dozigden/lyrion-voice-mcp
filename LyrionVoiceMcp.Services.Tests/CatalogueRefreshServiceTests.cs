using LyrionVoiceMcp.Abstractions;
using LyrionVoiceMcp.Services;

namespace LyrionVoiceMcp.Services.Tests;

public sealed class CatalogueRefreshServiceTests
{
    [Fact]
    public async Task HandlerShouldWriteProgressAndReturnTheCompletedSummary()
    {
        // Arrange
        var summary = new CatalogueSummary(
            "development", "lms", "revision-1", "9.1.2",
            DateTimeOffset.Parse("2026-08-16T10:00:00Z"), null,
            DateTimeOffset.Parse("2026-08-16T10:01:00Z"), 2, 1, 2, 3, 1, 1);
        var store = new RecordingCatalogueStore(summary);
        var logs = new RecordingJobLogWriter();
        var searchIndexes = new RecordingSearchIndexService();
        var handler = new CatalogueRefreshJobHandler(
            new RecordingCatalogueReader(),
            store,
            searchIndexes,
            logs,
            new FixedTimeProvider(summary.RefreshedAt));

        // Act
        var result = await handler.HandleAsync(
            new JobContext(42, JobTypes.CatalogueRefresh, "{}"),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.Success);
        Assert.Equal("job-42", store.RefreshId);
        Assert.Equal("job-42", searchIndexes.CatalogueRefreshId);
        Assert.Contains(logs.Entries, entry => entry.Message == "Catalogue refresh started.");
        Assert.Contains(logs.Entries, entry => entry.Message == "Reading fictional catalogue.");
        Assert.Contains(logs.Entries, entry => entry.Message == "Catalogue refresh completed.");
    }

    [Fact]
    public async Task HandlerShouldRecordFailedCatalogueStateWhenReadingFails()
    {
        // Arrange
        var summary = new CatalogueSummary(
            "development", "lms", "revision-1", "9.1.2",
            DateTimeOffset.Parse("2026-08-16T10:00:00Z"), null,
            DateTimeOffset.Parse("2026-08-16T10:01:00Z"), 2, 1, 2, 3, 1, 0);
        var store = new RecordingCatalogueStore(summary);
        var handler = new CatalogueRefreshJobHandler(
            new ThrowingCatalogueReader(),
            store,
            new RecordingSearchIndexService(),
            new RecordingJobLogWriter(),
            new FixedTimeProvider(summary.RefreshedAt));

        // Act
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.HandleAsync(
                new JobContext(43, JobTypes.CatalogueRefresh, "{}"),
                TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal("Fictional read failure.", exception.Message);
        Assert.Equal("job-43", store.RefreshId);
        Assert.Equal(CatalogueStateStatus.Failed, store.TerminalStatus);
    }

    private sealed class RecordingCatalogueReader : ICatalogueSourceReader
    {
        public async Task<CatalogueSourceReadResult> ReadAsync(
            string refreshId,
            ICatalogueImportWriter writer,
            ICatalogueRefreshLogSink log,
            CancellationToken cancellationToken)
        {
            await log.WriteAsync(
                CatalogueRefreshLogLevel.Information,
                "Reading fictional catalogue.",
                3,
                3,
                cancellationToken);
            return new CatalogueSourceReadResult(
                new CatalogueImportSource("development", "lms", "9.1.2", "revision-1"),
                DateTimeOffset.Parse("2026-08-16T10:00:00Z"), null, 0, 0, 0, 0, 0, []);
        }
    }

    private sealed class ThrowingCatalogueReader : ICatalogueSourceReader
    {
        public Task<CatalogueSourceReadResult> ReadAsync(
            string refreshId,
            ICatalogueImportWriter writer,
            ICatalogueRefreshLogSink log,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Fictional read failure.");
    }

    private sealed class RecordingCatalogueStore(CatalogueSummary summary) : IMediaCatalogueStore
    {
        public string? RefreshId { get; private set; }
        public CatalogueStateStatus? TerminalStatus { get; private set; }
        public Task InitialiseAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<CatalogueState?> GetStateAsync(CancellationToken cancellationToken) =>
            Task.FromResult<CatalogueState?>(new CatalogueState(
                RefreshId ?? "previous",
                CatalogueStateStatus.Succeeded,
                summary.CapturedAt,
                summary.RefreshedAt,
                summary));
        public Task<CatalogueSummary?> GetSummaryAsync(CancellationToken cancellationToken) => Task.FromResult<CatalogueSummary?>(summary);
        public Task BeginRefreshAsync(string refreshId, DateTimeOffset startedAt, CancellationToken cancellationToken)
        {
            RefreshId = refreshId;
            return Task.CompletedTask;
        }
        public Task<CatalogueRefreshCompletion> CompleteRefreshAsync(string refreshId, CatalogueSourceReadResult source, DateTimeOffset completedAt, int existingWarningCount, CancellationToken cancellationToken)
        {
            RefreshId = refreshId;
            return Task.FromResult(new CatalogueRefreshCompletion(summary, []));
        }
        public Task FinishRefreshAsync(string refreshId, CatalogueStateStatus status, DateTimeOffset completedAt, CancellationToken cancellationToken)
        {
            RefreshId = refreshId;
            TerminalStatus = status;
            return Task.CompletedTask;
        }
        public Task WriteAlbumsAsync(string refreshId, IReadOnlyList<CatalogueImportAlbum> albums, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task WriteGenresAsync(string refreshId, IReadOnlyList<CatalogueImportGenre> genres, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task WriteTracksAsync(string refreshId, IReadOnlyList<CatalogueImportTrack> tracks, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task WriteArtistsAsync(string refreshId, IReadOnlyList<CatalogueImportArtist> artists, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task WriteVirtualLibrariesAsync(string refreshId, IReadOnlyList<CatalogueImportVirtualLibrary> libraries, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task WriteVirtualLibraryTracksAsync(string refreshId, string librarySourceId, IReadOnlyList<string> trackSourceIds, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class RecordingSearchIndexService : ISearchIndexService
    {
        public string? CatalogueRefreshId { get; private set; }

        public Task<SearchIndexStatus> GetAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new SearchIndexStatus("catalogue-phuzzy-sqlite", null, null));

        public Task<SearchIndexRebuildOutcome> RebuildAsync(CancellationToken cancellationToken) =>
            Task.FromResult<SearchIndexRebuildOutcome>(new SearchIndexRebuildRejected("Not used."));

        public Task<long?> EnqueueForCatalogueAsync(string catalogueRefreshId, CancellationToken cancellationToken)
        {
            CatalogueRefreshId = catalogueRefreshId;
            return Task.FromResult<long?>(101);
        }
    }

    private sealed class RecordingJobLogWriter : IJobLogWriter
    {
        public List<(JobLogLevel Level, string Message)> Entries { get; } = [];
        public Task WriteAsync(long jobId, JobLogLevel level, string message, object? data, CancellationToken cancellationToken)
        {
            Entries.Add((level, message));
            return Task.CompletedTask;
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
