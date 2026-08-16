using LyrionVoiceMcp.Abstractions;
using LyrionVoiceMcp.Persistence;

namespace LyrionVoiceMcp.Evaluation.Tests;

public sealed class EvaluationCatalogueRefresherTests : IDisposable
{
    private readonly string directory = Path.Combine(
        Path.GetTempPath(),
        $"lyrion-voice-evaluation-refresh-{Guid.NewGuid():N}");
    private readonly SqliteMediaCatalogueStore store;

    public EvaluationCatalogueRefresherTests()
    {
        var databasePath = Path.Combine(directory, "catalogue.db");
        store = new SqliteMediaCatalogueStore(new CatalogueSettings(databasePath));
    }

    [Fact]
    public async Task RefreshAsync_builds_a_successful_local_catalogue()
    {
        var refresher = new EvaluationCatalogueRefresher(
            store,
            new StubSourceReader(),
            TimeProvider.System);

        var summary = await refresher.RefreshAsync(TestContext.Current.CancellationToken);

        Assert.Equal("live-evaluation", summary.SourceId);
    }

    [Fact]
    public async Task RefreshAsync_preserves_a_source_failure_without_replacing_the_catalogue()
    {
        var refresher = new EvaluationCatalogueRefresher(
            store,
            new StubSourceReader(new InvalidOperationException("Fictional source failed.")),
            TimeProvider.System);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            refresher.RefreshAsync(TestContext.Current.CancellationToken));

        Assert.Null(await store.GetSummaryAsync(TestContext.Current.CancellationToken));
    }

    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private sealed class StubSourceReader(Exception? exception = null) : ICatalogueSourceReader
    {
        public Task<CatalogueSourceReadResult> ReadAsync(
            string refreshId,
            ICatalogueImportWriter writer,
            ICatalogueRefreshLogSink log,
            CancellationToken cancellationToken)
        {
            if (exception is not null)
            {
                throw exception;
            }

            return Task.FromResult(new CatalogueSourceReadResult(
                new CatalogueImportSource("live-evaluation", "lms", "1.0", "revision-1"),
                DateTimeOffset.UtcNow,
                null,
                0,
                0,
                0,
                0,
                0,
                []));
        }
    }
}
