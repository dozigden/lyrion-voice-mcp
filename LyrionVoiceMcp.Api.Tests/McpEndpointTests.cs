using System.Net;
using System.Text;
using LyrionVoiceMcp.Abstractions;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace LyrionVoiceMcp.Api.Tests;

public sealed class McpEndpointTests : IClassFixture<LyrionVoiceMcpApiFactory>
{
    private const string ProtocolVersion = "2026-07-28";
    private readonly LyrionVoiceMcpApiFactory factory;

    public McpEndpointTests(LyrionVoiceMcpApiFactory factory)
    {
        this.factory = factory;
    }

    [Fact]
    public async Task DiscoveryShouldAdvertiseTheServerAndToolsCapability()
    {
        // Arrange
        using var client = factory.CreateClient();
        using var request = CreateRequest(
            "server/discover",
            1,
            """
            {"_meta":{"io.modelcontextprotocol/protocolVersion":"2026-07-28","io.modelcontextprotocol/clientInfo":{"name":"api-tests","version":"0.1.0"},"io.modelcontextprotocol/clientCapabilities":{}}}
            """);

        // Act
        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.True(
            response.StatusCode == HttpStatusCode.OK,
            $"Expected an OK MCP response but received {(int)response.StatusCode}: {body}");
        Assert.Contains("Lyrion Voice MCP", body, StringComparison.Ordinal);
        Assert.Contains("\"tools\"", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ListToolsShouldAdvertiseTheImplementedToolSurface()
    {
        // Arrange
        using var client = factory.CreateClient();
        using var request = CreateRequest(
            "tools/list",
            2,
            """
            {"_meta":{"io.modelcontextprotocol/protocolVersion":"2026-07-28","io.modelcontextprotocol/clientInfo":{"name":"api-tests","version":"0.1.0"},"io.modelcontextprotocol/clientCapabilities":{}}}
            """);

        // Act
        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("\"name\":\"search\"", body, StringComparison.Ordinal);
        Assert.Contains("\"required\":[\"query\"]", body, StringComparison.Ordinal);
        Assert.Contains("\"name\":\"get_player_status\"", body, StringComparison.Ordinal);
        Assert.Contains("\"name\":\"control_player\"", body, StringComparison.Ordinal);
        Assert.Contains("\"required\":[\"player\",\"action\"]", body, StringComparison.Ordinal);
        Assert.Contains(
            "\"enum\":[\"resume\",\"pause\",\"stop\",\"next\",\"previous\",\"power_on\",\"power_off\"]",
            body,
            StringComparison.Ordinal);
        Assert.Contains("\"name\":\"get_queue\"", body, StringComparison.Ordinal);
        Assert.Contains("\"required\":[\"player\"]", body, StringComparison.Ordinal);
        Assert.Contains("\"name\":\"play\"", body, StringComparison.Ordinal);
        Assert.Contains("\"required\":[\"player\",\"items\"]", body, StringComparison.Ordinal);
        Assert.Contains("\"enum\":[\"replace\",\"append\"]", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SearchToolShouldReturnStructuredMinimalCandidates()
    {
        // Arrange
        await using var searchFactory = factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<ISearchService>();
                services.AddSingleton<ISearchService>(new StubSearchService());
            }));
        using var client = searchFactory.CreateClient();
        using var request = CreateRequest(
            "tools/call",
            3,
            """
            {"_meta":{"io.modelcontextprotocol/protocolVersion":"2026-07-28","io.modelcontextprotocol/clientInfo":{"name":"api-tests","version":"0.1.0"},"io.modelcontextprotocol/clientCapabilities":{}},"name":"search","arguments":{"query":"copper lines"}}
            """,
            "search");

        // Act
        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.True(
            response.StatusCode == HttpStatusCode.OK,
            $"Expected an OK MCP response but received {(int)response.StatusCode}: {body}");
        Assert.Contains("\"structuredContent\"", body, StringComparison.Ordinal);
        Assert.Contains("opaque-reference", body, StringComparison.Ordinal);
        Assert.Contains("The Copper Lines", body, StringComparison.Ordinal);
        Assert.DoesNotContain("confidence", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SearchShouldReturnAnSdkToolErrorForARejectedQuery()
    {
        // Arrange
        var rejection = new SearchRejected(
            SearchRejectionReason.InvalidQuery,
            "The search query must not be empty.");
        await using var searchFactory = factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<ISearchService>();
                services.AddSingleton<ISearchService>(new StubSearchService(rejection));
            }));
        using var client = searchFactory.CreateClient();
        using var request = CreateRequest(
            "tools/call",
            7,
            """
            {"_meta":{"io.modelcontextprotocol/protocolVersion":"2026-07-28","io.modelcontextprotocol/clientInfo":{"name":"api-tests","version":"0.1.0"},"io.modelcontextprotocol/clientCapabilities":{}},"name":"search","arguments":{"query":" "}}
            """,
            "search");

        // Act
        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("\"isError\":true", body, StringComparison.Ordinal);
        Assert.Contains(rejection.Message, body, StringComparison.Ordinal);
        Assert.DoesNotContain("\"error\":", body, StringComparison.Ordinal);
        Assert.DoesNotContain("structuredContent", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetPlayerStatusShouldReturnStructuredFullPlayers()
    {
        // Arrange
        await using var playerFactory = factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IPlayerStatusService>();
                services.AddSingleton<IPlayerStatusService>(new StubPlayerStatusService());
            }));
        using var client = playerFactory.CreateClient();
        using var request = CreateRequest(
            "tools/call",
            4,
            """
            {"_meta":{"io.modelcontextprotocol/protocolVersion":"2026-07-28","io.modelcontextprotocol/clientInfo":{"name":"api-tests","version":"0.1.0"},"io.modelcontextprotocol/clientCapabilities":{}},"name":"get_player_status","arguments":{}}
            """,
            "get_player_status");

        // Act
        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.True(
            response.StatusCode == HttpStatusCode.OK,
            $"Expected an OK MCP response but received {(int)response.StatusCode}: {body}");
        Assert.Contains("\"structuredContent\"", body, StringComparison.Ordinal);
        Assert.Contains("00:11:22:33:44:55", body, StringComparison.Ordinal);
        Assert.Contains("North Room", body, StringComparison.Ordinal);
        Assert.Contains("\"poweredOn\":true", body, StringComparison.Ordinal);
        Assert.Contains("\"mode\":\"Stopped\"", body, StringComparison.Ordinal);
        Assert.Contains("\"volume\":42", body, StringComparison.Ordinal);
        Assert.Contains("\"muted\":false", body, StringComparison.Ordinal);
        Assert.Contains("Lantern Signals", body, StringComparison.Ordinal);
        Assert.Contains("The Paper Comets", body, StringComparison.Ordinal);
        Assert.Contains("\"durationSeconds\":244.25", body, StringComparison.Ordinal);
        Assert.Contains("\"elapsedSeconds\":12.5", body, StringComparison.Ordinal);
        Assert.DoesNotContain("queue", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PlayShouldAcceptAnOrderedBatchAndReturnStructuredFullPlayerStatus()
    {
        // Arrange
        var playbackService = new StubPlaybackService();
        await using var playbackFactory = factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IPlaybackService>();
                services.AddSingleton<IPlaybackService>(playbackService);
            }));
        using var client = playbackFactory.CreateClient();
        using var request = CreateRequest(
            "tools/call",
            5,
            """
            {"_meta":{"io.modelcontextprotocol/protocolVersion":"2026-07-28","io.modelcontextprotocol/clientInfo":{"name":"api-tests","version":"0.1.0"},"io.modelcontextprotocol/clientCapabilities":{}},"name":"play","arguments":{"player":"00:11:22:33:44:55","items":["first-reference","second-reference"],"mode":"append"}}
            """,
            "play");

        // Act
        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.True(
            response.StatusCode == HttpStatusCode.OK,
            $"Expected an OK MCP response but received {(int)response.StatusCode}: {body}");
        Assert.Equal(
            ["first-reference", "second-reference"],
            playbackService.References);
        Assert.Equal(PlaybackQueueMode.Append, playbackService.Mode);
        Assert.Contains("\"structuredContent\"", body, StringComparison.Ordinal);
        Assert.Contains("00:11:22:33:44:55", body, StringComparison.Ordinal);
        Assert.Contains("\"poweredOn\":true", body, StringComparison.Ordinal);
        Assert.Contains("\"mode\":\"Playing\"", body, StringComparison.Ordinal);
        Assert.DoesNotContain("queue", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ControlPlayerShouldApplyActionAndReturnUpdatedStatus()
    {
        // Arrange
        var controlService = new StubPlayerControlService();
        await using var controlFactory = factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IPlayerControlService>();
                services.AddSingleton<IPlayerControlService>(controlService);
            }));
        using var client = controlFactory.CreateClient();
        using var request = CreateRequest(
            "tools/call",
            9,
            """
            {"_meta":{"io.modelcontextprotocol/protocolVersion":"2026-07-28","io.modelcontextprotocol/clientInfo":{"name":"api-tests","version":"0.1.0"},"io.modelcontextprotocol/clientCapabilities":{}},"name":"control_player","arguments":{"player":"00:11:22:33:44:55","action":"pause"}}
            """,
            "control_player");

        // Act
        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(PlayerControlCommand.Pause, controlService.Command);
        Assert.Contains("\"structuredContent\"", body, StringComparison.Ordinal);
        Assert.Contains("00:11:22:33:44:55", body, StringComparison.Ordinal);
        Assert.Contains("\"mode\":\"Paused\"", body, StringComparison.Ordinal);
        Assert.DoesNotContain("queue", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetQueueShouldReturnTheOrderedQueueAndCurrentIndex()
    {
        // Arrange
        await using var queueFactory = factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IQueueService>();
                services.AddSingleton<IQueueService>(new StubQueueService());
            }));
        using var client = queueFactory.CreateClient();
        using var request = CreateRequest(
            "tools/call",
            11,
            """
            {"_meta":{"io.modelcontextprotocol/protocolVersion":"2026-07-28","io.modelcontextprotocol/clientInfo":{"name":"api-tests","version":"0.1.0"},"io.modelcontextprotocol/clientCapabilities":{}},"name":"get_queue","arguments":{"player":"00:11:22:33:44:55"}}
            """,
            "get_queue");

        // Act
        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("\"structuredContent\"", body, StringComparison.Ordinal);
        Assert.Contains("\"player\":\"00:11:22:33:44:55\"", body, StringComparison.Ordinal);
        Assert.Contains("\"currentIndex\":1", body, StringComparison.Ordinal);
        Assert.Contains("Lantern Signals", body, StringComparison.Ordinal);
        Assert.Contains("The midnight bulletin", body, StringComparison.Ordinal);
        Assert.DoesNotContain("reference", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("revision", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetQueueShouldReturnAToolErrorForARejectedPlayer()
    {
        // Arrange
        var rejection = new QueueRejected(
            QueueRejectionReason.PlayerNotFound,
            "LMS player 'missing-player' was not found.");
        await using var queueFactory = factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IQueueService>();
                services.AddSingleton<IQueueService>(new StubQueueService(rejection));
            }));
        using var client = queueFactory.CreateClient();
        using var request = CreateRequest(
            "tools/call",
            12,
            """
            {"_meta":{"io.modelcontextprotocol/protocolVersion":"2026-07-28","io.modelcontextprotocol/clientInfo":{"name":"api-tests","version":"0.1.0"},"io.modelcontextprotocol/clientCapabilities":{}},"name":"get_queue","arguments":{"player":"missing-player"}}
            """,
            "get_queue");

        // Act
        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("\"isError\":true", body, StringComparison.Ordinal);
        Assert.Contains("missing-player", body, StringComparison.Ordinal);
        Assert.Contains("was not found.", body, StringComparison.Ordinal);
        Assert.DoesNotContain("structuredContent", body, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("\"bogus\"")]
    [InlineData("null")]
    [InlineData("17")]
    [InlineData("{}")]
    public async Task ControlPlayerShouldReturnACorrectiveToolErrorForAnInvalidAction(
        string actionJson)
    {
        // Arrange
        var controlService = new StubPlayerControlService();
        await using var controlFactory = factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IPlayerControlService>();
                services.AddSingleton<IPlayerControlService>(controlService);
            }));
        using var client = controlFactory.CreateClient();
        using var request = CreateRequest(
            "tools/call",
            10,
            """
            {"_meta":{"io.modelcontextprotocol/protocolVersion":"2026-07-28","io.modelcontextprotocol/clientInfo":{"name":"api-tests","version":"0.1.0"},"io.modelcontextprotocol/clientCapabilities":{}},"name":"control_player","arguments":{"player":"00:11:22:33:44:55","action":ACTION_JSON}}
            """.Replace("ACTION_JSON", actionJson, StringComparison.Ordinal),
            "control_player");

        // Act
        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("\"isError\":true", body, StringComparison.Ordinal);
        Assert.Contains("The player control action is invalid.", body, StringComparison.Ordinal);
        Assert.DoesNotContain("An error occurred invoking", body, StringComparison.Ordinal);
        Assert.DoesNotContain("\"error\":", body, StringComparison.Ordinal);
        Assert.Null(controlService.Command);
    }

    [Fact]
    public async Task PlayShouldReturnAnSdkToolErrorForARejectedRequest()
    {
        // Arrange
        var rejection = new PlaybackRejected(
            PlaybackRejectionReason.InvalidReference,
            "Search-result item 1 has an invalid reference.");
        await using var playbackFactory = factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IPlaybackService>();
                services.AddSingleton<IPlaybackService>(new StubPlaybackService(rejection));
            }));
        using var client = playbackFactory.CreateClient();
        using var request = CreateRequest(
            "tools/call",
            6,
            """
            {"_meta":{"io.modelcontextprotocol/protocolVersion":"2026-07-28","io.modelcontextprotocol/clientInfo":{"name":"api-tests","version":"0.1.0"},"io.modelcontextprotocol/clientCapabilities":{}},"name":"play","arguments":{"player":"00:11:22:33:44:55","items":["invalid-reference"]}}
            """,
            "play");

        // Act
        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("\"isError\":true", body, StringComparison.Ordinal);
        Assert.Contains(rejection.Message, body, StringComparison.Ordinal);
        Assert.DoesNotContain("\"error\":", body, StringComparison.Ordinal);
        Assert.DoesNotContain("structuredContent", body, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("\"bogus\"")]
    [InlineData("null")]
    [InlineData("17")]
    [InlineData("{}")]
    public async Task PlayShouldReturnACorrectiveToolErrorForAnInvalidMode(
        string modeJson)
    {
        // Arrange
        var playbackService = new StubPlaybackService();
        await using var playbackFactory = factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IPlaybackService>();
                services.AddSingleton<IPlaybackService>(playbackService);
            }));
        using var client = playbackFactory.CreateClient();
        using var request = CreateRequest(
            "tools/call",
            8,
            """
            {"_meta":{"io.modelcontextprotocol/protocolVersion":"2026-07-28","io.modelcontextprotocol/clientInfo":{"name":"api-tests","version":"0.1.0"},"io.modelcontextprotocol/clientCapabilities":{}},"name":"play","arguments":{"player":"00:11:22:33:44:55","items":["first-reference"],"mode":MODE_JSON}}
            """.Replace("MODE_JSON", modeJson, StringComparison.Ordinal),
            "play");

        // Act
        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("\"isError\":true", body, StringComparison.Ordinal);
        Assert.Contains("The playback queue mode is invalid.", body, StringComparison.Ordinal);
        Assert.DoesNotContain("An error occurred invoking", body, StringComparison.Ordinal);
        Assert.DoesNotContain("\"error\":", body, StringComparison.Ordinal);
        Assert.Null(playbackService.References);
    }

    private static HttpRequestMessage CreateRequest(
        string method,
        int id,
        string parameters,
        string? name = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/mcp")
        {
            Content = new StringContent(
                $"{{\"jsonrpc\":\"2.0\",\"id\":{id},\"method\":\"{method}\",\"params\":{parameters}}}",
                Encoding.UTF8,
                "application/json")
        };
        request.Headers.Accept.ParseAdd("application/json, text/event-stream");
        request.Headers.TryAddWithoutValidation("MCP-Protocol-Version", ProtocolVersion);
        request.Headers.TryAddWithoutValidation("Mcp-Method", method);
        if (name is not null)
        {
            request.Headers.TryAddWithoutValidation("Mcp-Name", name);
        }

        return request;
    }

    private sealed class StubSearchService(SearchOutcome? outcome = null) : ISearchService
    {
        public Task<SearchOutcome> SearchAsync(
            string query,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (outcome is not null)
            {
                return Task.FromResult(outcome);
            }

            Assert.Equal("copper lines", query);
            return Task.FromResult<SearchOutcome>(new SearchSucceeded(
            [
                new(
                    "opaque-reference",
                    MediaEntityKind.Artist,
                    "The Copper Lines",
                    null,
                    null)
            ]));
        }
    }

    private sealed class StubPlayerStatusService : IPlayerStatusService
    {
        public Task<IReadOnlyList<LmsPlayerStatus>> GetPlayersAsync(
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IReadOnlyList<LmsPlayerStatus> players =
            [
                new(
                    "00:11:22:33:44:55",
                    "North Room",
                    true,
                    PlayerPlaybackState.Stopped,
                    42,
                    false,
                    new LmsNowPlaying(
                        "Lantern Signals",
                        "The Paper Comets",
                        "Night Routes",
                        244.25,
                        12.5))
            ];
            return Task.FromResult(players);
        }
    }

    private sealed class StubPlaybackService(PlaybackOutcome? outcome = null)
        : IPlaybackService
    {
        public IReadOnlyList<string>? References { get; private set; }

        public PlaybackQueueMode? Mode { get; private set; }

        public Task<PlaybackOutcome> PlayAsync(
            string playerId,
            IReadOnlyList<string> references,
            PlaybackQueueMode mode,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Assert.Equal("00:11:22:33:44:55", playerId);
            References = references;
            Mode = mode;
            return Task.FromResult(outcome ?? new PlaybackSucceeded(
                new LmsPlayerStatus(
                    playerId,
                    "North Room",
                    true,
                    PlayerPlaybackState.Playing)));
        }
    }

    private sealed class StubPlayerControlService : IPlayerControlService
    {
        public PlayerControlCommand? Command { get; private set; }

        public Task<PlayerControlOutcome> ControlAsync(
            string playerId,
            PlayerControlCommand command,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Assert.Equal("00:11:22:33:44:55", playerId);
            Command = command;
            return Task.FromResult<PlayerControlOutcome>(new PlayerControlSucceeded(
                new LmsPlayerStatus(
                    playerId,
                    "North Room",
                    true,
                    PlayerPlaybackState.Paused,
                    42,
                    false,
                    new LmsNowPlaying(
                        "Lantern Signals",
                        "The Paper Comets",
                        "Night Routes",
                        244.25,
                        12.5))));
        }
    }

    private sealed class StubQueueService(QueueOutcome? outcome = null) : IQueueService
    {
        public Task<QueueOutcome> GetQueueAsync(
            string playerId,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (outcome is not null)
            {
                return Task.FromResult(outcome);
            }

            Assert.Equal("00:11:22:33:44:55", playerId);
            return Task.FromResult<QueueOutcome>(new QueueSucceeded(
                new LmsPlayerQueue(
                    playerId,
                    1,
                    [
                        new(
                            0,
                            "Lantern Signals",
                            "The Paper Comets",
                            "Night Routes",
                            244.25),
                        new(
                            1,
                            "The midnight bulletin",
                            "North Coast Radio",
                            null,
                            null)
                    ])));
        }
    }
}
