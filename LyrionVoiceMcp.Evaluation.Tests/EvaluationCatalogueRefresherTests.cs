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
        store = new SqliteMediaCatalogueStore(
            new CatalogueSettings(databasePath),
            TimeProvider.System);
    }

    [Fact]
    public async Task RefreshAsync_builds_a_successful_local_catalogue()
    {
        var refresher = new EvaluationCatalogueRefresher(
            store,
            new StubSourceReader(),
            TimeProvider.System);

        var summary = await refresher.RefreshAsync(TestContext.Current.CancellationToken);

        var latest = await store.GetLatestRefreshRunAsync(TestContext.Current.CancellationToken);
        Assert.Equal("live-evaluation", summary.SourceId);
        Assert.Equal(CatalogueRefreshRunStatus.Succeeded, latest?.Status);
    }

    [Fact]
    public async Task RefreshAsync_records_a_source_failure()
    {
        var refresher = new EvaluationCatalogueRefresher(
            store,
            new StubSourceReader(new InvalidOperationException("Fictional source failed.")),
            TimeProvider.System);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            refresher.RefreshAsync(TestContext.Current.CancellationToken));

        var latest = await store.GetLatestRefreshRunAsync(TestContext.Current.CancellationToken);
        Assert.Equal(CatalogueRefreshRunStatus.Failed, latest?.Status);
        Assert.Equal("Evaluation catalogue refresh failed.", latest?.FailureMessage);
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
