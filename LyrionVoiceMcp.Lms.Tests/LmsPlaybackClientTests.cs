using System.Net;
using System.Text;
using System.Text.Json;
using LyrionVoiceMcp.Abstractions;
using LyrionVoiceMcp.Lms;

namespace LyrionVoiceMcp.Lms.Tests;

public sealed class LmsPlaybackClientTests
{
    [Fact]
    public async Task PlayableItemChecksShouldUseOneItemFilteredQueries()
    {
        // Arrange
        var handler = new StubHttpMessageHandler(_ =>
            JsonResponse("""{"id":1,"result":{"count":3}}"""));
        using var httpClient = new HttpClient(handler);
        var client = CreateClient(httpClient);

        // Act
        var results = await Task.WhenAll(
            client.GetPlayableItemCountAsync(
                new MediaIdentity(MediaEntityKind.Artist, "11"),
                TestContext.Current.CancellationToken),
            client.GetPlayableItemCountAsync(
                new MediaIdentity(MediaEntityKind.Album, "22"),
                TestContext.Current.CancellationToken),
            client.GetPlayableItemCountAsync(
                new MediaIdentity(MediaEntityKind.Track, "33"),
                TestContext.Current.CancellationToken),
            client.GetPlayableItemCountAsync(
                new MediaIdentity(MediaEntityKind.Playlist, "44"),
                TestContext.Current.CancellationToken));

        // Assert
        Assert.All(results, count => Assert.Equal(3, count));
        Assert.Contains(handler.Requests, request =>
            request.CommandJson == "[\"titles\",0,1,\"artist_id:11\",\"tags:i\"]");
        Assert.Contains(handler.Requests, request =>
            request.CommandJson == "[\"titles\",0,1,\"album_id:22\",\"tags:i\"]");
        Assert.Contains(handler.Requests, request =>
            request.CommandJson == "[\"titles\",0,1,\"track_id:33\",\"tags:i\"]");
        Assert.Contains(handler.Requests, request =>
            request.CommandJson == "[\"playlists\",\"tracks\",0,1,\"playlist_id:44\",\"tags:i\"]");
    }

    [Fact]
    public async Task PlayableItemCheckShouldReturnFalseForAnEmptyFilteredResult()
    {
        // Arrange
        var handler = new StubHttpMessageHandler(_ =>
            JsonResponse("""{"id":1,"result":{"count":"0"}}"""));
        using var httpClient = new HttpClient(handler);
        var client = CreateClient(httpClient);

        // Act
        var result = await client.GetPlayableItemCountAsync(
            new MediaIdentity(MediaEntityKind.Album, "22"),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(0, result);
    }

    [Fact]
    public async Task LoadAndAddShouldPassCollectionIdsDirectlyToPlaylistControl()
    {
        // Arrange
        var handler = new StubHttpMessageHandler(_ =>
            JsonResponse("""{"id":1,"result":{"count":3}}"""));
        using var httpClient = new HttpClient(handler);
        var client = CreateClient(httpClient);

        // Act
        await client.LoadAsync(
            "00:11:22:33:44:55",
            new MediaIdentity(MediaEntityKind.Album, "22"),
            TestContext.Current.CancellationToken);
        await client.AddAsync(
            "00:11:22:33:44:55",
            new MediaIdentity(MediaEntityKind.Playlist, "44"),
            TestContext.Current.CancellationToken);
        await client.AddAsync(
            "00:11:22:33:44:55",
            new MediaIdentity(MediaEntityKind.Artist, "11"),
            TestContext.Current.CancellationToken);
        await client.AddAsync(
            "00:11:22:33:44:55",
            new MediaIdentity(MediaEntityKind.Track, "33"),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Collection(
            handler.Requests,
            request => AssertRequest(
                request,
                "00:11:22:33:44:55",
                "[\"playlistcontrol\",\"cmd:load\",\"album_id:22\"]"),
            request => AssertRequest(
                request,
                "00:11:22:33:44:55",
                "[\"playlistcontrol\",\"cmd:add\",\"playlist_id:44\"]"),
            request => AssertRequest(
                request,
                "00:11:22:33:44:55",
                "[\"playlistcontrol\",\"cmd:add\",\"artist_id:11\"]"),
            request => AssertRequest(
                request,
                "00:11:22:33:44:55",
                "[\"playlistcontrol\",\"cmd:add\",\"track_id:33\"]"));
    }

    [Fact]
    public async Task InsertAndClearShouldUseNativeQueueCommands()
    {
        // Arrange
        var handler = new StubHttpMessageHandler(request =>
            request.CommandName == "playlistcontrol"
                ? JsonResponse("""{"id":1,"result":{"count":2}}""")
                : JsonResponse("""{"id":1,"result":{}}"""));
        using var httpClient = new HttpClient(handler);
        var client = CreateClient(httpClient);

        // Act
        await client.InsertAsync(
            "00:11:22:33:44:55",
            new MediaIdentity(MediaEntityKind.Album, "22"),
            TestContext.Current.CancellationToken);
        await client.ClearAsync(
            "00:11:22:33:44:55",
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Collection(
            handler.Requests,
            request => AssertRequest(
                request,
                "00:11:22:33:44:55",
                "[\"playlistcontrol\",\"cmd:insert\",\"album_id:22\"]"),
            request => AssertRequest(
                request,
                "00:11:22:33:44:55",
                "[\"playlist\",\"clear\"]"));
    }

    [Fact]
    public async Task PlayerMutationPrimitivesShouldValidatePowerAndQueueState()
    {
        // Arrange
        var handler = new StubHttpMessageHandler(request => request.CommandName switch
        {
            "power" when request.CommandJson == "[\"power\",1,1]" =>
                JsonResponse("""{"id":1,"result":{}}"""),
            "power" => JsonResponse("""{"id":1,"result":{"_power":"1"}}"""),
            "playlist" when request.CommandJson.Contains("\"tracks\"", StringComparison.Ordinal) =>
                JsonResponse("""{"id":1,"result":{"_tracks":"7"}}"""),
            "playlist" => JsonResponse("""{"id":1,"result":{}}"""),
            _ => throw new InvalidOperationException($"Unexpected command {request.CommandJson}.")
        });
        using var httpClient = new HttpClient(handler);
        var client = CreateClient(httpClient);

        // Act
        var queueCount = await client.GetQueueCountAsync(
            "00:11:22:33:44:55",
            TestContext.Current.CancellationToken);
        await client.PowerOnAsync(
            "00:11:22:33:44:55",
            TestContext.Current.CancellationToken);
        await client.StartAtAsync(
            "00:11:22:33:44:55",
            queueCount,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(7, queueCount);
        Assert.Equal(
            [
                "[\"playlist\",\"tracks\",\"?\"]",
                "[\"power\",1,1]",
                "[\"power\",\"?\"]",
                "[\"playlist\",\"index\",7]"
            ],
            handler.Requests.Select(request => request.CommandJson));
    }

    [Fact]
    public async Task PowerOnShouldRejectAFalsePowerResponse()
    {
        // Arrange
        var handler = new StubHttpMessageHandler(request =>
            request.CommandJson == "[\"power\",1,1]"
                ? JsonResponse("""{"id":1,"result":{}}""")
                : JsonResponse("""{"id":1,"result":{"_power":0}}"""));
        using var httpClient = new HttpClient(handler);
        var client = CreateClient(httpClient);

        // Act
        var exception = await Assert.ThrowsAsync<LmsRequestException>(() =>
            client.PowerOnAsync(
                "00:11:22:33:44:55",
                TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal("LMS did not power on the selected player.", exception.Message);
    }

    private static LmsPlaybackClient CreateClient(HttpClient httpClient)
    {
        var settings = LmsConnectionSettings.FromValues(
            "development",
            "http://music.test:9000",
            null);
        return new LmsPlaybackClient(new LmsJsonRpcClient(settings, httpClient));
    }

    private static HttpResponseMessage JsonResponse(string json) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

    private static void AssertRequest(
        RecordedRequest request,
        string playerId,
        string commandJson)
    {
        Assert.Equal(playerId, request.PlayerId);
        Assert.Equal(commandJson, request.CommandJson);
    }

    private sealed class StubHttpMessageHandler(
        Func<RecordedRequest, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<RecordedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var body = await request.Content!.ReadAsStringAsync(cancellationToken);
            using var document = JsonDocument.Parse(body);
            var parameters = document.RootElement.GetProperty("params");
            var command = parameters[1];
            var recorded = new RecordedRequest(
                parameters[0].GetString()!,
                command[0].GetString()!,
                command.GetRawText());
            Requests.Add(recorded);
            return respond(recorded);
        }
    }

    private sealed record RecordedRequest(
        string PlayerId,
        string CommandName,
        string CommandJson);
}
