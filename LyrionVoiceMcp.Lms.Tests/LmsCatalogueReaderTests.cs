using System.Net;
using System.Text;
using System.Text.Json;
using LyrionVoiceMcp.Abstractions;
using LyrionVoiceMcp.Lms;

namespace LyrionVoiceMcp.Lms.Tests;

public sealed class LmsCatalogueReaderTests
{
    private static readonly DateTimeOffset CapturedAt =
        new(2026, 8, 15, 12, 30, 0, TimeSpan.Zero);

    [Fact]
    public async Task ReadShouldWriteSupportedCataloguePagesAndRelationships()
    {
        // Arrange
        var handler = new CatalogueHandler(command => command[0] switch
        {
            "serverstatus" =>
                """
                {"id":1,"result":{"version":"9.1.2","lastscan":"1786379003","info total artists":3,"info total albums":1,"info total genres":2,"info total songs":1}}
                """,
            "artists" =>
                """
                {"id":1,"result":{"count":3,"artists_loop":[
                  {"id":11,"artist":"The Glass Harbours","extid":"artist:glass-harbours"},
                  {"id":12,"artist":"Orla Meridian"},
                  {"id":13,"artist":"Rowan Almanac"}
                ]}}
                """,
            "albums" =>
                """
                {"id":1,"result":{"count":1,"albums_loop":[
                  {"id":21,"title":"Compass Weather","artist_id":12,"year":"2025","disccount":2,"compilation":"0","release_type":"ALBUM","artwork_track_id":31}
                ]}}
                """,
            "genres" =>
                """
                {"id":1,"result":{"count":2,"genres_loop":[
                  {"id":41,"genre":"Maritime Pop"},
                  {"id":42,"genre":"Night Folk"}
                ]}}
                """,
            "titles" when command.Any(value => value == "library_id:51") =>
                """{"id":1,"result":{"count":1,"titles_loop":[{"id":31}]}}""",
            "titles" =>
                """
                {"id":1,"result":{"count":1,"titles_loop":[{
                  "id":31,"title":"Lantern Almanac","subtitle":"Harbour version",
                  "url":"file:///music/Glass%20Harbours/Lantern%20Almanac.flac","type":"flc","remote":"0",
                  "album_id":21,"year":2025,"disc":1,"disccount":2,"tracknum":3,"duration":"241.5",
                  "filesize":"42000000","samplerate":"96000","addedTime":"1786000000",
                  "modificationTime":"1785000000","lastUpdated":"1786100000","release_type":"ALBUM",
                  "compilation":"0","artwork_track_id":31,"work_id":61,"work":"Northern Bearings",
                  "performance":"Live at Low Water","grouping":"Tidal pieces","artist_ids":"11",
                  "composer_ids":"13","genre_ids":"41,42","rating":"80","playcount":"7"
                }]}}
                """,
            "libraries" =>
                """{"id":1,"result":{"folder_loop":[{"id":51,"name":"Evening Navigation"}]}}""",
            _ => throw new InvalidOperationException($"Unexpected command {string.Join(' ', command)}")
        });
        using var httpClient = new HttpClient(handler);
        var writer = new RecordingWriter();
        var reader = CreateReader(httpClient);

        // Act
        var result = await reader.ReadAsync(
            "refresh-1",
            writer,
            new RecordingCatalogueLogSink(),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("development", result.Source.Id);
        Assert.Equal("9.1.2", result.Source.Version);
        Assert.Equal(CapturedAt, result.CapturedAt);
        Assert.Equal(3, result.ArtistLookupCount);
        Assert.Equal(1, result.TrackCount);
        Assert.Equal(
            new CatalogueImportVirtualLibraryMembership("51", 1),
            Assert.Single(result.VirtualLibraryMemberships));
        Assert.Equal(3, writer.Artists.Count);
        Assert.Equal("13", writer.Artists[2].SourceId);
        Assert.Equal("12", Assert.Single(writer.Albums).AlbumArtistSourceId);
        var track = Assert.Single(writer.Tracks);
        Assert.Equal(["11"], track.ArtistSourceIds);
        Assert.Equal(["41", "42"], track.GenreSourceIds);
        Assert.Equal(80, Assert.Single(track.Statistics).Rating);
        Assert.Equal(["31"], writer.LibraryTracks["51"]);
        Assert.Contains(
            handler.Commands,
            command => command[0] == "titles"
                && command.Any(value => value.StartsWith("tags:", StringComparison.Ordinal)
                    && value.Contains('R')));
        Assert.Contains(
            handler.Commands,
            command => command.SequenceEqual(
                ["titles", "0", "500", "library_id:51", "tags:II"]));
    }

    [Fact]
    public async Task ReadShouldPassEachLmsPageToTheWriterWithoutCombiningThem()
    {
        // Arrange
        var handler = new CatalogueHandler(command => command[0] switch
        {
            "serverstatus" => StatusResponse(501, 0, 0, 0),
            "artists" => ArtistsPage(int.Parse(command[1], System.Globalization.CultureInfo.InvariantCulture)),
            "albums" or "genres" or "titles" => EmptyCountedResponse(),
            "libraries" => """{"id":1,"result":{"folder_loop":[]}}""",
            _ => throw new InvalidOperationException($"Unexpected command {string.Join(' ', command)}")
        });
        using var httpClient = new HttpClient(handler);
        var writer = new RecordingWriter();

        // Act
        await CreateReader(httpClient).ReadAsync(
            "refresh-1",
            writer,
            new RecordingCatalogueLogSink(),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal([500, 1], writer.ArtistBatchSizes);
        Assert.Contains(
            handler.Commands,
            command => command.SequenceEqual(["artists", "500", "500", "tags:E"]));
    }

    [Fact]
    public async Task ReadShouldStopBeforeEntityQueriesWhileLmsIsScanning()
    {
        // Arrange
        var handler = new CatalogueHandler(command =>
            command[0] == "serverstatus"
                ? """{"id":1,"result":{"rescan":"1","lastscan":"1786379003"}}"""
                : throw new InvalidOperationException("The reader should not continue while scanning."));
        using var httpClient = new HttpClient(handler);

        // Act
        var exception = await Assert.ThrowsAsync<LmsRequestException>(() =>
            CreateReader(httpClient).ReadAsync(
                "refresh-1",
                new RecordingWriter(),
                new RecordingCatalogueLogSink(),
                TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(
            "LMS catalogue cannot be read while a library scan is in progress.",
            exception.Message);
        Assert.Single(handler.Commands);
    }

    [Fact]
    public async Task ReadShouldRejectRatingOutsideTheLmsScale()
    {
        // Arrange
        var handler = new CatalogueHandler(command => command[0] switch
        {
            "serverstatus" => StatusResponse(0, 0, 0, 1),
            "artists" or "albums" or "genres" => EmptyCountedResponse(),
            "titles" =>
                """
                {"id":1,"result":{"count":1,"titles_loop":[{
                  "id":31,"title":"Impossible Stars","url":"file:///music/Impossible%20Stars.flac","remote":"0","rating":101
                }]}}
                """,
            _ => throw new InvalidOperationException($"Unexpected command {string.Join(' ', command)}")
        });
        using var httpClient = new HttpClient(handler);

        // Act
        var exception = await Assert.ThrowsAsync<LmsRequestException>(() =>
            CreateReader(httpClient).ReadAsync(
                "refresh-1",
                new RecordingWriter(),
                new RecordingCatalogueLogSink(),
                TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(
            "LMS tracks response contained a rating outside the 0 to 100 range.",
            exception.Message);
    }

    private static LmsCatalogueReader CreateReader(HttpClient httpClient)
    {
        var settings = LmsConnectionSettings.FromValues(
            "development",
            "http://music.test:9000",
            null);
        return new LmsCatalogueReader(
            new LmsJsonRpcClient(settings, httpClient),
            settings,
            new FixedTimeProvider(CapturedAt));
    }

    private static string StatusResponse(int artists, int albums, int genres, int tracks) =>
        JsonSerializer.Serialize(new
        {
            id = 1,
            result = new Dictionary<string, object?>
            {
                ["version"] = "9.1.2",
                ["lastscan"] = "1786379003",
                ["info total artists"] = artists,
                ["info total albums"] = albums,
                ["info total genres"] = genres,
                ["info total songs"] = tracks
            }
        });

    private static string EmptyCountedResponse() => """{"id":1,"result":{"count":0}}""";

    private static string ArtistsPage(int offset)
    {
        var items = offset == 0
            ? Enumerable.Range(1, 500)
            : Enumerable.Range(501, 1);
        return JsonSerializer.Serialize(new
        {
            id = 1,
            result = new
            {
                count = 501,
                artists_loop = items.Select(id => new { id, artist = $"Fictional Artist {id}" })
            }
        });
    }

    private sealed class RecordingWriter : ICatalogueImportWriter
    {
        public List<CatalogueImportArtist> Artists { get; } = [];
        public List<CatalogueImportAlbum> Albums { get; } = [];
        public List<CatalogueImportTrack> Tracks { get; } = [];
        public Dictionary<string, List<string>> LibraryTracks { get; } = [];
        public List<int> ArtistBatchSizes { get; } = [];

        public Task WriteAlbumsAsync(string refreshId, IReadOnlyList<CatalogueImportAlbum> albums, CancellationToken cancellationToken)
        {
            Albums.AddRange(albums);
            return Task.CompletedTask;
        }

        public Task WriteGenresAsync(string refreshId, IReadOnlyList<CatalogueImportGenre> genres, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task WriteTracksAsync(string refreshId, IReadOnlyList<CatalogueImportTrack> tracks, CancellationToken cancellationToken)
        {
            Tracks.AddRange(tracks);
            return Task.CompletedTask;
        }

        public Task WriteArtistsAsync(string refreshId, IReadOnlyList<CatalogueImportArtist> artists, CancellationToken cancellationToken)
        {
            ArtistBatchSizes.Add(artists.Count);
            Artists.AddRange(artists);
            return Task.CompletedTask;
        }

        public Task WriteVirtualLibrariesAsync(string refreshId, IReadOnlyList<CatalogueImportVirtualLibrary> libraries, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task WriteVirtualLibraryTracksAsync(string refreshId, string librarySourceId, IReadOnlyList<string> trackSourceIds, CancellationToken cancellationToken)
        {
            if (!LibraryTracks.TryGetValue(librarySourceId, out var tracks))
            {
                tracks = [];
                LibraryTracks.Add(librarySourceId, tracks);
            }

            tracks.AddRange(trackSourceIds);
            return Task.CompletedTask;
        }

    }

    private sealed class RecordingCatalogueLogSink : ICatalogueRefreshLogSink
    {
        public Task WriteAsync(CatalogueRefreshLogLevel level, string message, int? processedCount, int? totalCount, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    private sealed class CatalogueHandler(
        Func<IReadOnlyList<string>, string> response) : HttpMessageHandler
    {
        public List<IReadOnlyList<string>> Commands { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var body = await request.Content!.ReadAsStringAsync(cancellationToken);
            using var document = JsonDocument.Parse(body);
            var command = document.RootElement
                .GetProperty("params")[1]
                .EnumerateArray()
                .Select(element => element.ToString())
                .ToArray();
            Commands.Add(command);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(response(command), Encoding.UTF8, "application/json")
            };
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
