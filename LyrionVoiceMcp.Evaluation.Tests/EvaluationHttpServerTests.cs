using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace LyrionVoiceMcp.Evaluation.Tests;

public sealed class EvaluationHttpServerTests : IDisposable
{
    private readonly string directory = Path.Combine(
        Path.GetTempPath(),
        $"lyrion-voice-http-evaluation-{Guid.NewGuid():N}");
    private readonly string cataloguePath;

    public EvaluationHttpServerTests()
    {
        cataloguePath = Path.Combine(directory, "catalogue.db");
        Directory.CreateDirectory(directory);
    }

    [Fact]
    public async Task Search_endpoint_returns_complete_diagnostic_results()
    {
        await CreateCatalogueAsync();
        var options = new EvaluationServerArgumentsParsed(
            cataloguePath,
            Path.Combine(directory, "indexes"),
            "http://127.0.0.1:0");
        await using var application = EvaluationHttpServer.BuildApplication(options);
        await application.StartAsync(TestContext.Current.CancellationToken);
        var server = application.Services.GetRequiredService<IServer>();
        var addresses = server.Features.Get<IServerAddressesFeature>();
        var address = Assert.Single(addresses!.Addresses);
        using var client = new HttpClient { BaseAddress = new Uri(address) };

        using var response = await client.PostAsJsonAsync(
            "/api/evaluation/search",
            new EvaluationHttpSearchRequest("catalogue-phuzzy-indexed", "Nite"),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await using var content = await response.Content.ReadAsStreamAsync(
            TestContext.Current.CancellationToken);
        using var document = await JsonDocument.ParseAsync(
            content,
            cancellationToken: TestContext.Current.CancellationToken);
        var root = document.RootElement;
        Assert.True(root.GetProperty("resolverPreparedForThisRequest").GetBoolean());
        var search = root.GetProperty("search");
        Assert.Equal(
            search.GetProperty("retrievedCandidateCount").GetInt32(),
            search.GetProperty("results").GetArrayLength());
        Assert.Equal(
            "Knight",
            search.GetProperty("results")[0].GetProperty("title").GetString());
        Assert.Equal(
            "double_metaphone",
            search.GetProperty("results")[0]
                .GetProperty("scoreEvidence")
                .GetProperty("signal")
                .GetString());
        var lane = search.GetProperty("lanes").EnumerateArray().First();
        Assert.True(lane.TryGetProperty("matchedCandidateCount", out _));
        Assert.True(lane.TryGetProperty("retrievedCandidateCount", out _));

        using var cachedResponse = await client.PostAsJsonAsync(
            "/api/evaluation/search",
            new EvaluationHttpSearchRequest("catalogue-phuzzy-indexed", "Nite"),
            TestContext.Current.CancellationToken);
        using var cachedDocument = await JsonDocument.ParseAsync(
            await cachedResponse.Content.ReadAsStreamAsync(TestContext.Current.CancellationToken),
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.False(
            cachedDocument.RootElement
                .GetProperty("resolverPreparedForThisRequest")
                .GetBoolean());
    }

    [Fact]
    public async Task Search_endpoint_rejects_an_unknown_resolver()
    {
        await CreateCatalogueAsync();
        var options = new EvaluationServerArgumentsParsed(
            cataloguePath,
            Path.Combine(directory, "indexes"),
            "http://127.0.0.1:0");
        await using var application = EvaluationHttpServer.BuildApplication(options);
        await application.StartAsync(TestContext.Current.CancellationToken);
        var server = application.Services.GetRequiredService<IServer>();
        var addresses = server.Features.Get<IServerAddressesFeature>();
        var address = Assert.Single(addresses!.Addresses);
        using var client = new HttpClient { BaseAddress = new Uri(address) };

        using var response = await client.PostAsJsonAsync(
            "/api/evaluation/search",
            new EvaluationHttpSearchRequest("not-a-resolver", "Nite"),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Search_executor_serialises_complete_measurement_windows()
    {
        var resolver = new BlockingDiagnosticResolver();
        var provider = new StubResolverProvider(resolver);
        using var executor = new EvaluationSearchExecutor(provider);
        var request = new EvaluationHttpSearchRequest("test", "query");

        var first = executor.ExecuteAsync(request, TestContext.Current.CancellationToken);
        Assert.Equal(1, resolver.EntryCount);
        var second = executor.ExecuteAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(1, resolver.EntryCount);
        resolver.ReleaseFirst();
        await Task.WhenAll(first, second);
        Assert.Equal(2, resolver.EntryCount);
        Assert.Equal(1, resolver.MaximumConcurrency);
    }

    [Fact]
    public async Task Application_disposal_disposes_the_resolver_provider()
    {
        var options = new EvaluationServerArgumentsParsed(
            cataloguePath,
            Path.Combine(directory, "indexes"),
            "http://127.0.0.1:0");
        var application = EvaluationHttpServer.BuildApplication(options);
        var provider = application.Services
            .GetRequiredService<IEvaluationDiagnosticResolverProvider>();

        await application.DisposeAsync();

        await Assert.ThrowsAsync<ObjectDisposedException>(() => provider.GetAsync(
            "catalogue-phuzzy-indexed",
            TestContext.Current.CancellationToken));
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
                ('artist-1', 'Knight'),
                ('artist-2', 'ZYRAQ');
            """;
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    private sealed class StubResolverProvider(IEvaluationDiagnosticSearchResolver resolver)
        : IEvaluationDiagnosticResolverProvider
    {
        public Task<ResolvedDiagnosticResolver> GetAsync(
            string name,
            CancellationToken cancellationToken) =>
            Task.FromResult(new ResolvedDiagnosticResolver(resolver, false));
    }

    private sealed class BlockingDiagnosticResolver : IEvaluationDiagnosticSearchResolver
    {
        private readonly TaskCompletionSource releaseFirst = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private int activeCount;
        private int entryCount;
        private int maximumConcurrency;

        public string Name => "test";
        public string Version => "1";
        public EvaluationResolverMetrics Metrics { get; } = new(0, 0, 0);
        public int EntryCount => entryCount;
        public int MaximumConcurrency => maximumConcurrency;

        public Task<EvaluationSearchResponse> SearchAsync(
            string query,
            CancellationToken cancellationToken) =>
            Task.FromResult(new EvaluationSearchResponse([], null));

        public async Task<EvaluationDiagnosticSearchResponse> SearchDetailedAsync(
            string query,
            CancellationToken cancellationToken)
        {
            var entry = Interlocked.Increment(ref entryCount);
            var active = Interlocked.Increment(ref activeCount);
            InterlockedMax(ref maximumConcurrency, active);
            try
            {
                if (entry == 1)
                {
                    await releaseFirst.Task.WaitAsync(cancellationToken);
                }

                return new EvaluationDiagnosticSearchResponse(
                    Name,
                    Version,
                    Metrics,
                    0,
                    0,
                    0,
                    0,
                    [],
                    []);
            }
            finally
            {
                Interlocked.Decrement(ref activeCount);
            }
        }

        public void ReleaseFirst() => releaseFirst.SetResult();

        private static void InterlockedMax(ref int location, int value)
        {
            var current = Volatile.Read(ref location);
            while (value > current)
            {
                var observed = Interlocked.CompareExchange(ref location, value, current);
                if (observed == current)
                {
                    return;
                }

                current = observed;
            }
        }
    }
}
