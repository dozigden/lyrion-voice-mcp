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
    public async Task ReadShouldMapTheSupportedCatalogueAndRelationships()
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
                """
                {"id":1,"result":{"count":1,"titles_loop":[{"id":31,"title":"Lantern Almanac"}]}}
                """,
            "titles" =>
                """
                {"id":1,"result":{"count":1,"titles_loop":[{
                  "id":31,
                  "title":"Lantern Almanac",
                  "subtitle":"Harbour version",
                  "url":"file:///music/Glass%20Harbours/Lantern%20Almanac.flac",
                  "type":"flc",
                  "remote":"0",
                  "album_id":21,
                  "year":2025,
                  "disc":1,
                  "disccount":2,
                  "tracknum":3,
                  "duration":"241.5",
                  "filesize":"42000000",
                  "samplerate":"96000",
                  "addedTime":"1786000000",
                  "modificationTime":"1785000000",
                  "lastUpdated":"1786100000",
                  "release_type":"ALBUM",
                  "compilation":"0",
                  "artwork_track_id":31,
                  "work_id":61,
                  "work":"Northern Bearings",
                  "performance":"Live at Low Water",
                  "grouping":"Tidal pieces",
                  "artist_ids":"11",
                  "composer_ids":"13",
                  "genre_ids":"41,42",
                  "rating":"80",
                  "playcount":"7"
                }]}}
                """,
            "libraries" =>
                """
                {"id":1,"result":{"folder_loop":[{"id":51,"name":"Evening Navigation"}]}}
                """,
            _ => throw new InvalidOperationException($"Unexpected command {string.Join(' ', command)}")
        });
        using var httpClient = new HttpClient(handler);
        var reader = CreateReader(httpClient);

        // Act
        var snapshot = await reader.ReadAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("development", snapshot.Source.Id);
        Assert.Equal("lms", snapshot.Source.Provider);
        Assert.Equal("9.1.2", snapshot.Source.Version);
        Assert.Equal("1786379003", snapshot.Source.Revision);
        Assert.Equal(CapturedAt, snapshot.CapturedAt);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1786379003), snapshot.SourceLastScanAt);
        Assert.Empty(snapshot.Warnings);

        Assert.Equal(2, snapshot.Artists.Count);
        var artist = snapshot.Artists[0];
        Assert.Equal("11", artist.SourceId);
        Assert.Equal("The Glass Harbours", artist.Name);
        Assert.Equal("artist:glass-harbours", artist.ExternalId);
        Assert.Contains(snapshot.Artists, item => item.SourceId == "12");
        Assert.DoesNotContain(snapshot.Artists, item => item.SourceId == "13");

        var album = Assert.Single(snapshot.Albums);
        Assert.Equal("21", album.SourceId);
        Assert.Equal("12", album.AlbumArtistSourceId);
        Assert.False(album.IsCompilation);

        var track = Assert.Single(snapshot.Tracks);
        Assert.Equal("31", track.SourceId);
        Assert.Equal("21", track.AlbumSourceId);
        Assert.Equal(241.5, track.DurationSeconds);
        Assert.Equal(42_000_000, track.FileSizeBytes);
        Assert.Equal(96_000, track.SampleRate);
        Assert.Equal(["41", "42"], track.GenreSourceIds);
        Assert.Equal(["11"], track.ArtistSourceIds);
        var statistics = Assert.Single(track.Statistics);
        Assert.Equal("lms-core", statistics.Source);
        Assert.Equal(80, statistics.Rating);
        Assert.Equal(7, statistics.PlayCount);
        Assert.Null(statistics.LastPlayedAt);

        var library = Assert.Single(snapshot.VirtualLibraries);
        Assert.Equal("51", library.SourceId);
        Assert.Equal(["31"], library.TrackSourceIds);
        Assert.Contains(
            handler.Commands,
            command => command.SequenceEqual(
                ["titles", "0", "500", "library_id:51", "tags:II"]));
        Assert.Contains(
            handler.Commands,
            command => command.SequenceEqual(
                ["titles", "0", "500", "tags:uxoeyiqtdfTnDUESPROWCb1hzJ"]));
        Assert.Single(
            handler.Commands,
            command => command.SequenceEqual(["libraries"]));
    }

    [Fact]
    public async Task ReadShouldPageCountedCollections()
    {
        // Arrange
        var handler = new CatalogueHandler(command => command[0] switch
        {
            "serverstatus" => StatusResponse(501, 0, 0, 0),
            "artists" => ArtistsPage(int.Parse(command[1], System.Globalization.CultureInfo.InvariantCulture)),
            "albums" => EmptyCountedResponse(),
            "genres" => EmptyCountedResponse(),
            "titles" => EmptyCountedResponse(),
            "libraries" => """{"id":1,"result":{"folder_loop":[]}}""",
            _ => throw new InvalidOperationException($"Unexpected command {string.Join(' ', command)}")
        });
        using var httpClient = new HttpClient(handler);
        var reader = CreateReader(httpClient);

        // Act
        var snapshot = await reader.ReadAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(snapshot.Artists);
        Assert.Contains(
            handler.Commands,
            command => command.SequenceEqual(["artists", "500", "500", "tags:E"]));
    }

    [Fact]
    public async Task ReadShouldRecordSanitisedRelationshipWarnings()
    {
        // Arrange
        var handler = new CatalogueHandler(command => command[0] switch
        {
            "serverstatus" => StatusResponse(0, 0, 0, 1),
            "artists" or "albums" or "genres" => EmptyCountedResponse(),
            "titles" when command.Any(value => value == "library_id:51") =>
                """{"id":1,"result":{"count":1,"titles_loop":[{"id":999}]}}""",
            "titles" =>
                """
                {"id":1,"result":{"count":1,"titles_loop":[{
                  "id":31,"title":"Clockwork Estuary","url":"file:///music/Clockwork%20Estuary.flac","remote":"0",
                  "album_id":21,"artist_ids":"11","genre_ids":"41"
                }]}}
                """,
            "libraries" =>
                """{"id":1,"result":{"folder_loop":[{"id":51,"name":"Unmapped Shores"}]}}""",
            _ => throw new InvalidOperationException($"Unexpected command {string.Join(' ', command)}")
        });
        using var httpClient = new HttpClient(handler);
        var reader = CreateReader(httpClient);

        // Act
        var snapshot = await reader.ReadAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Collection(
            snapshot.Warnings,
            warning => Assert.Equal(("missing-album", 1), (warning.Code, warning.Occurrences)),
            warning => Assert.Equal(("missing-artist", 1), (warning.Code, warning.Occurrences)),
            warning => Assert.Equal(("missing-genre", 1), (warning.Code, warning.Occurrences)),
            warning => Assert.Equal(("missing-library-track", 1), (warning.Code, warning.Occurrences)));
        Assert.DoesNotContain(snapshot.Warnings, warning => warning.Message.Contains("Clockwork", StringComparison.Ordinal));
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
        var reader = CreateReader(httpClient);

        // Act
        var exception = await Assert.ThrowsAsync<LmsRequestException>(
            () => reader.ReadAsync(TestContext.Current.CancellationToken));

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
        var reader = CreateReader(httpClient);

        // Act
        var exception = await Assert.ThrowsAsync<LmsRequestException>(
            () => reader.ReadAsync(TestContext.Current.CancellationToken));

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

    private static string EmptyCountedResponse() =>
        """{"id":1,"result":{"count":0}}""";

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
                artists_loop = items.Select(id => new
                {
                    id,
                    artist = $"Fictional Artist {id}"
                })
            }
        });
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
