using LyrionVoiceMcp.Abstractions;
using Microsoft.Data.Sqlite;

namespace LyrionVoiceMcp.Evaluation.Tests;

public sealed class CatalogueLexicalSearchResolverTests : IDisposable
{
    private readonly string directory = Path.Combine(
        Path.GetTempPath(),
        $"lyrion-voice-catalogue-evaluation-{Guid.NewGuid():N}");
    private readonly string databasePath;

    public CatalogueLexicalSearchResolverTests()
    {
        databasePath = Path.Combine(directory, "catalogue.db");
        Directory.CreateDirectory(directory);
    }

    [Theory]
    [InlineData("moonlit owls", MediaEntityKind.Artist, "The Moonlit Owls")]
    [InlineData("routes night", MediaEntityKind.Album, "Night Routes")]
    [InlineData("lantrn signals", MediaEntityKind.Track, "Lantern Signals")]
    [InlineData("elan vital", MediaEntityKind.Track, "Élan Vital")]
    public async Task SearchAsync_matches_normalised_and_fuzzy_catalogue_entities(
        string query,
        MediaEntityKind expectedKind,
        string expectedTitle)
    {
        await CreateCatalogueAsync(includeSuccessfulState: true);
        var resolver = await CatalogueLexicalSearchResolver.CreateAsync(
            databasePath,
            TestContext.Current.CancellationToken);

        var response = await resolver.SearchAsync(
            query,
            TestContext.Current.CancellationToken);

        var first = Assert.Single(response.Candidates, candidate =>
            candidate.Kind == expectedKind && candidate.Title == expectedTitle);
        Assert.NotNull(first);
        Assert.Null(response.Error);
        Assert.Equal(4, resolver.Metrics.IndexedCandidateCount);
    }

    [Fact]
    public async Task CreateAsync_requires_a_successfully_refreshed_catalogue()
    {
        await CreateCatalogueAsync(includeSuccessfulState: false);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CatalogueLexicalSearchResolver.CreateAsync(
                databasePath,
                TestContext.Current.CancellationToken));

        Assert.Contains("successful refresh", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreateAsync_rejects_partial_batches_from_a_later_failed_refresh()
    {
        await CreateCatalogueAsync(includeSuccessfulState: true);
        await using (var connection = new SqliteConnection($"Data Source={databasePath}"))
        {
            await connection.OpenAsync(TestContext.Current.CancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO catalogue_refresh_runs (id, status, started_at)
                VALUES ('refresh-2', 'failed', '2026-08-15T12:05:00Z');
                """;
            await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CatalogueLexicalSearchResolver.CreateAsync(
                databasePath,
                TestContext.Current.CancellationToken));

        Assert.Contains("converged", exception.Message, StringComparison.Ordinal);
    }

    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private async Task CreateCatalogueAsync(bool includeSuccessfulState)
    {
        await using var connection = new SqliteConnection($"Data Source={databasePath}");
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE catalogue_schema (version INTEGER NOT NULL);
            CREATE TABLE catalogue_state (id INTEGER PRIMARY KEY);
            CREATE TABLE catalogue_refresh_runs (
                id TEXT PRIMARY KEY,
                status TEXT NOT NULL,
                started_at TEXT NOT NULL);
            CREATE TABLE catalogue_artists (
                source_id TEXT PRIMARY KEY,
                name TEXT NOT NULL);
            CREATE TABLE catalogue_albums (
                source_id TEXT PRIMARY KEY,
                title TEXT NOT NULL,
                album_artist_source_id TEXT NULL);
            CREATE TABLE catalogue_tracks (
                source_id TEXT PRIMARY KEY,
                title TEXT NOT NULL,
                album_source_id TEXT NULL);
            CREATE TABLE catalogue_track_artists (
                track_source_id TEXT NOT NULL,
                artist_source_id TEXT NOT NULL);

            INSERT INTO catalogue_schema (version) VALUES (4);
            INSERT INTO catalogue_artists (source_id, name)
            VALUES ('artist-1', 'The Moonlit Owls');
            INSERT INTO catalogue_albums (source_id, title, album_artist_source_id)
            VALUES ('album-1', 'Night Routes', 'artist-1');
            INSERT INTO catalogue_tracks (source_id, title, album_source_id)
            VALUES
                ('track-1', 'Lantern Signals', 'album-1'),
                ('track-2', 'Élan Vital', 'album-1');
            INSERT INTO catalogue_track_artists (track_source_id, artist_source_id)
            VALUES
                ('track-1', 'artist-1'),
                ('track-2', 'artist-1');
            """;
        if (includeSuccessfulState)
        {
            command.CommandText += """
                INSERT INTO catalogue_state (id) VALUES (1);
                INSERT INTO catalogue_refresh_runs (id, status, started_at)
                VALUES ('refresh-1', 'succeeded', '2026-08-15T12:00:00Z');
                """;
        }

        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }
}
