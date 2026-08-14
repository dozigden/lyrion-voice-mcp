using System.Net;
using System.Text;
using System.Text.Json;
using LyrionVoiceMcp.Abstractions;
using LyrionVoiceMcp.Lms;

namespace LyrionVoiceMcp.Lms.Tests;

public sealed class LmsBrowseClientTests
{
    public static TheoryData<LmsBrowseQueryKind, string?, string[]> BrowseCommandCases =>
        new()
        {
            {
                LmsBrowseQueryKind.AlbumArtists,
                null,
                ["artists", "2", "17", "role_id:ALBUMARTIST"]
            },
            {
                LmsBrowseQueryKind.Artists,
                null,
                ["artists", "2", "17"]
            },
            {
                LmsBrowseQueryKind.Albums,
                null,
                ["albums", "2", "17", "tags:la"]
            },
            {
                LmsBrowseQueryKind.Genres,
                null,
                ["genres", "2", "17"]
            },
            {
                LmsBrowseQueryKind.Playlists,
                null,
                ["playlists", "2", "17"]
            },
            {
                LmsBrowseQueryKind.RecentlyAddedAlbums,
                null,
                ["albums", "2", "17", "sort:new", "tags:la"]
            },
            {
                LmsBrowseQueryKind.Years,
                null,
                ["years", "2", "17", "hasAlbums:1"]
            },
            {
                LmsBrowseQueryKind.AlbumArtistAlbums,
                "278",
                ["albums", "2", "17", "artist_id:278", "role_id:ALBUMARTIST", "tags:la"]
            },
            {
                LmsBrowseQueryKind.ArtistAlbums,
                "278",
                ["albums", "2", "17", "artist_id:278", "tags:la"]
            },
            {
                LmsBrowseQueryKind.GenreAlbums,
                "12",
                ["albums", "2", "17", "genre_id:12", "tags:la"]
            },
            {
                LmsBrowseQueryKind.YearAlbums,
                "2024",
                ["albums", "2", "17", "year:2024", "tags:la"]
            },
            {
                LmsBrowseQueryKind.AlbumTracks,
                "280",
                ["titles", "2", "17", "album_id:280", "sort:tracknum", "tags:ald"]
            },
            {
                LmsBrowseQueryKind.PlaylistTracks,
                "3315",
                ["playlists", "tracks", "2", "17", "playlist_id:3315", "tags:ald"]
            }
        };

    [Theory]
    [MemberData(nameof(BrowseCommandCases))]
    public async Task BrowseQueryShouldUseItsExpectedLmsCommand(
        LmsBrowseQueryKind kind,
        string? filterId,
        string[] expectedCommand)
    {
        // Arrange
        var handler = new RecordingHandler("""{"id":1,"result":{"count":0}}""");
        using var httpClient = new HttpClient(handler);
        var client = CreateClient(httpClient);

        // Act
        await client.BrowseAsync(
            new LmsBrowseRequest(kind, filterId, 2, 17),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(expectedCommand, handler.Command);
    }

    [Fact]
    public async Task AlbumArtistsShouldUseTheAlbumArtistRoleAndMapThePage()
    {
        // Arrange
        var handler = new RecordingHandler(
            """
            {"id":1,"result":{"artists_loop":[
              {"id":278,"artist":"Coppercap Circuit"}
            ],"count":10}}
            """);
        using var httpClient = new HttpClient(handler);
        var client = CreateClient(httpClient);

        // Act
        var page = await client.BrowseAsync(
            new LmsBrowseRequest(LmsBrowseQueryKind.AlbumArtists, null, 0, 50),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(10, page.TotalCount);
        var item = Assert.Single(page.Items);
        Assert.Equal(BrowseItemKind.AlbumArtist, item.Kind);
        Assert.Equal("278", item.Id);
        Assert.Equal("Coppercap Circuit", item.Title);
        Assert.Null(item.Artist);
        Assert.Equal(
            ["artists", "0", "50", "role_id:ALBUMARTIST"],
            handler.Command);
    }

    [Fact]
    public async Task AlbumTracksShouldRequestTrackOrderAndMapDisplayMetadata()
    {
        // Arrange
        var handler = new RecordingHandler(
            """
            {"id":1,"result":{"titles_loop":[
              {"id":3280,"title":"Postcard from Level Twelve","artist":"The Tunnel Wardens","album":"Away from Home"}
            ],"count":5}}
            """);
        using var httpClient = new HttpClient(handler);
        var client = CreateClient(httpClient);

        // Act
        var page = await client.BrowseAsync(
            new LmsBrowseRequest(LmsBrowseQueryKind.AlbumTracks, "280", 2, 50),
            TestContext.Current.CancellationToken);

        // Assert
        var item = Assert.Single(page.Items);
        Assert.Equal(BrowseItemKind.Track, item.Kind);
        Assert.Equal("3280", item.Id);
        Assert.Equal("The Tunnel Wardens", item.Artist);
        Assert.Equal("Away from Home", item.Album);
        Assert.Equal(
            ["titles", "2", "50", "album_id:280", "sort:tracknum", "tags:ald"],
            handler.Command);
    }

    [Fact]
    public async Task PlaylistTracksShouldUseTheDedicatedPlaylistTracksQuery()
    {
        // Arrange
        var handler = new RecordingHandler(
            """
            {"id":1,"result":{"playlisttracks_loop":[
              {"id":3229,"title":"Seedlings After Supper","artist":"The Rusty Lanterns","album":"By Lanternlight"}
            ],"count":4}}
            """);
        using var httpClient = new HttpClient(handler);
        var client = CreateClient(httpClient);

        // Act
        var page = await client.BrowseAsync(
            new LmsBrowseRequest(LmsBrowseQueryKind.PlaylistTracks, "3315", 0, 50),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Single(page.Items);
        Assert.Equal(
            ["playlists", "tracks", "0", "50", "playlist_id:3315", "tags:ald"],
            handler.Command);
    }

    [Fact]
    public async Task EmptyPageShouldAllowLmsToOmitTheLoop()
    {
        // Arrange
        var handler = new RecordingHandler("""{"id":1,"result":{"count":0}}""");
        using var httpClient = new HttpClient(handler);
        var client = CreateClient(httpClient);

        // Act
        var page = await client.BrowseAsync(
            new LmsBrowseRequest(LmsBrowseQueryKind.Genres, null, 0, 50),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(page.Items);
        Assert.Equal(0, page.TotalCount);
    }

    [Fact]
    public async Task InvalidLoopShouldFailClearly()
    {
        // Arrange
        var handler = new RecordingHandler(
            """{"id":1,"result":{"albums_loop":{},"count":1}}""");
        using var httpClient = new HttpClient(handler);
        var client = CreateClient(httpClient);

        // Act
        var exception = await Assert.ThrowsAsync<LmsRequestException>(() =>
            client.BrowseAsync(
                new LmsBrowseRequest(LmsBrowseQueryKind.Albums, null, 0, 50),
                TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(
            "LMS browse response did not include a valid albums_loop array.",
            exception.Message);
    }

    private static LmsBrowseClient CreateClient(HttpClient httpClient)
    {
        var settings = LmsConnectionSettings.FromValues(
            "development",
            "http://music.test:9000",
            null);
        return new LmsBrowseClient(new LmsJsonRpcClient(settings, httpClient));
    }

    private sealed class RecordingHandler(string responseJson) : HttpMessageHandler
    {
        public IReadOnlyList<string>? Command { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var body = await request.Content!.ReadAsStringAsync(cancellationToken);
            using var document = JsonDocument.Parse(body);
            Command = document.RootElement
                .GetProperty("params")[1]
                .EnumerateArray()
                .Select(element => element.ToString())
                .ToArray();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson, Encoding.UTF8, "application/json")
            };
        }
    }
}
