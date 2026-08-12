using System.Net;
using System.Text;
using System.Text.Json;
using LyrionVoiceMcp.Abstractions;
using LyrionVoiceMcp.Lms;

namespace LyrionVoiceMcp.Lms.Tests;

public sealed class LmsPlayerClientTests
{
    [Fact]
    public async Task GetPlayersShouldMapPowerAndPlaybackModesInDiscoveryOrder()
    {
        // Arrange
        var handler = new StubHttpMessageHandler(request => request.Command switch
        {
            "players" => JsonResponse(
                """
                {"id":1,"result":{"players_loop":[
                  {"playerid":"00:11:22:33:44:55","name":"North Room","power":1},
                  {"playerid":"66:77:88:99:aa:bb","name":"South Room","power":"0"},
                  {"playerid":"cc:dd:ee:ff:00:11","name":"Workshop","power":true},
                  {"playerid":"22:33:44:55:66:77","name":"Garden Room","power":false}
                ]}}
                """),
            "mode" when request.PlayerId == "00:11:22:33:44:55" => ModeResponse("play"),
            "mode" when request.PlayerId == "66:77:88:99:aa:bb" => ModeResponse("pause"),
            "mode" when request.PlayerId == "cc:dd:ee:ff:00:11" => ModeResponse("future-mode"),
            "mode" when request.PlayerId == "22:33:44:55:66:77" => ModeResponse("stop"),
            _ => throw new InvalidOperationException($"Unexpected command {request.Command}.")
        });
        using var client = new HttpClient(handler);
        var playerClient = CreateClient(client);

        // Act
        var players = await playerClient.GetPlayersAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Collection(
            players,
            player => AssertPlayer(player, "00:11:22:33:44:55", "North Room", true, PlayerPlaybackState.Playing),
            player => AssertPlayer(player, "66:77:88:99:aa:bb", "South Room", false, PlayerPlaybackState.Paused),
            player => AssertPlayer(player, "cc:dd:ee:ff:00:11", "Workshop", true, PlayerPlaybackState.Unknown),
            player => AssertPlayer(player, "22:33:44:55:66:77", "Garden Room", false, PlayerPlaybackState.Stopped));
    }

    [Fact]
    public async Task GetPlayersShouldReturnEmptyWithoutModeQueries()
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
    public async Task GetPlayersShouldRejectInvalidPowerValue()
    {
        // Arrange
        var handler = new StubHttpMessageHandler(_ => JsonResponse(
            """
            {"id":1,"result":{"players_loop":[{"playerid":"00:11:22:33:44:55","name":"North Room","power":"perhaps"}]}}
            """));
        using var client = new HttpClient(handler);
        var playerClient = CreateClient(client);

        // Act
        var exception = await Assert.ThrowsAsync<LmsRequestException>(() =>
            playerClient.GetPlayersAsync(TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(
            "LMS players response contained an invalid power value.",
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

    private static HttpResponseMessage ModeResponse(string mode) =>
        JsonResponse(
            $"{{\"id\":1,\"result\":{{\"_mode\":{JsonSerializer.Serialize(mode)}}}}}");

    private static void AssertPlayer(
        LmsPlayerStatus player,
        string id,
        string name,
        bool poweredOn,
        PlayerPlaybackState playbackState)
    {
        Assert.Equal(id, player.Id);
        Assert.Equal(name, player.Name);
        Assert.Equal(poweredOn, player.PoweredOn);
        Assert.Equal(playbackState, player.PlaybackState);
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
                parameters[1][0].GetString()!);
            lock (requests)
            {
                requests.Add(recorded);
            }

            return respond(recorded);
        }
    }

    private sealed record RecordedRequest(string PlayerId, string Command);
}
