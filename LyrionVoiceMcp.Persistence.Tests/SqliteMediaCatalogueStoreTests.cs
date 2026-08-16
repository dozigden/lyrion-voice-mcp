using LyrionVoiceMcp.Abstractions;
using LyrionVoiceMcp.Persistence;
using Microsoft.Data.Sqlite;

namespace LyrionVoiceMcp.Persistence.Tests;

public sealed class SqliteMediaCatalogueStoreTests : IDisposable
{
    private static readonly DateTimeOffset StartedAt =
        new(2026, 8, 15, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset CompletedAt = StartedAt.AddSeconds(12);
    private readonly string directory = Path.Combine(
        Path.GetTempPath(),
        $"lyrion-voice-catalogue-tests-{Guid.NewGuid():N}");
    private readonly string databasePath;
    private readonly SqliteMediaCatalogueStore store;

    public SqliteMediaCatalogueStoreTests()
    {
        databasePath = Path.Combine(directory, "catalogue.db");
        store = new SqliteMediaCatalogueStore(new CatalogueSettings(databasePath));
        store.InitialiseAsync(CancellationToken.None).GetAwaiter().GetResult();
    }

    [Fact]
    public async Task CompleteRefreshShouldPersistBatchesAndOnlyReferencedArtists()
    {
        // Arrange
        await WriteCatalogueAsync("refresh-1", [CreateTrack("31", "First Tide")]);

        // Act
        var summary = await store.CompleteRefreshAsync(
            "refresh-1",
            CreateReadResult(trackCount: 1),
            CompletedAt,
            0,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(2, summary.Summary.ArtistCount);
        Assert.Equal(1, summary.Summary.TrackCount);
        Assert.Equal(2, await CountAsync("catalogue_artists"));
        Assert.Equal(0, await ScalarIntAsync(
            "SELECT COUNT(*) FROM catalogue_artists WHERE source_id = '99';"));
        Assert.Equal(1, await CountAsync("catalogue_track_artists"));
        Assert.Equal(2, await CountAsync("catalogue_track_genres"));
        Assert.Equal(1, await CountAsync("catalogue_track_statistics"));
        Assert.Equal(1, await CountAsync("catalogue_virtual_library_tracks"));
        var state = await store.GetStateAsync(TestContext.Current.CancellationToken);
        Assert.Equal("refresh-1", state?.RefreshId);
        Assert.Equal(CatalogueStateStatus.Succeeded, state?.Status);
        Assert.Equal(CompletedAt, state?.CompletedAt);
        Assert.Equal(1, state?.Summary?.TrackCount);
    }

    [Fact]
    public async Task CompleteRefreshShouldRejectDuplicateArtistLookupRowsAcrossBatches()
    {
        // Arrange
        await WriteCatalogueAsync("refresh-1", [CreateTrack("31", "First Tide")]);
        await store.WriteArtistsAsync(
            "refresh-1",
            [new CatalogueImportArtist("11", "The Glass Harbours", null)],
            TestContext.Current.CancellationToken);
        var result = CreateReadResult(trackCount: 1) with { ArtistLookupCount = 4 };

        // Act
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            store.CompleteRefreshAsync(
                "refresh-1",
                result,
                CompletedAt,
                0,
                TestContext.Current.CancellationToken));

        // Assert
        Assert.Contains("catalogue_artist_lookup", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CompleteRefreshShouldRejectDuplicateVirtualLibraryMembersAcrossBatches()
    {
        // Arrange
        await WriteCatalogueAsync("refresh-1", [CreateTrack("31", "First Tide")]);
        await store.WriteVirtualLibraryTracksAsync(
            "refresh-1",
            "51",
            ["31"],
            TestContext.Current.CancellationToken);
        var result = CreateReadResult(trackCount: 1) with
        {
            VirtualLibraryMemberships =
            [
                new CatalogueImportVirtualLibraryMembership("51", 2)
            ]
        };

        // Act
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            store.CompleteRefreshAsync(
                "refresh-1",
                result,
                CompletedAt,
                0,
                TestContext.Current.CancellationToken));

        // Assert
        Assert.Contains("virtual-library memberships", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FailedRefreshShouldKeepCompletedBatchesWithoutRemovingOlderRows()
    {
        // Arrange
        await WriteCatalogueAsync("refresh-1", [CreateTrack("31", "First Tide")]);
        await store.CompleteRefreshAsync(
            "refresh-1",
            CreateReadResult(trackCount: 1),
            CompletedAt,
            0,
            TestContext.Current.CancellationToken);
        await store.BeginRefreshAsync(
            "refresh-2",
            CompletedAt.AddMinutes(1),
            TestContext.Current.CancellationToken);
        await store.WriteTracksAsync(
            "refresh-2",
            [CreateTrack("32", "Second Tide")],
            TestContext.Current.CancellationToken);

        // Act
        var summary = await store.GetSummaryAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(summary);
        Assert.Equal(
            CatalogueStateStatus.Running,
            (await store.GetStateAsync(TestContext.Current.CancellationToken))?.Status);
        Assert.Equal(2, await CountAsync("catalogue_tracks"));
        Assert.Equal(1, await ScalarIntAsync(
            "SELECT COUNT(*) FROM catalogue_tracks WHERE title = 'First Tide';"));
        Assert.Equal(1, await ScalarIntAsync(
            "SELECT COUNT(*) FROM catalogue_tracks WHERE title = 'Second Tide';"));
    }

    [Fact]
    public async Task FailedRefreshShouldRecordTerminalStateWithoutRetainingThePreviousSummary()
    {
        // Arrange
        await WriteCatalogueAsync("refresh-1", [CreateTrack("31", "First Tide")]);
        await store.CompleteRefreshAsync(
            "refresh-1",
            CreateReadResult(trackCount: 1),
            CompletedAt,
            0,
            TestContext.Current.CancellationToken);
        var failedAt = CompletedAt.AddMinutes(2);
        await store.BeginRefreshAsync(
            "refresh-2",
            CompletedAt.AddMinutes(1),
            TestContext.Current.CancellationToken);

        // Act
        await store.FinishRefreshAsync(
            "refresh-2",
            CatalogueStateStatus.Failed,
            failedAt,
            TestContext.Current.CancellationToken);

        // Assert
        var state = await store.GetStateAsync(TestContext.Current.CancellationToken);
        Assert.Equal("refresh-2", state?.RefreshId);
        Assert.Equal(CatalogueStateStatus.Failed, state?.Status);
        Assert.Equal(failedAt, state?.CompletedAt);
        Assert.Null(state?.Summary);
        Assert.Null(await store.GetSummaryAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task InitialiseShouldMarkAnAbandonedRefreshInterrupted()
    {
        // Arrange
        var interruptedAt = CompletedAt.AddHours(1);
        await store.BeginRefreshAsync(
            "refresh-running",
            StartedAt,
            TestContext.Current.CancellationToken);
        var restarted = new SqliteMediaCatalogueStore(
            new CatalogueSettings(databasePath),
            new FixedTimeProvider(interruptedAt));

        // Act
        await restarted.InitialiseAsync(TestContext.Current.CancellationToken);

        // Assert
        var state = await restarted.GetStateAsync(TestContext.Current.CancellationToken);
        Assert.Equal("refresh-running", state?.RefreshId);
        Assert.Equal(CatalogueStateStatus.Interrupted, state?.Status);
        Assert.Equal(interruptedAt, state?.CompletedAt);
        Assert.Null(state?.Summary);
    }

    [Fact]
    public async Task LaterSuccessfulRefreshShouldRemoveRowsNotSeenInThatRefresh()
    {
        // Arrange
        await WriteCatalogueAsync(
            "refresh-1",
            [CreateTrack("31", "First Tide"), CreateTrack("32", "Second Tide")]);
        await store.CompleteRefreshAsync(
            "refresh-1",
            CreateReadResult(trackCount: 2),
            CompletedAt,
            0,
            TestContext.Current.CancellationToken);
        await WriteCatalogueAsync("refresh-2", [CreateTrack("32", "Second Tide revised")]);

        // Act
        await store.CompleteRefreshAsync(
            "refresh-2",
            CreateReadResult(trackCount: 1),
            CompletedAt.AddMinutes(1).AddSeconds(10),
            0,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(1, await CountAsync("catalogue_tracks"));
        Assert.Equal("Second Tide revised", await ScalarStringAsync(
            "SELECT title FROM catalogue_tracks;"));
    }

    [Fact]
    public async Task InitialiseShouldRebuildAnOlderCatalogueSchemaWithoutMigratingData()
    {
        // Arrange
        await WriteCatalogueAsync("refresh-1", [CreateTrack("31", "Disposable Tide")]);
        await using (var connection = await OpenAsync())
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "UPDATE catalogue_schema SET version = 2;";
            await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }

        var restarted = new SqliteMediaCatalogueStore(new CatalogueSettings(databasePath));

        // Act
        await restarted.InitialiseAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(6, await ScalarIntAsync("SELECT version FROM catalogue_schema;"));
        Assert.Equal(0, await CountAsync("catalogue_tracks"));
        Assert.Null(await restarted.GetSummaryAsync(TestContext.Current.CancellationToken));
    }

    private async Task WriteCatalogueAsync(
        string refreshId,
        IReadOnlyList<CatalogueImportTrack> tracks)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await store.BeginRefreshAsync(refreshId, StartedAt, cancellationToken);
        await store.WriteAlbumsAsync(
            refreshId,
            [new CatalogueImportAlbum("21", "Compass Weather", "12", 2025, 1, false, "ALBUM", "31", null)],
            cancellationToken);
        await store.WriteGenresAsync(
            refreshId,
            [new CatalogueImportGenre("41", "Maritime Pop"), new CatalogueImportGenre("42", "Night Folk")],
            cancellationToken);
        await store.WriteTracksAsync(refreshId, tracks, cancellationToken);
        await store.WriteArtistsAsync(
            refreshId,
            [
                new CatalogueImportArtist("11", "The Glass Harbours", null),
                new CatalogueImportArtist("12", "Orla Meridian", null),
                new CatalogueImportArtist("99", "Unreferenced Person", null)
            ],
            cancellationToken);
        await store.WriteVirtualLibrariesAsync(
            refreshId,
            [new CatalogueImportVirtualLibrary("51", "Evening Navigation")],
            cancellationToken);
        await store.WriteVirtualLibraryTracksAsync(
            refreshId,
            "51",
            tracks.Select(track => track.SourceId).ToArray(),
            cancellationToken);
    }

    private static CatalogueSourceReadResult CreateReadResult(int trackCount) => new(
        new CatalogueImportSource("development", "lms", "9.1.2", "1786379003"),
        CompletedAt.AddSeconds(-1),
        DateTimeOffset.FromUnixTimeSeconds(1786379003),
        3,
        1,
        2,
        trackCount,
        1,
        [new CatalogueImportVirtualLibraryMembership("51", trackCount)]);

    private static CatalogueImportTrack CreateTrack(string sourceId, string title) => new(
        sourceId,
        title,
        "Harbour version",
        $"file:///music/{sourceId}.flac",
        "flc",
        false,
        null,
        "21",
        2025,
        1,
        1,
        3,
        241.5,
        42_000_000,
        96_000,
        CompletedAt.AddYears(-1),
        CompletedAt.AddMonths(-1),
        CompletedAt.AddDays(-1),
        "ALBUM",
        false,
        sourceId,
        null,
        null,
        null,
        null,
        ["11"],
        ["41", "42"],
        [new CatalogueImportTrackStatistics("lms-core", 80, 7, null)]);

    private async Task<SqliteConnection> OpenAsync()
    {
        var connection = new SqliteConnection($"Data Source={databasePath}");
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        return connection;
    }

    private async Task<int> CountAsync(string table)
    {
        await using var connection = await OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM {table};";
        return Convert.ToInt32(
            await command.ExecuteScalarAsync(TestContext.Current.CancellationToken),
            System.Globalization.CultureInfo.InvariantCulture);
    }

    private async Task<int> ScalarIntAsync(string sql)
    {
        await using var connection = await OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt32(
            await command.ExecuteScalarAsync(TestContext.Current.CancellationToken),
            System.Globalization.CultureInfo.InvariantCulture);
    }

    private async Task<string> ScalarStringAsync(string sql)
    {
        await using var connection = await OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return (string)(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken))!;
    }

    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }

        GC.SuppressFinalize(this);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

}
