using LyrionVoiceMcp.Abstractions;
using LyrionVoiceMcp.Ef;
using LyrionVoiceMcp.Ef.Abstractions.Catalogue;
using LyrionVoiceMcp.Ef.Abstractions.DataAccess;
using LyrionVoiceMcp.Services;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace LyrionVoiceMcp.Persistence.Tests;

public sealed class EfCataloguePersistenceTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Now =
        DateTimeOffset.Parse("2026-08-18T12:00:00Z");

    private readonly string directory = Path.Combine(
        Path.GetTempPath(),
        $"lvm-ef-catalogue-{Guid.NewGuid():N}");
    private ServiceProvider serviceProvider = null!;
    private ICatalogueLifecycleService lifecycle = null!;
    private ICatalogueImportWriter writer = null!;
    private ICatalogueSearchDocumentSource documents = null!;

    public async ValueTask InitializeAsync()
    {
        var services = new ServiceCollection();
        services.AddLyrionVoiceMcpEf(new ApplicationDatabaseSettings(
            Path.Combine(directory, "application.db")));
        services.AddSingleton<TimeProvider>(new FixedTimeProvider(Now));
        services.AddTransient<ICatalogueLifecycleService, CatalogueLifecycleService>();
        services.AddTransient<ICatalogueImportWriter, CatalogueImportWriter>();
        services.AddTransient<ICatalogueSearchDocumentSource, CatalogueSearchDocumentSource>();
        serviceProvider = services.BuildServiceProvider();
        await serviceProvider.InitialiseLyrionVoiceMcpEfAsync(
            TestContext.Current.CancellationToken);
        lifecycle = serviceProvider.GetRequiredService<ICatalogueLifecycleService>();
        writer = serviceProvider.GetRequiredService<ICatalogueImportWriter>();
        documents = serviceProvider.GetRequiredService<ICatalogueSearchDocumentSource>();
    }

    public async ValueTask DisposeAsync()
    {
        if (serviceProvider is not null)
        {
            await serviceProvider.DisposeAsync();
        }

        SqliteConnection.ClearAllPools();
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task CompletedRefreshShouldPreserveMediaShapeAndStreamBoundedDocuments()
    {
        const string refreshId = "refresh-1";
        await lifecycle.BeginRefreshAsync(
            refreshId,
            Now,
            TestContext.Current.CancellationToken);
        await writer.WriteAlbumsAsync(
            refreshId,
            [new CatalogueImportAlbum(
                "album-1", "Fictional Signals", "artist-1", 2026, 1, false,
                "album", "track-1", "external-album-1")],
            TestContext.Current.CancellationToken);
        await writer.WriteGenresAsync(
            refreshId,
            [new CatalogueImportGenre("genre-1", "Imaginary Pop")],
            TestContext.Current.CancellationToken);
        await writer.WriteTracksAsync(
            refreshId,
            [
                CreateTrack("track-1", "Night Signal", "album-1", "artist-1", "genre-1"),
                CreateTrack("track-2", "Album Signal", "album-1", null, "genre-1") with
                {
                    Year = null
                }
            ],
            TestContext.Current.CancellationToken);
        await writer.WriteArtistsAsync(
            refreshId,
            [
                new CatalogueImportArtist("artist-1", "The Imaginaries", "external-artist-1"),
                new CatalogueImportArtist("unused-role-1", "Unused Person", null)
            ],
            TestContext.Current.CancellationToken);
        await writer.WriteVirtualLibrariesAsync(
            refreshId,
            [new CatalogueImportVirtualLibrary("library-1", "Evening Fiction")],
            TestContext.Current.CancellationToken);
        await writer.WriteVirtualLibraryTracksAsync(
            refreshId,
            "library-1",
            ["track-1"],
            TestContext.Current.CancellationToken);

        var completion = await lifecycle.CompleteRefreshAsync(
            refreshId,
            CreateSourceResult(refreshId, 2, 1, 1, 2, 1, 1),
            Now.AddMinutes(1),
            0,
            TestContext.Current.CancellationToken);
        var projected = await ReadDocumentsAsync(refreshId, 1);

        Assert.Equal(1, completion.Summary.ArtistCount);
        Assert.Equal(0, completion.Summary.WarningCount);
        Assert.DoesNotContain(projected, item => item.Title == "Unused Person");
        Assert.Contains(projected, item =>
            item.Identity.Kind == MediaEntityKind.Album
            && item.Title == "Fictional Signals"
            && item.Artist == "The Imaginaries"
            && item.ArtistIds is not null
            && item.ArtistIds.SequenceEqual(["artist-1"]));
        Assert.Contains(projected, item =>
            item.Identity.Kind == MediaEntityKind.Track
            && item.Title == "Night Signal"
            && item.Artist == "The Imaginaries"
            && item.Album == "Fictional Signals"
            && item.NativeRating == 80
            && item.Year == 2026
            && item.GenreKeys is not null
            && item.GenreKeys.SequenceEqual(["IMAGINARY POP"])
            && item.ArtistIds is not null
            && item.ArtistIds.SequenceEqual(["artist-1"]));
        Assert.Contains(projected, item =>
            item.Identity.Kind == MediaEntityKind.Track
            && item.Title == "Album Signal"
            && item.Artist == "The Imaginaries"
            && item.Album == "Fictional Signals"
            && item.Year == 2026
            && item.GenreKeys is not null
            && item.GenreKeys.SequenceEqual(["IMAGINARY POP"])
            && item.ArtistIds is not null
            && item.ArtistIds.SequenceEqual(["artist-1"]));

        var scopeFactory = serviceProvider.GetRequiredService<IDbContextScopeFactory>();
        var tracks = serviceProvider.GetRequiredService<ICatalogueTrackRepository>();
        using var scope = scopeFactory.CreateReadOnly();
        var track = Assert.Single(await tracks.ListForUpdateAsync(
            ["track-1"],
            TestContext.Current.CancellationToken));
        var statistic = Assert.Single(track.Statistics);
        Assert.Equal("Fictional subtitle", track.Subtitle);
        Assert.Equal("file:///fictional/track-1.flac", track.Url);
        Assert.Equal("work-1", track.WorkSourceId);
        Assert.Equal("Fictional Work", track.WorkTitle);
        Assert.Equal("Fictional Group", track.Grouping);
        Assert.Equal("artist-1", Assert.Single(track.Artists).ArtistSourceId);
        Assert.Equal("genre-1", Assert.Single(track.Genres).GenreSourceId);
        Assert.Equal(80, statistic.Rating);
        Assert.Equal(12, statistic.PlayCount);
        Assert.Equal(DateTime.Parse("2026-08-17T19:30:00Z").ToUniversalTime(),
            statistic.LastPlayedAtUtc);
    }

    [Fact]
    public async Task FailedRefreshShouldRetainPagesAndAReplacementRefreshShouldConverge()
    {
        await WriteSmallCatalogueAsync("refresh-old", "old", "Old Signal");
        await lifecycle.BeginRefreshAsync(
            "refresh-failed",
            Now.AddMinutes(2),
            TestContext.Current.CancellationToken);
        await writer.WriteTracksAsync(
            "refresh-failed",
            [CreateTrack("new", "New Signal", null, null, null)],
            TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            lifecycle.CompleteRefreshAsync(
                "refresh-failed",
                CreateSourceResult("refresh-failed", 0, 0, 0, 2, 0, 0),
                Now.AddMinutes(3),
                0,
                TestContext.Current.CancellationToken));
        await lifecycle.FinishRefreshAsync(
            "refresh-failed",
            CatalogueStateStatus.Failed,
            Now.AddMinutes(3),
            TestContext.Current.CancellationToken);
        var failed = await lifecycle.GetStateAsync(TestContext.Current.CancellationToken);

        Assert.Equal(CatalogueStateStatus.Failed, failed?.Status);
        Assert.Null(failed?.Summary);

        await WriteSmallCatalogueAsync("refresh-new", "new", "New Signal");
        var projected = await ReadDocumentsAsync("refresh-new", 500);
        Assert.DoesNotContain(projected, item => item.Identity.Id == "old");
        Assert.Contains(projected, item => item.Identity.Id == "new");
    }

    [Fact]
    public async Task RecoveryShouldInterruptAnAbandonedRefreshAndRejectOversizedProjectionBatches()
    {
        await lifecycle.BeginRefreshAsync(
            "refresh-abandoned",
            Now,
            TestContext.Current.CancellationToken);
        await lifecycle.RecoverInterruptedRefreshAsync(TestContext.Current.CancellationToken);

        var state = await lifecycle.GetStateAsync(TestContext.Current.CancellationToken);
        Assert.Equal(CatalogueStateStatus.Interrupted, state?.Status);
        Assert.Equal(Now, state?.CompletedAt);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
        {
            await foreach (var _ in documents.ReadBatchesAsync(
                "refresh-abandoned",
                501,
                TestContext.Current.CancellationToken))
            {
            }
        });
    }

    [Fact]
    public async Task MissingCrossCatalogueTargetsShouldCompleteWithReferentialWarnings()
    {
        const string refreshId = "refresh-warnings";
        await lifecycle.BeginRefreshAsync(
            refreshId,
            Now,
            TestContext.Current.CancellationToken);
        await writer.WriteTracksAsync(
            refreshId,
            [CreateTrack("track-1", "Unresolved Signal", "missing-album", "missing-artist", "missing-genre")],
            TestContext.Current.CancellationToken);
        await writer.WriteVirtualLibrariesAsync(
            refreshId,
            [new CatalogueImportVirtualLibrary("library-1", "Unresolved Fiction")],
            TestContext.Current.CancellationToken);
        await writer.WriteVirtualLibraryTracksAsync(
            refreshId,
            "library-1",
            ["missing-track"],
            TestContext.Current.CancellationToken);

        var completion = await lifecycle.CompleteRefreshAsync(
            refreshId,
            CreateSourceResult(refreshId, 0, 0, 0, 1, 1, 1),
            Now.AddMinutes(1),
            0,
            TestContext.Current.CancellationToken);

        Assert.Equal(4, completion.Summary.WarningCount);
        Assert.Equal(4, completion.Warnings.Count);
        Assert.All(completion.Warnings, warning => Assert.Equal(1, warning.ProcessedCount));
    }

    private async Task WriteSmallCatalogueAsync(
        string refreshId,
        string trackId,
        string title)
    {
        await lifecycle.BeginRefreshAsync(
            refreshId,
            Now,
            TestContext.Current.CancellationToken);
        await writer.WriteTracksAsync(
            refreshId,
            [CreateTrack(trackId, title, null, null, null)],
            TestContext.Current.CancellationToken);
        await lifecycle.CompleteRefreshAsync(
            refreshId,
            CreateSourceResult(refreshId, 0, 0, 0, 1, 0, 0),
            Now.AddMinutes(1),
            0,
            TestContext.Current.CancellationToken);
    }

    private async Task<IReadOnlyList<CatalogueSearchDocument>> ReadDocumentsAsync(
        string refreshId,
        int batchSize)
    {
        var results = new List<CatalogueSearchDocument>();
        await foreach (var batch in documents.ReadBatchesAsync(
            refreshId,
            batchSize,
            TestContext.Current.CancellationToken))
        {
            Assert.InRange(batch.Documents.Count, 1, batchSize);
            results.AddRange(batch.Documents);
        }

        return results;
    }

    private static CatalogueSourceReadResult CreateSourceResult(
        string revision,
        int artistLookups,
        int albums,
        int genres,
        int tracks,
        int libraries,
        int libraryTracks) => new(
        new CatalogueImportSource("fictional", "lms", "1.0", revision),
        Now,
        null,
        artistLookups,
        albums,
        genres,
        tracks,
        libraries,
        libraries == 0
            ? []
            : [new CatalogueImportVirtualLibraryMembership("library-1", libraryTracks)]);

    private static CatalogueImportTrack CreateTrack(
        string sourceId,
        string title,
        string? albumSourceId,
        string? artistSourceId,
        string? genreSourceId) => new(
        sourceId,
        title,
        "Fictional subtitle",
        $"file:///fictional/{sourceId}.flac",
        "flc",
        false,
        $"external-{sourceId}",
        albumSourceId,
        2026,
        1,
        1,
        1,
        240,
        1_024,
        48_000,
        Now.AddDays(-10),
        Now.AddDays(-2),
        Now.AddDays(-1),
        "album",
        false,
        sourceId,
        "work-1",
        "Fictional Work",
        "Studio",
        "Fictional Group",
        artistSourceId is null ? [] : [artistSourceId],
        genreSourceId is null ? [] : [genreSourceId],
        [new CatalogueImportTrackStatistics(
            "lms-core",
            80,
            12,
            DateTimeOffset.Parse("2026-08-17T19:30:00Z"))]);

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
