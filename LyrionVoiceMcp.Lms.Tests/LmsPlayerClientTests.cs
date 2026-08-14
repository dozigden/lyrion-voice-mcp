using System.Net;
using System.Text;
using System.Text.Json;
using LyrionVoiceMcp.Abstractions;
using LyrionVoiceMcp.Lms;

namespace LyrionVoiceMcp.Lms.Tests;

public sealed class LmsPlayerClientTests
{
    [Fact]
    public async Task GetPlayersShouldMapFullStatusInDiscoveryOrder()
    {
        // Arrange
        var handler = new StubHttpMessageHandler(request => request.Command switch
        {
            "players" => JsonResponse(
                """
                {"id":1,"result":{"players_loop":[
                  {"playerid":"00:11:22:33:44:55","name":"North Room","power":1},
                  {"playerid":"66:77:88:99:aa:bb","name":"South Room","power":0},
                  {"playerid":"cc:dd:ee:ff:00:11","name":"Workshop","power":1},
                  {"playerid":"22:33:44:55:66:77","name":"Garden Room","power":0}
                ]}}
                """),
            "status" when request.PlayerId == "00:11:22:33:44:55" => JsonResponse(
                """
                {"id":1,"result":{"power":1,"mode":"play","mixer volume":"37","time":"12.5","duration":244.25,"playlist_loop":[{"title":"Lantern Signals","trackartist":"The Paper Comets","album":"Night Routes"}]}}
                """),
            "status" when request.PlayerId == "66:77:88:99:aa:bb" => JsonResponse(
                """
                {"id":1,"result":{"power":"0","mode":"pause","mixer volume":0,"playlist_loop":[]}}
                """),
            "status" when request.PlayerId == "cc:dd:ee:ff:00:11" => JsonResponse(
                """
                {"id":1,"result":{"power":true,"mode":"future-mode"}}
                """),
            "status" when request.PlayerId == "22:33:44:55:66:77" => JsonResponse(
                """
                {"id":1,"result":{"power":false,"mode":"stop","mixer volume":51,"time":0,"duration":"206.466","playlist_loop":[{"title":"Silver Map","albumartist":"Northbound Signals","album":"Paper Roads"}]}}
                """),
            "mixer" when request.PlayerId == "00:11:22:33:44:55" => MutingResponse(1),
            "mixer" when request.PlayerId == "66:77:88:99:aa:bb" => MutingResponse("0"),
            "mixer" when request.PlayerId == "cc:dd:ee:ff:00:11" => MutingResponse(null),
            "mixer" when request.PlayerId == "22:33:44:55:66:77" => MutingResponse(false),
            _ => throw new InvalidOperationException($"Unexpected command {request.Command}.")
        });
        using var client = new HttpClient(handler);
        var playerClient = CreateClient(client);

        // Act
        var players = await playerClient.GetPlayersAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Collection(
            players,
            player => AssertPlayer(
                player,
                "00:11:22:33:44:55",
                "North Room",
                true,
                PlayerPlaybackState.Playing,
                37,
                true,
                new LmsNowPlaying(
                    "Lantern Signals",
                    "The Paper Comets",
                    "Night Routes",
                    244.25,
                    12.5)),
            player => AssertPlayer(
                player,
                "66:77:88:99:aa:bb",
                "South Room",
                false,
                PlayerPlaybackState.Paused,
                0,
                false,
                null),
            player => AssertPlayer(
                player,
                "cc:dd:ee:ff:00:11",
                "Workshop",
                true,
                PlayerPlaybackState.Unknown,
                null,
                null,
                null),
            player => AssertPlayer(
                player,
                "22:33:44:55:66:77",
                "Garden Room",
                false,
                PlayerPlaybackState.Stopped,
                51,
                false,
                new LmsNowPlaying(
                    "Silver Map",
                    "Northbound Signals",
                    "Paper Roads",
                    206.466,
                    0)));
        Assert.Equal(4, handler.Requests.Count(request => request.Command == "status"));
        Assert.Equal(4, handler.Requests.Count(request => request.Command == "mixer"));
        Assert.All(
            handler.Requests.Where(request => request.Command == "status"),
            request => Assert.Equal(
                "[\"status\",\"-\",1,\"tags:cgAABbehldiqtyrSuoKLNJ\"]",
                request.Parameters));
    }

    [Fact]
    public async Task GetPlayersShouldReturnEmptyWithoutStatusQueries()
    {
        // Arrange
        var handler = new StubHttpMessageHandler(_ =>
            JsonResponse("""{"id":1,"result":{"count":0}}"""));
        using var client = new HttpClient(handler);
        var playerClient = CreateClient(client);

        // Act
        var players = await playerClient.GetPlayersAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(players);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task GetPlayersShouldPreferTheCurrentRemoteStreamTitle()
    {
        // Arrange
        var handler = new StubHttpMessageHandler(request => request.Command switch
        {
            "players" => JsonResponse(
                """
                {"id":1,"result":{"players_loop":[{"playerid":"00:11:22:33:44:55","name":"North Room"}]}}
                """),
            "status" => JsonResponse(
                """
                {"id":1,"result":{"power":1,"mode":"play","time":18,"current_title":"Weather report","playlist_loop":[{"title":"Fictional Radio"}]}}
                """),
            "mixer" => MutingResponse(null),
            _ => throw new InvalidOperationException($"Unexpected command {request.Command}.")
        });
        using var client = new HttpClient(handler);
        var playerClient = CreateClient(client);

        // Act
        var players = await playerClient.GetPlayersAsync(TestContext.Current.CancellationToken);

        // Assert
        var player = Assert.Single(players);
        Assert.Equal("Weather report", player.NowPlaying?.Title);
        Assert.Equal(18, player.NowPlaying?.ElapsedSeconds);
    }

    [Fact]
    public async Task GetPlayersShouldRejectInvalidPowerValue()
    {
        // Arrange
        var handler = new StubHttpMessageHandler(request => request.Command switch
        {
            "players" => JsonResponse(
                """
                {"id":1,"result":{"players_loop":[{"playerid":"00:11:22:33:44:55","name":"North Room","power":1}]}}
                """),
            "status" => JsonResponse(
                """
                {"id":1,"result":{"power":"perhaps","mode":"stop"}}
                """),
            "mixer" => MutingResponse(null),
            _ => throw new InvalidOperationException($"Unexpected command {request.Command}.")
        });
        using var client = new HttpClient(handler);
        var playerClient = CreateClient(client);

        // Act
        var exception = await Assert.ThrowsAsync<LmsRequestException>(() =>
            playerClient.GetPlayersAsync(TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(
            "LMS player status response contained an invalid power value.",
            exception.Message);
    }

    [Fact]
    public async Task GetPlayersShouldReportUpstreamHttpFailure()
    {
        // Arrange
        var handler = new StubHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        using var client = new HttpClient(handler);
        var playerClient = CreateClient(client);

        // Act
        var exception = await Assert.ThrowsAsync<LmsRequestException>(() =>
            playerClient.GetPlayersAsync(TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal("LMS returned HTTP 503.", exception.Message);
    }

    [Fact]
    public async Task GetPlayersShouldPropagateCallerCancellation()
    {
        // Arrange
        var handler = new StubHttpMessageHandler(_ =>
            throw new InvalidOperationException("A cancelled request should not be handled."));
        using var client = new HttpClient(handler);
        var playerClient = CreateClient(client);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        // Act
        var exception = await Record.ExceptionAsync(() =>
            playerClient.GetPlayersAsync(cancellation.Token));

        // Assert
        Assert.IsAssignableFrom<OperationCanceledException>(exception);
    }

    private static LmsPlayerClient CreateClient(HttpClient httpClient)
    {
        var settings = LmsConnectionSettings.FromValues(
            "development",
            "http://music.test:9000",
            null);
        return new LmsPlayerClient(new LmsJsonRpcClient(settings, httpClient));
    }

    private static HttpResponseMessage JsonResponse(string json) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

    private static HttpResponseMessage MutingResponse(object? value) =>
        JsonResponse(
            $"{{\"id\":1,\"result\":{{\"_muting\":{JsonSerializer.Serialize(value)}}}}}");

    private static void AssertPlayer(
        LmsPlayerStatus player,
        string id,
        string name,
        bool poweredOn,
        PlayerPlaybackState playbackState,
        int? volume,
        bool? muted,
        LmsNowPlaying? nowPlaying)
    {
        Assert.Equal(id, player.Id);
        Assert.Equal(name, player.Name);
        Assert.Equal(poweredOn, player.PoweredOn);
        Assert.Equal(playbackState, player.PlaybackState);
        Assert.Equal(volume, player.Volume);
        Assert.Equal(muted, player.Muted);
        Assert.Equal(nowPlaying, player.NowPlaying);
    }

    private sealed class StubHttpMessageHandler(
        Func<RecordedRequest, HttpResponseMessage> respond) : HttpMessageHandler
    {
        private readonly List<RecordedRequest> requests = [];

        public IReadOnlyList<RecordedRequest> Requests
        {
            get
            {
                lock (requests)
                {
                    return requests.ToArray();
                }
            }
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var body = await request.Content!.ReadAsStringAsync(cancellationToken);
            using var document = JsonDocument.Parse(body);
            var parameters = document.RootElement.GetProperty("params");
            var recorded = new RecordedRequest(
                parameters[0].GetString()!,
                parameters[1][0].GetString()!,
                parameters[1].GetRawText());
            lock (requests)
            {
                requests.Add(recorded);
            }

            return respond(recorded);
        }
    }

    private sealed record RecordedRequest(
        string PlayerId,
        string Command,
        string Parameters);
}
