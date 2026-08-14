using System.Net;
using System.Text;
using System.Text.Json;
using LyrionVoiceMcp.Abstractions;
using LyrionVoiceMcp.Lms;

namespace LyrionVoiceMcp.Lms.Tests;

public sealed class LmsPlayerControlClientTests
{
    private const string PlayerId = "00:11:22:33:44:55";

    [Fact]
    public async Task PlaybackActionsShouldUseExplicitLmsCommands()
    {
        // Arrange
        var handler = new StubHttpMessageHandler(_ =>
            JsonResponse("""{"id":1,"result":{}}"""));
        using var httpClient = new HttpClient(handler);
        var client = CreateClient(httpClient);

        // Act
        await client.ControlAsync(
            PlayerId,
            PlayerControlCommand.Resume,
            TestContext.Current.CancellationToken);
        await client.ControlAsync(
            PlayerId,
            PlayerControlCommand.Pause,
            TestContext.Current.CancellationToken);
        await client.ControlAsync(
            PlayerId,
            PlayerControlCommand.Stop,
            TestContext.Current.CancellationToken);
        await client.ControlAsync(
            PlayerId,
            PlayerControlCommand.Next,
            TestContext.Current.CancellationToken);
        await client.ControlAsync(
            PlayerId,
            PlayerControlCommand.Previous,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.All(handler.Requests, request => Assert.Equal(PlayerId, request.PlayerId));
        Assert.Equal(
            [
                "[\"play\"]",
                "[\"pause\",1]",
                "[\"stop\"]",
                "[\"playlist\",\"index\",\"\\u002B1\"]",
                "[\"playlist\",\"index\",\"-1\"]"
            ],
            handler.Requests.Select(request => request.CommandJson));
    }

    [Fact]
    public async Task PowerActionsShouldSetAndConfirmTheRequestedState()
    {
        // Arrange
        var powerState = false;
        var handler = new StubHttpMessageHandler(request =>
        {
            if (request.CommandJson == "[\"power\",1,1]")
            {
                powerState = true;
            }
            else if (request.CommandJson == "[\"power\",0]")
            {
                powerState = false;
            }

            return request.CommandJson == "[\"power\",\"?\"]"
                ? JsonResponse(
                    $"{{\"id\":1,\"result\":{{\"_power\":{(powerState ? 1 : 0)}}}}}")
                : JsonResponse("""{"id":1,"result":{}}""");
        });
        using var httpClient = new HttpClient(handler);
        var client = CreateClient(httpClient);

        // Act
        await client.ControlAsync(
            PlayerId,
            PlayerControlCommand.PowerOn,
            TestContext.Current.CancellationToken);
        await client.ControlAsync(
            PlayerId,
            PlayerControlCommand.PowerOff,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            [
                "[\"power\",1,1]",
                "[\"power\",\"?\"]",
                "[\"power\",0]",
                "[\"power\",\"?\"]"
            ],
            handler.Requests.Select(request => request.CommandJson));
    }

    [Theory]
    [InlineData(PlayerControlCommand.PowerOn, 0, "LMS did not power on the selected player.")]
    [InlineData(PlayerControlCommand.PowerOff, 1, "LMS did not power off the selected player.")]
    public async Task PowerActionShouldRejectAnUnconfirmedState(
        PlayerControlCommand command,
        int reportedState,
        string expectedMessage)
    {
        // Arrange
        var handler = new StubHttpMessageHandler(request =>
            request.CommandJson == "[\"power\",\"?\"]"
                ? JsonResponse(
                    $"{{\"id\":1,\"result\":{{\"_power\":{reportedState}}}}}")
                : JsonResponse("""{"id":1,"result":{}}"""));
        using var httpClient = new HttpClient(handler);
        var client = CreateClient(httpClient);

        // Act
        var exception = await Assert.ThrowsAsync<LmsRequestException>(() =>
            client.ControlAsync(
                PlayerId,
                command,
                TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(expectedMessage, exception.Message);
    }

    private static LmsPlayerControlClient CreateClient(HttpClient httpClient)
    {
        var settings = LmsConnectionSettings.FromValues(
            "development",
            "http://music.test:9000",
            null);
        return new LmsPlayerControlClient(
            new LmsJsonRpcClient(settings, httpClient));
    }

    private static HttpResponseMessage JsonResponse(string json) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

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
            var recorded = new RecordedRequest(
                parameters[0].GetString()!,
                parameters[1].GetRawText());
            Requests.Add(recorded);
            return respond(recorded);
        }
    }

    private sealed record RecordedRequest(
        string PlayerId,
        string CommandJson);
}
