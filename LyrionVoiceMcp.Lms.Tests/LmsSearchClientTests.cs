using System.Net;
using System.Text;
using System.Text.Json;
using LyrionVoiceMcp.Abstractions;
using LyrionVoiceMcp.Lms;

namespace LyrionVoiceMcp.Lms.Tests;

public sealed class LmsSearchClientTests
{
    [Fact]
    public async Task SearchShouldMapLibraryAndPlaylistResultsInCategoryOrder()
    {
        // Arrange
        var handler = new StubHttpMessageHandler(command => command switch
        {
            "search" => JsonResponse(
                """
                {"id":1,"result":{
                  "contributors_loop":[{"contributor_id":11,"contributor":"The Copper Lines"}],
                  "albums_loop":[{"album_id":"22","album":"Fictional Frequencies"}],
                  "tracks_loop":[{"track_id":33,"track":"First Light","artist":"The Copper Lines","album":"Fictional Frequencies"}]
                }}
                """),
            "playlists" => JsonResponse(
                """
                {"id":1,"result":{"playlists_loop":[{"id":"44","playlist":"Morning Signals"}]}}
                """),
            _ => throw new InvalidOperationException($"Unexpected command {command}.")
        });
        using var client = new HttpClient(handler);
        var settings = ConfiguredSettings();
        var searchClient = new LmsSearchClient(new LmsJsonRpcClient(settings, client));

        // Act
        var response = await searchClient.SearchAsync(
            "copper",
            TestContext.Current.CancellationToken);
        var results = response.Candidates;

        // Assert
        Assert.Equal(4, results.Count);
        Assert.Collection(
            results,
            result => AssertCandidate(result, MediaEntityKind.Artist, "11", "The Copper Lines"),
            result =>
            {
                AssertCandidate(result, MediaEntityKind.Album, "22", "Fictional Frequencies");
                Assert.Null(result.Album);
            },
            result =>
            {
                AssertCandidate(result, MediaEntityKind.Track, "33", "First Light");
                Assert.Equal("The Copper Lines", result.Artist);
                Assert.Equal("Fictional Frequencies", result.Album);
            },
            result => AssertCandidate(result, MediaEntityKind.Playlist, "44", "Morning Signals"));
        Assert.All(handler.Commands, command => Assert.Contains("copper", command.Body, StringComparison.Ordinal));
        Assert.Collection(
            response.Requests,
            request => Assert.Equal(
                ("library", LmsSearchRequestStatus.Completed, 3),
                (request.Source, request.Status, request.ResultCount)),
            request => Assert.Equal(
                ("playlists", LmsSearchRequestStatus.Completed, 1),
                (request.Source, request.Status, request.ResultCount)));
    }

    [Fact]
    public async Task SearchShouldReturnEmptyWhenLmsOmitsEmptyLoops()
    {
        // Arrange
        var handler = new StubHttpMessageHandler(_ => JsonResponse("""{"id":1,"result":{"count":0}}"""));
        using var client = new HttpClient(handler);
        var settings = ConfiguredSettings();
        var searchClient = new LmsSearchClient(new LmsJsonRpcClient(settings, client));

        // Act
        var response = await searchClient.SearchAsync(
            "missing",
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(response.Candidates);
        Assert.Equal(2, handler.Commands.Count);
    }

    [Fact]
    public async Task SearchShouldRejectAnInvalidLoopShape()
    {
        // Arrange
        var handler = new StubHttpMessageHandler(command => command == "search"
            ? JsonResponse("""{"id":1,"result":{"tracks_loop":{}}}""")
            : JsonResponse("""{"id":1,"result":{"playlists_loop":[{"id":"44","playlist":"Morning Signals"}]}}"""));
        using var client = new HttpClient(handler);
        var settings = ConfiguredSettings();
        var searchClient = new LmsSearchClient(new LmsJsonRpcClient(settings, client));

        // Act
        var exception = await Assert.ThrowsAsync<LmsSearchFailedException>(() =>
            searchClient.SearchAsync("broken", TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal("LMS search failed for library.", exception.Message);
        Assert.Equal(
            "LMS search response did not include a valid tracks_loop array.",
            exception.InnerException?.Message);
        Assert.Collection(
            exception.Response.Requests,
            request =>
            {
                Assert.Equal(LmsSearchRequestStatus.Failed, request.Status);
                Assert.Contains("tracks_loop", request.FailureMessage, StringComparison.Ordinal);
            },
            request => Assert.Equal(LmsSearchRequestStatus.Completed, request.Status));
        Assert.Equal("Morning Signals", Assert.Single(exception.Response.Candidates).Title);
    }

    [Fact]
    public async Task SearchShouldPropagateCallerCancellation()
    {
        // Arrange
        var handler = new StubHttpMessageHandler(_ =>
            throw new InvalidOperationException("A cancelled request should not be handled."));
        using var client = new HttpClient(handler);
        var settings = ConfiguredSettings();
        var searchClient = new LmsSearchClient(new LmsJsonRpcClient(settings, client));
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        // Act
        var exception = await Record.ExceptionAsync(() =>
            searchClient.SearchAsync("cancelled", cancellation.Token));

        // Assert
        Assert.IsAssignableFrom<OperationCanceledException>(exception);
    }

    private static LmsConnectionSettings ConfiguredSettings() =>
        LmsConnectionSettings.FromValues(
            "development",
            "http://music.test:9000",
            null);

    private static HttpResponseMessage JsonResponse(string json) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

    private static void AssertCandidate(
        LmsSearchCandidate candidate,
        MediaEntityKind kind,
        string id,
        string title)
    {
        Assert.Equal(kind, candidate.Identity.Kind);
        Assert.Equal(id, candidate.Identity.Id);
        Assert.Equal(title, candidate.Title);
    }

    private sealed class StubHttpMessageHandler(
        Func<string, HttpResponseMessage> respond) : HttpMessageHandler
    {
        private readonly List<RecordedCommand> commands = [];

        public IReadOnlyList<RecordedCommand> Commands
        {
            get
            {
                lock (commands)
                {
                    return commands.ToArray();
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
            var command = document.RootElement
                .GetProperty("params")[1][0]
                .GetString()!;
            lock (commands)
            {
                commands.Add(new RecordedCommand(command, body));
            }

            return respond(command);
        }
    }

    private sealed record RecordedCommand(string Name, string Body);
}
