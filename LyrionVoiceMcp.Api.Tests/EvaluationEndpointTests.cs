using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace LyrionVoiceMcp.Api.Tests;

public sealed class EvaluationEndpointTests : IClassFixture<LyrionVoiceMcpApiFactory>
{
    private readonly LyrionVoiceMcpApiFactory factory;

    public EvaluationEndpointTests(LyrionVoiceMcpApiFactory factory)
    {
        this.factory = factory;
    }

    [Fact]
    public async Task DiscoveryShouldAdvertiseTheDeployedComparators()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(
            "/api/evaluation",
            TestContext.Current.CancellationToken);
        using var document = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(TestContext.Current.CancellationToken),
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, document.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.Contains(
            document.RootElement.GetProperty("resolvers").EnumerateArray(),
            item => item.GetString() == "catalogue-phuzzy-indexed");
    }

    [Fact]
    public async Task SearchShouldUseTheDeployedCatalogueAndReturnDiagnostics()
    {
        using var client = factory.CreateClient();
        await SeedCatalogueAsync();

        using var response = await client.PostAsJsonAsync(
            "/api/evaluation/search",
            new { resolver = "catalogue-phuzzy-indexed", query = "Nite" },
            TestContext.Current.CancellationToken);
        using var document = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(TestContext.Current.CancellationToken),
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(document.RootElement.GetProperty("resolverPreparedForThisRequest").GetBoolean());
        var search = document.RootElement.GetProperty("search");
        Assert.Equal("Knight", search.GetProperty("results")[0].GetProperty("title").GetString());
        Assert.Equal("artist", search.GetProperty("results")[0].GetProperty("kind").GetString());
        Assert.True(Directory.Exists(factory.EvaluationIndexDirectoryPath));
    }

    [Fact]
    public async Task SearchShouldRejectAnUnknownResolver()
    {
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync(
            "/api/evaluation/search",
            new { resolver = "not-a-resolver", query = "Nite" },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private async Task SeedCatalogueAsync()
    {
        await using var connection = new SqliteConnection($"Data Source={factory.CataloguePath}");
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO catalogue_refresh_runs (
                id, status, started_at, completed_at, duration_ms, failure_message)
            VALUES (
                'refresh-1', 'succeeded', '2026-08-15T12:00:00Z',
                '2026-08-15T12:00:01Z', 1000, NULL);

            INSERT INTO catalogue_state (
                id, source_id, source_provider, source_version, source_revision,
                captured_at, source_last_scan_at, refreshed_at, artist_count,
                album_count, genre_count, track_count, virtual_library_count, warning_count)
            VALUES (
                1, 'fictional', 'lms', '1.0', 'revision-1',
                '2026-08-15T12:00:00Z', NULL, '2026-08-15T12:00:01Z',
                1, 0, 0, 0, 0, 0);

            INSERT INTO catalogue_artists (source_id, name, external_id, seen_refresh_id)
            VALUES ('artist-1', 'Knight', NULL, 'refresh-1');
            """;
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }
}
