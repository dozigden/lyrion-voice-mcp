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
        Assert.Equal(2, await CountAsync("catalogue_track_artists"));
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
            Artists =
            [
                new CatalogueImportArtist("11", "The Glass Harbours", null),
                new CatalogueImportArtist("11", "Duplicate Harbour", null)
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

    [Fact]
    public async Task InitialiseShouldMigrateVersionOneContributorRelationshipsToArtistsOnly()
    {
        // Arrange
        var versionOnePath = Path.Combine(directory, "version-1.db");
        var original = new SqliteMediaCatalogueStore(
            new CatalogueSettings(versionOnePath),
            new FixedTimeProvider(CompletedAt));
        await original.InitialiseAsync(TestContext.Current.CancellationToken);
        await using (var connection = new SqliteConnection($"Data Source={versionOnePath}"))
        {
            await connection.OpenAsync(TestContext.Current.CancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = """
                ALTER TABLE catalogue_generations RENAME COLUMN artist_count TO contributor_count;
                ALTER TABLE catalogue_artists RENAME TO catalogue_contributors;
                CREATE TABLE catalogue_track_contributors (
                    generation_id TEXT NOT NULL,
                    track_source_id TEXT NOT NULL,
                    contributor_source_id TEXT NOT NULL,
                    role TEXT NOT NULL,
                    PRIMARY KEY (generation_id, track_source_id, contributor_source_id, role),
                    FOREIGN KEY (generation_id, track_source_id)
                        REFERENCES catalogue_tracks(generation_id, source_id) ON DELETE CASCADE
                );
                DROP TABLE catalogue_track_artists;
                UPDATE catalogue_schema SET version = 1;
                INSERT INTO catalogue_generations (
                    id, source_id, source_provider, captured_at, published_at,
                    contributor_count, album_count, genre_count, track_count,
                    virtual_library_count, warning_count)
                VALUES ('generation-1', 'development', 'lms',
                    '2026-08-15T09:59:59.0000000Z', '2026-08-15T10:00:00.0000000Z',
                    3, 2, 0, 1, 0, 1);
                INSERT INTO catalogue_contributors (generation_id, source_id, name)
                VALUES
                    ('generation-1', '11', 'The Glass Harbours'),
                    ('generation-1', '12', 'Orla Meridian'),
                    ('generation-1', '13', 'Rowan Almanac');
                INSERT INTO catalogue_albums (
                    generation_id, source_id, title, album_artist_source_id)
                VALUES
                    ('generation-1', '21', 'Compass Weather', '13'),
                    ('generation-1', '22', 'Unmapped Weather', '14');
                INSERT INTO catalogue_tracks (
                    generation_id, source_id, title, url, is_remote)
                VALUES ('generation-1', '31', 'First Tide', 'file:///music/First%20Tide.flac', 0);
                INSERT INTO catalogue_track_contributors (
                    generation_id, track_source_id, contributor_source_id, role)
                VALUES
                    ('generation-1', '31', '11', 'ARTIST'),
                    ('generation-1', '31', '12', 'COMPOSER');
                INSERT INTO catalogue_state (id, published_generation_id)
                VALUES (1, 'generation-1');
                INSERT INTO catalogue_warnings (generation_id, code, message, occurrences)
                VALUES ('generation-1', 'missing-contributor', 'Legacy broad-role warning.', 5);
                """;
            await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }

        var migrated = new SqliteMediaCatalogueStore(
            new CatalogueSettings(versionOnePath),
            new FixedTimeProvider(CompletedAt));

        // Act
        await migrated.InitialiseAsync(TestContext.Current.CancellationToken);
        var published = await migrated.GetPublishedGenerationAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(2, await ScalarIntAsync(
            "SELECT version FROM catalogue_schema;",
            versionOnePath));
        Assert.Equal(2, published?.ArtistCount);
        Assert.Equal(1, published?.WarningCount);
        Assert.Equal(2, await CountAsync("catalogue_artists", versionOnePath));
        Assert.Equal(2, await ScalarIntAsync(
            "SELECT COUNT(*) FROM catalogue_artists WHERE source_id IN ('11', '13');",
            versionOnePath));
        Assert.Equal(0, await ScalarIntAsync(
            "SELECT COUNT(*) FROM catalogue_artists WHERE source_id = '12';",
            versionOnePath));
        Assert.Equal(1, await CountAsync("catalogue_track_artists", versionOnePath));
        Assert.Equal("missing-artist", await ScalarStringAsync(
            "SELECT code FROM catalogue_warnings;",
            versionOnePath));
        Assert.Equal(1, await ScalarIntAsync(
            "SELECT occurrences FROM catalogue_warnings WHERE code = 'missing-artist';",
            versionOnePath));
        Assert.Equal(0, await ScalarIntAsync(
            "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'catalogue_track_contributors';",
            versionOnePath));
    }

    private async Task<int> CountAsync(string table, string? path = null)
    {
        await using var connection = new SqliteConnection($"Data Source={path ?? databasePath}");
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM {table};";
        return Convert.ToInt32(
            await command.ExecuteScalarAsync(TestContext.Current.CancellationToken),
            System.Globalization.CultureInfo.InvariantCulture);
    }

    private async Task<string> ScalarStringAsync(string sql, string? path = null)
    {
        await using var connection = new SqliteConnection($"Data Source={path ?? databasePath}");
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return (string)(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken))!;
    }

    private async Task<int> ScalarIntAsync(string sql, string? path = null)
    {
        await using var connection = new SqliteConnection($"Data Source={path ?? databasePath}");
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt32(
            await command.ExecuteScalarAsync(TestContext.Current.CancellationToken),
            System.Globalization.CultureInfo.InvariantCulture);
    }

    private static CatalogueImportSnapshot CreateSnapshot(string trackTitle) => new(
        new CatalogueImportSource("development", "lms", "9.1.2", "1786379003"),
        CompletedAt.AddSeconds(-1),
        DateTimeOffset.FromUnixTimeSeconds(1786379003),
        [
            new CatalogueImportArtist("11", "The Glass Harbours", "artist:glass-harbours"),
            new CatalogueImportArtist("12", "Orla Meridian", null)
        ],
        [new CatalogueImportAlbum("21", "Compass Weather", "11", 2025, 1, false, "ALBUM", "31", null)],
        [new CatalogueImportGenre("41", "Maritime Pop"), new CatalogueImportGenre("42", "Night Folk")],
        [new CatalogueImportTrack(
            "31", trackTitle, "Harbour version", "file:///music/First%20Tide.flac", "flc", false,
            null, "21", 2025, 1, 1, 3, 241.5, 42_000_000, 96_000,
            CompletedAt.AddYears(-1), CompletedAt.AddMonths(-1), CompletedAt.AddDays(-1),
            "ALBUM", false, "31", "61", "Northern Bearings", "Live at Low Water", "Tidal pieces",
            ["11", "12"],
            ["41", "42"],
            [new CatalogueImportTrackStatistics("lms-core", 80, 7, null)])],
        [new CatalogueImportVirtualLibrary("51", "Evening Navigation", ["31"])],
        [new CatalogueImportWarning("synthetic-warning", "A fictional warning.", 2)]);

    private sealed class FixedTimeProvider(DateTimeOffset value) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => value;
    }
}
