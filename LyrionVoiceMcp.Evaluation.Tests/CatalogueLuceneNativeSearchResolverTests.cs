using LyrionVoiceMcp.Abstractions;
using Microsoft.Data.Sqlite;

namespace LyrionVoiceMcp.Evaluation.Tests;

public sealed class CatalogueLuceneNativeSearchResolverTests : IDisposable
{
    private readonly string directory = Path.Combine(
        Path.GetTempPath(),
        $"lyrion-voice-lucene-native-evaluation-{Guid.NewGuid():N}");
    private readonly string cataloguePath;
    private readonly string indexPath;

    public CatalogueLuceneNativeSearchResolverTests()
    {
        cataloguePath = Path.Combine(directory, "catalogue.db");
        indexPath = Path.Combine(directory, "catalogue.lucene-native-index");
        Directory.CreateDirectory(directory);
    }

    [Theory]
    [InlineData("seemoth", MediaEntityKind.Artist, "CMOTH")]
    [InlineData("Taddy Meer", MediaEntityKind.Artist, "Taði Mýr")]
    [InlineData("some noise Paper Comets now", MediaEntityKind.Artist, "Paper Comets")]
    [InlineData("Kngiht", MediaEntityKind.Artist, "Knight")]
    public async Task SearchAsync_uses_native_lucene_queries_and_field_ranking(
        string query,
        MediaEntityKind expectedKind,
        string expectedTitle)
    {
        await CreateCatalogueAsync();
        using var resolver = await CatalogueLuceneNativeSearchResolver.CreateAsync(
            cataloguePath,
            indexPath,
            TestContext.Current.CancellationToken);

        var response = await resolver.SearchAsync(
            query,
            TestContext.Current.CancellationToken);

        var first = Assert.IsType<EvaluationSearchCandidate>(response.Candidates.First());
        Assert.Equal(expectedKind, first.Kind);
        Assert.Equal(expectedTitle, first.Title);
        Assert.True(resolver.Metrics.IndexSizeBytes > 0);
        Assert.Null(response.Error);
    }

    [Fact]
    public async Task SearchAsync_phrase_slop_breaks_a_token_match_tie()
    {
        await CreateCatalogueAsync();
        using var resolver = await CatalogueLuceneNativeSearchResolver.CreateAsync(
            cataloguePath,
            indexPath,
            TestContext.Current.CancellationToken);

        var response = await resolver.SearchAsync(
            "Velvet Signals",
            TestContext.Current.CancellationToken);

        var first = Assert.IsType<EvaluationSearchCandidate>(response.Candidates.First());
        Assert.Equal("Velvet Radio Signals", first.Title);
    }

    [Fact]
    public async Task SearchDetailedAsync_returns_native_score_and_single_query_measurement()
    {
        await CreateCatalogueAsync();
        using var resolver = await CatalogueLuceneNativeSearchResolver.CreateAsync(
            cataloguePath,
            indexPath,
            TestContext.Current.CancellationToken);

        var response = await resolver.SearchDetailedAsync(
            "Kngiht",
            TestContext.Current.CancellationToken);

        var first = Assert.IsType<EvaluationDiagnosticCandidate>(response.Results.First());
        Assert.Equal("Knight", first.Title);
        Assert.Equal(["native_query"], first.RetrievalLanes);
        var evidence = Assert.IsType<EvaluationScoreEvidence>(first.ScoreEvidence);
        Assert.Equal("native_lucene_score", evidence.Signal);
        Assert.Equal(first.Score, evidence.FinalScore);
        var lane = Assert.Single(response.Lanes);
        Assert.Equal("native_query", lane.Name);
        Assert.Equal(response.Results.Count, lane.RetrievedCandidateCount);
        Assert.Equal(0, response.RerankDurationMilliseconds);
    }

    [Fact]
    public async Task SearchAsync_supports_the_endpoint_twenty_word_limit()
    {
        await CreateCatalogueAsync();
        using var resolver = await CatalogueLuceneNativeSearchResolver.CreateAsync(
            cataloguePath,
            indexPath,
            TestContext.Current.CancellationToken);
        var query = string.Join(' ', Enumerable.Range(1, 20).Select(index => $"word{index}"));

        var response = await resolver.SearchAsync(
            query,
            TestContext.Current.CancellationToken);

        Assert.NotNull(response.Candidates);
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
        await using var connection = new SqliteConnection($"Data Source={cataloguePath}");
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
                ('artist-3', 'Paper Comets'),
                ('artist-4', 'Knight');
            INSERT INTO catalogue_albums (source_id, title, album_artist_source_id)
            VALUES ('album-1', 'Elephable', 'artist-3');
            INSERT INTO catalogue_tracks (source_id, title, album_source_id)
            VALUES
                ('track-1', 'Lantern Signals', 'album-1'),
                ('track-2', 'Velvet Radio Signals', NULL),
                ('track-3', 'Velvet Very Long Radio Signals', NULL),
                ('track-4', 'Summit', NULL),
                ('track-5', 'Taddy', NULL),
                ('track-6', 'Taddy Again', NULL);
            INSERT INTO catalogue_track_artists (track_source_id, artist_source_id)
            VALUES ('track-1', 'artist-3');
            """;
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }
}
