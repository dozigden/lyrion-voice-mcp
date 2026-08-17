using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LyrionVoiceMcp.Contracts;
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
    public async Task DiscoveryShouldAdvertiseOnlyTheProductionResolver()
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
        Assert.Equal(
            "production",
            Assert.Single(document.RootElement.GetProperty("resolvers").EnumerateArray())
                .GetString());
    }

    [Fact]
    public async Task SearchShouldUseTheDeployedCatalogueAndReturnDiagnostics()
    {
        using var client = factory.CreateClient();
        await SeedCatalogueAsync();
        using var rebuildResponse = await client.PostAsync(
            "/api/search/index/rebuild",
            null,
            TestContext.Current.CancellationToken);
        var rebuild = await rebuildResponse.Content.ReadFromJsonAsync<SearchIndexStatusResponse>(
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Accepted, rebuildResponse.StatusCode);
        Assert.NotNull(rebuild?.LatestJob);
        await WaitForJobAsync(client, rebuild.LatestJob.Id);

        using var response = await client.PostAsJsonAsync(
            "/api/evaluation/search",
            new { resolver = "production", query = "Nite" },
            TestContext.Current.CancellationToken);
        using var document = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(TestContext.Current.CancellationToken),
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(document.RootElement.GetProperty("resolverPreparedForThisRequest").GetBoolean());
        var search = document.RootElement.GetProperty("search");
        Assert.Equal("Knight", search.GetProperty("results")[0].GetProperty("title").GetString());
        Assert.Equal("artist", search.GetProperty("results")[0].GetProperty("kind").GetString());
        Assert.True(Directory.Exists(factory.SearchIndexDirectoryPath));
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

    [Fact]
    public async Task ProductionDiagnosticsShouldReportAnUnbuiltIndexExplicitly()
    {
        using var isolatedFactory = new LyrionVoiceMcpApiFactory();
        using var client = isolatedFactory.CreateClient();

        using var status = await client.GetAsync(
            "/api/search/index",
            TestContext.Current.CancellationToken);
        var index = await status.Content.ReadFromJsonAsync<SearchIndexStatusResponse>(
            TestContext.Current.CancellationToken);
        using var search = await client.PostAsJsonAsync(
            "/api/evaluation/search",
            new { resolver = "production", query = "Fictional Signal" },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, status.StatusCode);
        Assert.Equal("catalogue-phuzzy-sqlite", index?.Resolver);
        Assert.Null(index?.Artifact);
        Assert.Equal(HttpStatusCode.Conflict, search.StatusCode);
    }

    private async Task SeedCatalogueAsync()
    {
        await using var connection = new SqliteConnection($"Data Source={factory.CataloguePath}");
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO catalogue_state (
                id, refresh_id, refresh_status, refresh_started_at, refresh_completed_at,
                source_id, source_provider, source_version, source_revision,
                captured_at, source_last_scan_at, refreshed_at, artist_count,
                album_count, genre_count, track_count, virtual_library_count, warning_count)
            VALUES (
                1, 'refresh-1', 'succeeded', '2026-08-15T12:00:00Z', '2026-08-15T12:00:01Z',
                'fictional', 'lms', '1.0', 'revision-1',
                '2026-08-15T12:00:00Z', NULL, '2026-08-15T12:00:01Z',
                1, 0, 0, 0, 0, 0);

            INSERT INTO catalogue_artists (source_id, name, external_id, seen_refresh_id)
            VALUES ('artist-1', 'Knight', NULL, 'refresh-1');
            """;
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    private static async Task WaitForJobAsync(HttpClient client, long jobId)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            timeout.Token,
            TestContext.Current.CancellationToken);
        while (true)
        {
            var details = await client.GetFromJsonAsync<JobDetailsResponse>(
                $"/api/jobs/{jobId}",
                linked.Token);
            if (details?.Job.Status == "completed")
            {
                return;
            }

            Assert.NotEqual("failed", details?.Job.Status);
            await Task.Delay(25, linked.Token);
        }
    }
}
