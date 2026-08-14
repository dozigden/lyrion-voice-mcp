using System.Net;
using System.Text;
using System.Text.Json;
using LyrionVoiceMcp.Abstractions;
using LyrionVoiceMcp.Lms;

namespace LyrionVoiceMcp.Lms.Tests;

public sealed class LmsQueueClientTests
{
    private const string PlayerId = "00:11:22:33:44:55";

    [Fact]
    public async Task GetQueueShouldMapEveryItemAndPreferTheCurrentStreamTitle()
    {
        // Arrange
        var handler = new StubHttpMessageHandler(JsonResponse(
            """
            {"id":1,"result":{"playlist_tracks":"3","playlist_cur_index":1,"current_title":"The midnight bulletin","playlist_loop":[{"playlist index":0,"title":"Lantern Signals","trackartist":"The Paper Comets","artist":"Paper Comets","album":"Night Routes","duration":"244.25"},{"playlist index":"1","title":"Evening Service","artist":"North Coast Radio"},{"playlist index":2,"title":"Glass Harbour","albumartist":"The Quiet Assembly","album":"Tidal Rooms","duration":198}]}}
            """));
        using var httpClient = new HttpClient(handler);
        var client = CreateClient(httpClient);

        // Act
        var queue = await client.GetQueueAsync(
            PlayerId,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(PlayerId, queue.PlayerId);
        Assert.Equal(1, queue.CurrentIndex);
        Assert.Collection(
            queue.Items,
            item => Assert.Equal(
                new(0, "Lantern Signals", "The Paper Comets", "Night Routes", 244.25),
                item),
            item => Assert.Equal(
                new(1, "The midnight bulletin", "North Coast Radio", null, null),
                item),
            item => Assert.Equal(
                new(2, "Glass Harbour", "The Quiet Assembly", "Tidal Rooms", 198),
                item));
        Assert.Equal(PlayerId, handler.PlayerId);
        Assert.Equal(
            "[\"status\",0,300,\"tags:aAld\"]",
            handler.CommandJson);
    }

    [Fact]
    public async Task GetQueueShouldReturnAnEmptyQueueWithoutAPlaylistLoop()
    {
        // Arrange
        var handler = new StubHttpMessageHandler(JsonResponse(
            """{"id":1,"result":{"playlist_tracks":0}}"""));
        using var httpClient = new HttpClient(handler);
        var client = CreateClient(httpClient);

        // Act
        var queue = await client.GetQueueAsync(
            PlayerId,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(queue.CurrentIndex);
        Assert.Empty(queue.Items);
    }

    [Fact]
    public async Task GetQueueShouldRejectAQueueAboveTheSupportedLimit()
    {
        // Arrange
        var handler = new StubHttpMessageHandler(JsonResponse(
            """{"id":1,"result":{"playlist_tracks":301,"playlist_cur_index":0,"playlist_loop":[]}}"""));
        using var httpClient = new HttpClient(handler);
        var client = CreateClient(httpClient);

        // Act
        var exception = await Assert.ThrowsAsync<LmsRequestException>(() =>
            client.GetQueueAsync(
                PlayerId,
                TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(
            "LMS queue contains more than the supported 300 items.",
            exception.Message);
    }

    [Fact]
    public async Task GetQueueShouldRejectAnIncompletePlaylistLoop()
    {
        // Arrange
        var handler = new StubHttpMessageHandler(JsonResponse(
            """
            {"id":1,"result":{"playlist_tracks":2,"playlist_cur_index":0,"playlist_loop":[{"playlist index":0,"title":"Lantern Signals"}]}}
            """));
        using var httpClient = new HttpClient(handler);
        var client = CreateClient(httpClient);

        // Act
        var exception = await Assert.ThrowsAsync<LmsRequestException>(() =>
            client.GetQueueAsync(
                PlayerId,
                TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(
            "LMS queue response did not include every queued item.",
            exception.Message);
    }

    private static LmsQueueClient CreateClient(HttpClient httpClient)
    {
        var settings = LmsConnectionSettings.FromValues(
            "development",
            "http://music.test:9000",
            null);
        return new LmsQueueClient(new LmsJsonRpcClient(settings, httpClient));
    }

    private static HttpResponseMessage JsonResponse(string json) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

    private sealed class StubHttpMessageHandler(HttpResponseMessage response)
        : HttpMessageHandler
    {
        public string? PlayerId { get; private set; }

        public string? CommandJson { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var body = await request.Content!.ReadAsStringAsync(cancellationToken);
            using var document = JsonDocument.Parse(body);
            var parameters = document.RootElement.GetProperty("params");
            PlayerId = parameters[0].GetString();
            CommandJson = parameters[1].GetRawText();
            return response;
        }
    }
}
