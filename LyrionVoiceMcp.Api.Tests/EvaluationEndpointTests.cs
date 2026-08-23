using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LyrionVoiceMcp.Abstractions;
using LyrionVoiceMcp.Api.Diagnostics;
using LyrionVoiceMcp.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace LyrionVoiceMcp.Api.Tests;

public sealed class EvaluationEndpointTests : IClassFixture<LyrionVoiceMcpApiFactory>
{
    private readonly LyrionVoiceMcpApiFactory factory;

    public EvaluationEndpointTests(LyrionVoiceMcpApiFactory factory)
    {
        this.factory = factory;
    }

    [Fact]
    public void DiagnosticValidationShouldApplyRawInputLengthLimitsBeforeTrimming()
    {
        var oversizedQuery = ProductionSearchDiagnosticValidation.Validate(
            new ProductionSearchDiagnosticRequest(
                "production",
                Query: new string(' ', 500) + "x"));
        var oversizedGenre = ProductionSearchDiagnosticValidation.Validate(
            new ProductionSearchDiagnosticRequest(
                "production",
                Genre: new string(' ', 500) + "x"));

        Assert.Contains(
            "500 characters",
            Assert.IsType<string>(oversizedQuery),
            StringComparison.Ordinal);
        Assert.Contains(
            "500 characters",
            Assert.IsType<string>(oversizedGenre),
            StringComparison.Ordinal);
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
        Assert.Equal(3, document.RootElement.GetProperty("schemaVersion").GetInt32());
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
        using var scope = factory.Services.CreateScope();
        var lifecycle = scope.ServiceProvider.GetRequiredService<ICatalogueLifecycleService>();
        var writer = scope.ServiceProvider.GetRequiredService<ICatalogueImportWriter>();
        var startedAt = DateTimeOffset.Parse("2026-08-15T12:00:00Z");
        await lifecycle.BeginRefreshAsync(
            "refresh-1",
            startedAt,
            TestContext.Current.CancellationToken);
        await writer.WriteAlbumsAsync(
            "refresh-1",
            [new CatalogueImportAlbum(
                "album-1", "Fictional Night", "artist-1", 2026, 1, false,
                null, null, null)],
            TestContext.Current.CancellationToken);
        await writer.WriteArtistsAsync(
            "refresh-1",
            [new CatalogueImportArtist("artist-1", "Knight", null)],
            TestContext.Current.CancellationToken);
        await lifecycle.CompleteRefreshAsync(
            "refresh-1",
            new CatalogueSourceReadResult(
                new CatalogueImportSource("fictional", "lms", "1.0", "revision-1"),
                startedAt,
                null,
                1,
                1,
                0,
                0,
                0,
                []),
            startedAt.AddSeconds(1),
            0,
            TestContext.Current.CancellationToken);
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
