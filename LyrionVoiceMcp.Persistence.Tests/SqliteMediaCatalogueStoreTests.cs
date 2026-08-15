using LyrionVoiceMcp.Abstractions;
using LyrionVoiceMcp.Persistence;
using Microsoft.Data.Sqlite;

namespace LyrionVoiceMcp.Persistence.Tests;

public sealed class SqliteMediaCatalogueStoreTests : IAsyncLifetime
{
    private static readonly DateTimeOffset StartedAt =
        DateTimeOffset.Parse("2026-08-15T10:00:00Z");
    private static readonly DateTimeOffset CompletedAt = StartedAt.AddSeconds(12);
    private readonly string directory = Path.Combine(
        Path.GetTempPath(),
        $"lvm-catalogue-{Guid.NewGuid():N}");
    private string databasePath = null!;
    private SqliteMediaCatalogueStore store = null!;

    public async ValueTask InitializeAsync()
    {
        databasePath = Path.Combine(directory, "catalogue.db");
        store = new SqliteMediaCatalogueStore(
            new CatalogueSettings(databasePath),
            new FixedTimeProvider(CompletedAt));
        await store.InitialiseAsync(TestContext.Current.CancellationToken);
    }

    public ValueTask DisposeAsync()
    {
        if (Directory.Exists(directory))
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(directory, recursive: true);
        }

        return ValueTask.CompletedTask;
    }

    [Fact]
    public async Task PublishShouldAtomicallyStoreTheCompleteGenerationAndRefreshRun()
    {
        // Arrange
        await store.BeginRefreshAsync("refresh-1", StartedAt, TestContext.Current.CancellationToken);

        // Act
        var published = await store.PublishAsync(
            CreateSnapshot("First Tide"),
            "refresh-1",
            CompletedAt,
            12_000,
            TestContext.Current.CancellationToken);
        var current = await store.GetPublishedGenerationAsync(TestContext.Current.CancellationToken);
        var run = await store.GetLatestRefreshRunAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(published, current);
        Assert.Equal(CatalogueRefreshRunStatus.Succeeded, run?.Status);
        Assert.Equal(published.Id, run?.PublishedGenerationId);
        Assert.Equal(12_000, run?.DurationMilliseconds);
        Assert.Equal(1, await CountAsync("catalogue_tracks"));
        Assert.Equal(2, await CountAsync("catalogue_track_contributors"));
        Assert.Equal(2, await CountAsync("catalogue_track_genres"));
        Assert.Equal(1, await CountAsync("catalogue_track_statistics"));
        Assert.Equal(1, await CountAsync("catalogue_virtual_library_tracks"));
        Assert.Equal(1, await CountAsync("catalogue_warnings"));
    }

    [Fact]
    public async Task FailedPublicationShouldLeaveThePreviousGenerationPublished()
    {
        // Arrange
        await store.BeginRefreshAsync("refresh-1", StartedAt, TestContext.Current.CancellationToken);
        var first = await store.PublishAsync(
            CreateSnapshot("First Tide"),
            "refresh-1",
            CompletedAt,
            12_000,
            TestContext.Current.CancellationToken);
        await store.BeginRefreshAsync("refresh-2", CompletedAt.AddMinutes(1), TestContext.Current.CancellationToken);
        var invalid = CreateSnapshot("Broken Tide") with
        {
            Contributors =
            [
                new CatalogueImportContributor("11", "The Glass Harbours", null),
                new CatalogueImportContributor("11", "Duplicate Harbour", null)
            ]
        };

        // Act
        await Assert.ThrowsAsync<SqliteException>(() => store.PublishAsync(
            invalid,
            "refresh-2",
            CompletedAt.AddMinutes(1).AddSeconds(3),
            3_000,
            TestContext.Current.CancellationToken));
        await store.CompleteFailedRefreshAsync(
            "refresh-2",
            CatalogueRefreshRunStatus.Failed,
            CompletedAt.AddMinutes(1).AddSeconds(3),
            3_000,
            "Catalogue refresh failed. See the service logs for details.",
            TestContext.Current.CancellationToken);
        var current = await store.GetPublishedGenerationAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(first.Id, current?.Id);
        Assert.Equal(1, await CountAsync("catalogue_generations"));
        Assert.Equal("First Tide", await ScalarStringAsync("SELECT title FROM catalogue_tracks LIMIT 1;"));
        Assert.Equal(CatalogueRefreshRunStatus.Failed,
            (await store.GetLatestRefreshRunAsync(TestContext.Current.CancellationToken))?.Status);
    }

    [Fact]
    public async Task InitialiseShouldMarkAnAbandonedRefreshAsInterrupted()
    {
        // Arrange
        await store.BeginRefreshAsync("refresh-1", StartedAt, TestContext.Current.CancellationToken);
        var restarted = new SqliteMediaCatalogueStore(
            new CatalogueSettings(databasePath),
            new FixedTimeProvider(StartedAt.AddMinutes(2)));

        // Act
        await restarted.InitialiseAsync(TestContext.Current.CancellationToken);
        var run = await restarted.GetLatestRefreshRunAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(CatalogueRefreshRunStatus.Interrupted, run?.Status);
        Assert.Equal(120_000, run?.DurationMilliseconds);
        Assert.Equal("Catalogue refresh was interrupted before completion.", run?.FailureMessage);
    }

    private async Task<int> CountAsync(string table)
    {
        await using var connection = new SqliteConnection($"Data Source={databasePath}");
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM {table};";
        return Convert.ToInt32(
            await command.ExecuteScalarAsync(TestContext.Current.CancellationToken),
            System.Globalization.CultureInfo.InvariantCulture);
    }

    private async Task<string> ScalarStringAsync(string sql)
    {
        await using var connection = new SqliteConnection($"Data Source={databasePath}");
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return (string)(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken))!;
    }

    private static CatalogueImportSnapshot CreateSnapshot(string trackTitle) => new(
        new CatalogueImportSource("development", "lms", "9.1.2", "1786379003"),
        CompletedAt.AddSeconds(-1),
        DateTimeOffset.FromUnixTimeSeconds(1786379003),
        [
            new CatalogueImportContributor("11", "The Glass Harbours", "artist:glass-harbours"),
            new CatalogueImportContributor("12", "Orla Meridian", null)
        ],
        [new CatalogueImportAlbum("21", "Compass Weather", "11", 2025, 1, false, "ALBUM", "31", null)],
        [new CatalogueImportGenre("41", "Maritime Pop"), new CatalogueImportGenre("42", "Night Folk")],
        [new CatalogueImportTrack(
            "31", trackTitle, "Harbour version", "file:///music/First%20Tide.flac", "flc", false,
            null, "21", 2025, 1, 1, 3, 241.5, 42_000_000, 96_000,
            CompletedAt.AddYears(-1), CompletedAt.AddMonths(-1), CompletedAt.AddDays(-1),
            "ALBUM", false, "31", "61", "Northern Bearings", "Live at Low Water", "Tidal pieces",
            [
                new CatalogueImportTrackContributor("11", "ARTIST"),
                new CatalogueImportTrackContributor("12", "COMPOSER")
            ],
            ["41", "42"],
            [new CatalogueImportTrackStatistics("lms-core", 80, 7, null)])],
        [new CatalogueImportVirtualLibrary("51", "Evening Navigation", ["31"])],
        [new CatalogueImportWarning("synthetic-warning", "A fictional warning.", 2)]);

    private sealed class FixedTimeProvider(DateTimeOffset value) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => value;
    }
}
