using LyrionVoiceMcp.Abstractions;
using Microsoft.Data.Sqlite;

namespace LyrionVoiceMcp.Evaluation.Tests;

public sealed class CataloguePhuzzySearchResolverTests : IDisposable
{
    private readonly string directory = Path.Combine(
        Path.GetTempPath(),
        $"lyrion-voice-phuzzy-evaluation-{Guid.NewGuid():N}");
    private readonly string databasePath;

    public CataloguePhuzzySearchResolverTests()
    {
        databasePath = Path.Combine(directory, "catalogue.db");
        Directory.CreateDirectory(directory);
    }

    [Theory]
    [InlineData("seemoth", MediaEntityKind.Artist, "CMOTH")]
    [InlineData("Taddy Meer", MediaEntityKind.Artist, "Taði Mýr")]
    [InlineData("some noise Paper Comets now", MediaEntityKind.Artist, "Paper Comets")]
    [InlineData("Ellie Fable", MediaEntityKind.Album, "Elephable")]
    public async Task SearchAsync_matches_voice_tolerant_forms(
        string query,
        MediaEntityKind expectedKind,
        string expectedTitle)
    {
        await CreateCatalogueAsync();
        var resolver = await CataloguePhuzzySearchResolver.CreateAsync(
            databasePath,
            TestContext.Current.CancellationToken);

        var response = await resolver.SearchAsync(
            query,
            TestContext.Current.CancellationToken);

        var first = Assert.IsType<EvaluationSearchCandidate>(response.Candidates.First());
        Assert.Equal(expectedKind, first.Kind);
        Assert.Equal(expectedTitle, first.Title);
        Assert.Null(response.Error);
    }

    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private async Task CreateCatalogueAsync()
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
            INSERT INTO catalogue_state (id) VALUES (1);
            INSERT INTO catalogue_refresh_runs (id, status, started_at)
            VALUES ('refresh-1', 'succeeded', '2026-08-15T12:00:00Z');
            INSERT INTO catalogue_artists (source_id, name)
            VALUES
                ('artist-1', 'CMOTH'),
                ('artist-2', 'Taði Mýr'),
                ('artist-3', 'Paper Comets');
            INSERT INTO catalogue_albums (source_id, title, album_artist_source_id)
            VALUES ('album-1', 'Elephable', 'artist-3');
            INSERT INTO catalogue_tracks (source_id, title, album_source_id)
            VALUES
                ('track-1', 'Lantern Signals', 'album-1'),
                ('track-2', 'Taddy', NULL),
                ('track-3', 'Ellie', NULL),
                ('track-4', 'Fable', NULL);
            INSERT INTO catalogue_track_artists (track_source_id, artist_source_id)
            VALUES ('track-1', 'artist-3');
            """;
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }
}
