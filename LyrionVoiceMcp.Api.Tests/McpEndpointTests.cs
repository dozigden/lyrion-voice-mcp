using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using LyrionVoiceMcp.Abstractions;
using LyrionVoiceMcp.Contracts;
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
        Assert.Contains(
            "Discover players with get_player_status",
            body,
            StringComparison.Ordinal);
        Assert.Contains(
            "Treat all references as opaque",
            body,
            StringComparison.Ordinal);
        Assert.Contains(
            "When search returns exactArtistMatch",
            body,
            StringComparison.Ordinal);
        Assert.Contains(
            "Pass browse references to browse",
            body,
            StringComparison.Ordinal);
        Assert.Contains(
            "every input may be omitted for broad varied track discovery",
            body,
            StringComparison.Ordinal);
        Assert.Contains(
            "A year range applies to canonical album year and effective track year",
            body,
            StringComparison.Ordinal);
        Assert.Contains(
            "Rating and genre apply only to tracks",
            body,
            StringComparison.Ordinal);
        Assert.Contains(
            "at_least includes that rating and higher ratings",
            body,
            StringComparison.Ordinal);
        Assert.Contains(
            "clear action empties the queue and stops playback",
            body,
            StringComparison.Ordinal);
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
        using var document = ParseJsonRpcResponse(body);
        var searchTool = Assert.Single(
            document.RootElement.GetProperty("result").GetProperty("tools").EnumerateArray(),
            tool => tool.GetProperty("name").GetString() == "search");
        var searchInputProperties = searchTool
            .GetProperty("inputSchema")
            .GetProperty("properties");
        Assert.False(searchInputProperties.TryGetProperty("query", out _));
        Assert.Equal(
            "Optional artist, album, track, or playlist name text, up to 500 characters and 20 words. Omit it or leave it blank for rating-, genre-, or year-filtered discovery; omit every input for broad varied track discovery. Do not include constraints or search syntax in the name. Wildcards are not supported.",
            searchInputProperties.GetProperty("name").GetProperty("description").GetString());
        Assert.False(searchInputProperties.TryGetProperty("kind", out _));
        Assert.True(searchInputProperties.TryGetProperty("genre", out _));
        Assert.True(searchInputProperties.TryGetProperty("fromYear", out _));
        Assert.True(searchInputProperties.TryGetProperty("toYear", out _));
        if (searchTool.GetProperty("inputSchema").TryGetProperty("required", out var required))
        {
            Assert.DoesNotContain(required.EnumerateArray(), item => item.GetString() == "name");
        }
        var ratingInput = searchInputProperties.GetProperty("rating");
        Assert.Equal(
            "Optional numeric track rating from 0 to 5, including decimals. Supply together with ratingMatch; do not put the rating in name.",
            ratingInput.GetProperty("description").GetString());
        Assert.Equal(0m, ratingInput.GetProperty("minimum").GetDecimal());
        Assert.Equal(5m, ratingInput.GetProperty("maximum").GetDecimal());
        Assert.Equal(
            ["exact", "at_least"],
            searchInputProperties.GetProperty("ratingMatch").GetProperty("enum")
                .EnumerateArray().Select(value => value.GetString()));
        var trackSchema = searchTool
            .GetProperty("outputSchema")
            .GetProperty("properties")
            .GetProperty("topTracks")
            .GetProperty("items");
        var searchOutput = searchTool.GetProperty("outputSchema");
        var exactArtistSchema = searchOutput
            .GetProperty("properties")
            .GetProperty("exactArtistMatch");
        Assert.Contains(
            searchOutput.GetProperty("required").EnumerateArray(),
            property => property.GetString() == "exactArtistMatch");
        Assert.Contains(
            exactArtistSchema.GetProperty("type").EnumerateArray(),
            type => type.GetString() == "null");
        Assert.Contains(
            exactArtistSchema.GetProperty("required").EnumerateArray(),
            property => property.GetString() == "discographyAlbumCount");
        var discographyAlbumCountSchema = exactArtistSchema
            .GetProperty("properties")
            .GetProperty("discographyAlbumCount");
        Assert.Contains(
            discographyAlbumCountSchema.GetProperty("type").EnumerateArray(),
            type => type.GetString() == "integer");
        Assert.Contains(
            discographyAlbumCountSchema.GetProperty("type").EnumerateArray(),
            type => type.GetString() == "null");
        Assert.True(searchTool
            .GetProperty("outputSchema")
            .GetProperty("properties")
            .TryGetProperty("tracks", out _));
        Assert.Equal(
            "number",
            trackSchema.GetProperty("properties").GetProperty("rating")
                .GetProperty("type").GetString());
        Assert.Contains(
            trackSchema.GetProperty("required").EnumerateArray(),
            property => property.GetString() == "rating");
        Assert.Equal(
            "Search the music library by optional name, exact genre, inclusive year range, rating, or a combination. Omit every input for broad varied track discovery. Reports a unique exact artist separately, returns 4+ top tracks separately, and varies selections. A year range can return albums and tracks; genre or rating makes the request track-only. * is not a wildcard.",
            searchTool.GetProperty("description").GetString());
        Assert.Contains("\"name\":\"browse\"", body, StringComparison.Ordinal);
        var browseTool = Assert.Single(
            document.RootElement.GetProperty("result").GetProperty("tools").EnumerateArray(),
            tool => tool.GetProperty("name").GetString() == "browse");
        Assert.True(
            browseTool.GetProperty("inputSchema").GetProperty("properties")
                .TryGetProperty("browseRef", out _));
        var browseItemRequired = browseTool.GetProperty("outputSchema")
            .GetProperty("properties").GetProperty("items").GetProperty("items")
            .GetProperty("required").EnumerateArray().ToArray();
        Assert.DoesNotContain(browseItemRequired, property =>
            property.GetString() == "browseRef");
        Assert.DoesNotContain(browseItemRequired, property =>
            property.GetString() == "playRef");
        Assert.Contains("\"name\":\"get_player_status\"", body, StringComparison.Ordinal);
        Assert.Contains("\"name\":\"control_player\"", body, StringComparison.Ordinal);
        Assert.Contains("\"required\":[\"player\",\"action\"]", body, StringComparison.Ordinal);
        Assert.Contains(
            "\"enum\":[\"resume\",\"pause\",\"stop\",\"next\",\"previous\",\"power_on\",\"power_off\"]",
            body,
            StringComparison.Ordinal);
        Assert.Contains("\"name\":\"get_queue\"", body, StringComparison.Ordinal);
        Assert.Contains("\"required\":[\"player\"]", body, StringComparison.Ordinal);
        Assert.Contains("\"name\":\"manage_queue\"", body, StringComparison.Ordinal);
        Assert.Contains(
            "\"enum\":[\"clear\",\"append\",\"insert_next\"]",
            body,
            StringComparison.Ordinal);
        Assert.Contains("\"name\":\"play\"", body, StringComparison.Ordinal);
        Assert.Contains("\"required\":[\"player\",\"items\"]", body, StringComparison.Ordinal);
        Assert.Equal(
            4,
            body.Split(
                "A raw LMS player ID or exact unique player name returned by get_player_status.",
                StringSplitOptions.None).Length - 1);
        Assert.DoesNotContain(
            "\"enum\":[\"replace\",\"append\"]",
            body,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task SearchToolShouldPassNumericRatingConstraintToTheService()
    {
        var search = new CapturingSearchService();
        await using var searchFactory = factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<ISearchService>();
                services.AddSingleton<ISearchService>(search);
            }));
        using var client = searchFactory.CreateClient();
        using var request = CreateRequest(
            "tools/call",
            31,
            """
            {"_meta":{"io.modelcontextprotocol/protocolVersion":"2026-07-28","io.modelcontextprotocol/clientInfo":{"name":"api-tests","version":"0.1.0"},"io.modelcontextprotocol/clientCapabilities":{}},"name":"search","arguments":{"rating":4.5,"ratingMatch":"at_least"}}
            """,
            "search");

        var response = await client.SendAsync(
            request,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Null(search.Criteria?.Query);
        Assert.Equal(4.5m, search.Criteria?.RatingConstraint?.Rating);
        Assert.Equal(RatingMatchMode.AtLeast, search.Criteria?.RatingConstraint?.Match);
    }

    [Fact]
    public async Task SearchToolShouldPassAnEmptyRequestForBroadDiscovery()
    {
        var search = new CapturingSearchService();
        await using var searchFactory = factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<ISearchService>();
                services.AddSingleton<ISearchService>(search);
            }));
        using var client = searchFactory.CreateClient();
        using var request = CreateRequest(
            "tools/call",
            34,
            """
            {"_meta":{"io.modelcontextprotocol/protocolVersion":"2026-07-28","io.modelcontextprotocol/clientInfo":{"name":"api-tests","version":"0.1.0"},"io.modelcontextprotocol/clientCapabilities":{}},"name":"search","arguments":{}}
            """,
            "search");

        var response = await client.SendAsync(
            request,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(new SearchCriteria(null), search.Criteria);
    }

    [Fact]
    public async Task SearchToolShouldPassNameFreeGenreAndYearConstraintsToTheService()
    {
        var search = new CapturingSearchService();
        await using var searchFactory = factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<ISearchService>();
                services.AddSingleton<ISearchService>(search);
            }));
        using var client = searchFactory.CreateClient();
        using var request = CreateRequest(
            "tools/call",
            33,
            """
            {"_meta":{"io.modelcontextprotocol/protocolVersion":"2026-07-28","io.modelcontextprotocol/clientInfo":{"name":"api-tests","version":"0.1.0"},"io.modelcontextprotocol/clientCapabilities":{}},"name":"search","arguments":{"genre":"Pop","fromYear":90,"toYear":99}}
            """,
            "search");

        var response = await client.SendAsync(
            request,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Null(search.Criteria?.Query);
        Assert.Equal("Pop", search.Criteria?.Genre);
        Assert.Equal(90, search.Criteria?.FromYear);
        Assert.Equal(99, search.Criteria?.ToYear);
    }

    [Fact]
    public async Task SearchToolShouldRejectAnIncompleteRatingConstraint()
    {
        await using var searchFactory = factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<ISearchService>();
                services.AddSingleton<ISearchService>(new CapturingSearchService());
            }));
        using var client = searchFactory.CreateClient();
        using var request = CreateRequest(
            "tools/call",
            32,
            """
            {"_meta":{"io.modelcontextprotocol/protocolVersion":"2026-07-28","io.modelcontextprotocol/clientInfo":{"name":"api-tests","version":"0.1.0"},"io.modelcontextprotocol/clientCapabilities":{}},"name":"search","arguments":{"name":"copper lines","rating":4}}
            """,
            "search");

        var response = await client.SendAsync(
            request,
            TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("must be supplied together", body, StringComparison.Ordinal);
        Assert.Contains("\"isError\":true", body, StringComparison.Ordinal);
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
            {"_meta":{"io.modelcontextprotocol/protocolVersion":"2026-07-28","io.modelcontextprotocol/clientInfo":{"name":"api-tests","version":"0.1.0"},"io.modelcontextprotocol/clientCapabilities":{}},"name":"search","arguments":{"name":"copper lines"}}
            """,
            "search");

        // Act
        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var history = await client.GetFromJsonAsync<ToolCallPageResponse>(
            "/api/tool-calls?toolName=search",
            TestContext.Current.CancellationToken);
        var summary = Assert.Single(history!.Items, item => item.TraceIdentifier == "3");
        var recorded = await client.GetFromJsonAsync<ToolCallResponse>(
            $"/api/tool-calls/{summary.Id}",
            TestContext.Current.CancellationToken);

        // Assert
        Assert.True(
            response.StatusCode == HttpStatusCode.OK,
            $"Expected an OK MCP response but received {(int)response.StatusCode}: {body}");
        Assert.Contains("\"structuredContent\"", body, StringComparison.Ordinal);
        Assert.Contains("opaque-reference", body, StringComparison.Ordinal);
        Assert.Contains("The Copper Lines", body, StringComparison.Ordinal);
        using var document = ParseJsonRpcResponse(body);
        var structuredContent = document.RootElement
            .GetProperty("result")
            .GetProperty("structuredContent");
        Assert.Equal(JsonValueKind.Null, structuredContent.GetProperty("exactArtistMatch").ValueKind);
        var artist = Assert.Single(structuredContent.GetProperty("artists").EnumerateArray());
        Assert.Equal("The Copper Lines", artist.GetProperty("name").GetString());
        Assert.Equal("opaque-reference", artist.GetProperty("browseRef").GetString());
        Assert.False(artist.TryGetProperty("playRef", out _));
        var album = Assert.Single(structuredContent.GetProperty("albums").EnumerateArray());
        Assert.Equal("Copper Signals", album.GetProperty("title").GetString());
        Assert.Equal("album-reference", album.GetProperty("browseRef").GetString());
        Assert.Equal("album-reference", album.GetProperty("playRef").GetString());
        var playlist = Assert.Single(
            structuredContent.GetProperty("playlists").EnumerateArray());
        Assert.Equal("playlist-reference", playlist.GetProperty("browseRef").GetString());
        Assert.Equal("playlist-reference", playlist.GetProperty("playRef").GetString());
        var tracks = structuredContent.GetProperty("tracks").EnumerateArray().ToArray();
        var topTrack = Assert.Single(
            structuredContent.GetProperty("topTracks").EnumerateArray());
        Assert.Equal("Copper Favourite", topTrack.GetProperty("title").GetString());
        Assert.Equal(5m, topTrack.GetProperty("rating").GetDecimal());
        Assert.Equal(
            4.5m,
            Assert.Single(tracks, result =>
                result.GetProperty("title").GetString() == "Ninety Point Signal")
                .GetProperty("rating").GetDecimal());
        Assert.Equal(
            3.35m,
            Assert.Single(tracks, result =>
                result.GetProperty("title").GetString() == "Odd Point Signal")
                .GetProperty("rating").GetDecimal());
        Assert.Equal(
            0m,
            Assert.Single(tracks, result =>
                result.GetProperty("title").GetString() == "Missing Rating Signal")
                .GetProperty("rating").GetDecimal());
        Assert.Equal(
            0m,
            Assert.Single(tracks, result =>
                result.GetProperty("title").GetString() == "Zero Rating Signal")
                .GetProperty("rating").GetDecimal());
        Assert.Contains("Pass a browseRef", structuredContent.GetProperty("guidance").GetString());
        Assert.DoesNotContain("confidence", body, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("succeeded", recorded!.Status);
        Assert.Contains("copper lines", recorded.ArgumentsJson, StringComparison.Ordinal);
        Assert.Contains("The Copper Lines", recorded.ResultJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SearchToolShouldExposeAResolvedExactArtistSeparately()
    {
        // Arrange
        var outcome = new SearchSucceeded(
            [],
            [],
            new ExactArtistMatchResult(
                "The Copper Lines",
                7,
                "album_artist_discography-reference"));
        await using var searchFactory = factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<ISearchService>();
                services.AddSingleton<ISearchService>(new StubSearchService(outcome));
            }));
        using var client = searchFactory.CreateClient();
        using var request = CreateRequest(
            "tools/call",
            33,
            """
            {"_meta":{"io.modelcontextprotocol/protocolVersion":"2026-07-28","io.modelcontextprotocol/clientInfo":{"name":"api-tests","version":"0.1.0"},"io.modelcontextprotocol/clientCapabilities":{}},"name":"search","arguments":{"name":"copper lines"}}
            """,
            "search");

        // Act
        var response = await client.SendAsync(
            request,
            TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(
            TestContext.Current.CancellationToken);

        // Assert
        Assert.True(
            response.StatusCode == HttpStatusCode.OK,
            $"Expected an OK MCP response but received {(int)response.StatusCode}: {body}");
        using var document = ParseJsonRpcResponse(body);
        var structuredContent = document.RootElement
            .GetProperty("result")
            .GetProperty("structuredContent");
        var exactArtist = structuredContent.GetProperty("exactArtistMatch");
        Assert.Equal("The Copper Lines", exactArtist.GetProperty("name").GetString());
        Assert.Equal(7, exactArtist.GetProperty("discographyAlbumCount").GetInt32());
        Assert.Equal(
            "album_artist_discography-reference",
            exactArtist.GetProperty("discographyBrowseRef").GetString());
        Assert.Empty(structuredContent.GetProperty("artists").EnumerateArray());
    }

    [Fact]
    public async Task SearchToolShouldEmitAnExplicitNullCountWhenAlbumExpansionWasSkipped()
    {
        var outcome = new SearchSucceeded(
            [],
            [],
            new ExactArtistMatchResult(
                "The Copper Lines",
                null,
                "album_artist_discography-reference"));
        await using var searchFactory = factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<ISearchService>();
                services.AddSingleton<ISearchService>(new StubSearchService(outcome));
            }));
        using var client = searchFactory.CreateClient();
        using var request = CreateRequest(
            "tools/call",
            34,
            """
            {"_meta":{"io.modelcontextprotocol/protocolVersion":"2026-07-28","io.modelcontextprotocol/clientInfo":{"name":"api-tests","version":"0.1.0"},"io.modelcontextprotocol/clientCapabilities":{}},"name":"search","arguments":{"name":"copper lines"}}
            """,
            "search");

        var response = await client.SendAsync(
            request,
            TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(
            TestContext.Current.CancellationToken);

        Assert.True(
            response.StatusCode == HttpStatusCode.OK,
            $"Expected an OK MCP response but received {(int)response.StatusCode}: {body}");
        using var document = ParseJsonRpcResponse(body);
        var exactArtist = document.RootElement
            .GetProperty("result")
            .GetProperty("structuredContent")
            .GetProperty("exactArtistMatch");
        Assert.Equal(
            JsonValueKind.Null,
            exactArtist.GetProperty("discographyAlbumCount").ValueKind);
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
            {"_meta":{"io.modelcontextprotocol/protocolVersion":"2026-07-28","io.modelcontextprotocol/clientInfo":{"name":"api-tests","version":"0.1.0"},"io.modelcontextprotocol/clientCapabilities":{}},"name":"search","arguments":{"name":" "}}
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
    public async Task UnexpectedToolExceptionShouldBeRecordedAndLinkedToTheToolCall()
    {
        // Arrange
        await using var searchFactory = factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<ISearchService>();
                services.AddSingleton<ISearchService>(new ThrowingSearchService());
            }));
        using var client = searchFactory.CreateClient();
        using var request = CreateRequest(
            "tools/call",
            15,
            """
            {"_meta":{"io.modelcontextprotocol/protocolVersion":"2026-07-28","io.modelcontextprotocol/clientInfo":{"name":"api-tests","version":"0.1.0"},"io.modelcontextprotocol/clientCapabilities":{}},"name":"search","arguments":{"name":"force unexpected failure"}}
            """,
            "search");

        // Act
        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var history = await client.GetFromJsonAsync<ToolCallPageResponse>(
            "/api/tool-calls?toolName=search",
            TestContext.Current.CancellationToken);
        var summary = Assert.Single(history!.Items, item => item.TraceIdentifier == "15");
        var recorded = await client.GetFromJsonAsync<ToolCallResponse>(
            $"/api/tool-calls/{summary.Id}",
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("\"isError\":true", body, StringComparison.Ordinal);
        Assert.Equal("failed", recorded!.Status);
        Assert.Contains("force unexpected failure", recorded.ArgumentsJson, StringComparison.Ordinal);
        Assert.NotNull(recorded.ErrorLogId);

        var error = await client.GetFromJsonAsync<ErrorLogResponse>(
            $"/api/error-logs/{recorded.ErrorLogId}",
            TestContext.Current.CancellationToken);
        Assert.Equal(ErrorLogSources.Mcp, error!.Source);
        Assert.Equal(ErrorLogAreas.McpToolCall, error.Area);
        Assert.Contains("Deliberate unexpected search failure.", error.Message, StringComparison.Ordinal);
        Assert.Contains(recorded.Id, error.ContextJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MissingRequiredArgumentShouldBeRecordedAsAToolErrorNotAnApplicationError()
    {
        using var client = factory.CreateClient();
        using var request = CreateRequest(
            "tools/call",
            16,
            """
            {"_meta":{"io.modelcontextprotocol/protocolVersion":"2026-07-28","io.modelcontextprotocol/clientInfo":{"name":"api-tests","version":"0.1.0"},"io.modelcontextprotocol/clientCapabilities":{}},"name":"search","arguments":{}}
            """,
            "search");

        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var history = await client.GetFromJsonAsync<ToolCallPageResponse>(
            "/api/tool-calls?toolName=search",
            TestContext.Current.CancellationToken);
        var summary = Assert.Single(history!.Items, item => item.TraceIdentifier == "16");
        var recorded = await client.GetFromJsonAsync<ToolCallResponse>(
            $"/api/tool-calls/{summary.Id}",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("\"isError\":true", body, StringComparison.Ordinal);
        Assert.Equal("tool_error", recorded!.Status);
        Assert.Equal("{}", recorded.ArgumentsJson);
        Assert.Null(recorded.ErrorLogId);
    }

    [Fact]
    public async Task BrowseToolShouldReturnStructuredMinimalItems()
    {
        // Arrange
        await using var browseFactory = factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IBrowseService>();
                services.AddSingleton<IBrowseService>(new StubBrowseService());
            }));
        using var client = browseFactory.CreateClient();
        using var request = CreateRequest(
            "tools/call",
            8,
            """
            {"_meta":{"io.modelcontextprotocol/protocolVersion":"2026-07-28","io.modelcontextprotocol/clientInfo":{"name":"api-tests","version":"0.1.0"},"io.modelcontextprotocol/clientCapabilities":{}},"name":"browse","arguments":{}}
            """,
            "browse");

        // Act
        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("\"structuredContent\"", body, StringComparison.Ordinal);
        Assert.Contains("browse-album-artists", body, StringComparison.Ordinal);
        Assert.Contains("Album artists", body, StringComparison.Ordinal);
        Assert.Contains("album_artist", body, StringComparison.Ordinal);
        Assert.Contains("\"artist\":null", body, StringComparison.Ordinal);
        Assert.Contains("\"album\":null", body, StringComparison.Ordinal);
        Assert.Contains("\"browseRef\":\"browse-album-artists\"", body, StringComparison.Ordinal);
        Assert.DoesNotContain("\"browsable\"", body, StringComparison.Ordinal);
        Assert.DoesNotContain("\"playable\"", body, StringComparison.Ordinal);
        Assert.Contains("\"rating\":4.5", body, StringComparison.Ordinal);
        Assert.Contains("\"nextBrowseRef\":\"browse-next\"", body, StringComparison.Ordinal);
        Assert.Contains("Pass a browseRef", body, StringComparison.Ordinal);
        using var document = ParseJsonRpcResponse(body);
        var items = document.RootElement.GetProperty("result")
            .GetProperty("structuredContent").GetProperty("items")
            .EnumerateArray().ToArray();
        var albumArtist = Assert.Single(items, item =>
            item.GetProperty("kind").GetString() == "album_artist");
        Assert.True(albumArtist.TryGetProperty("browseRef", out _));
        Assert.False(albumArtist.TryGetProperty("playRef", out _));
        var track = Assert.Single(items, item =>
            string.Equals(
                item.GetProperty("kind").GetString(),
                "Track",
                StringComparison.OrdinalIgnoreCase));
        Assert.False(track.TryGetProperty("browseRef", out _));
        Assert.True(track.TryGetProperty("playRef", out _));
    }

    [Fact]
    public async Task BrowseShouldReturnAnSdkToolErrorForAnInvalidReference()
    {
        // Arrange
        var rejection = new BrowseRejected(
            BrowseRejectionReason.InvalidReference,
            "The browse reference is invalid.");
        await using var browseFactory = factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IBrowseService>();
                services.AddSingleton<IBrowseService>(new StubBrowseService(rejection));
            }));
        using var client = browseFactory.CreateClient();
        using var request = CreateRequest(
            "tools/call",
            9,
            """
            {"_meta":{"io.modelcontextprotocol/protocolVersion":"2026-07-28","io.modelcontextprotocol/clientInfo":{"name":"api-tests","version":"0.1.0"},"io.modelcontextprotocol/clientCapabilities":{}},"name":"browse","arguments":{"browseRef":"invalid"}}
            """,
            "browse");

        // Act
        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("\"isError\":true", body, StringComparison.Ordinal);
        Assert.Contains(rejection.Message, body, StringComparison.Ordinal);
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
        Assert.Contains("Quiet Room", body, StringComparison.Ordinal);
        Assert.Contains("\"volume\":null", body, StringComparison.Ordinal);
        Assert.Contains("\"muted\":null", body, StringComparison.Ordinal);
        Assert.Contains("\"artist\":null", body, StringComparison.Ordinal);
        Assert.Contains("\"album\":null", body, StringComparison.Ordinal);
        Assert.Contains("\"durationSeconds\":null", body, StringComparison.Ordinal);
        Assert.Contains("\"elapsedSeconds\":null", body, StringComparison.Ordinal);
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
            {"_meta":{"io.modelcontextprotocol/protocolVersion":"2026-07-28","io.modelcontextprotocol/clientInfo":{"name":"api-tests","version":"0.1.0"},"io.modelcontextprotocol/clientCapabilities":{}},"name":"play","arguments":{"player":"00:11:22:33:44:55","items":["first-reference","second-reference"]}}
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
        Assert.Contains("\"structuredContent\"", body, StringComparison.Ordinal);
        Assert.Contains("00:11:22:33:44:55", body, StringComparison.Ordinal);
        Assert.Contains("\"poweredOn\":true", body, StringComparison.Ordinal);
        Assert.Contains("\"mode\":\"Playing\"", body, StringComparison.Ordinal);
        Assert.Contains("\"volume\":null", body, StringComparison.Ordinal);
        Assert.Contains("\"muted\":null", body, StringComparison.Ordinal);
        Assert.Contains("\"nowPlaying\":null", body, StringComparison.Ordinal);
        Assert.Contains("\"requestedItemCount\":2", body, StringComparison.Ordinal);
        Assert.Contains("\"completedItemCount\":2", body, StringComparison.Ordinal);
        Assert.Contains("\"skippedItems\":[]", body, StringComparison.Ordinal);
        Assert.Contains("\"stateRefreshError\":null", body, StringComparison.Ordinal);
        Assert.DoesNotContain("queue", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PlayShouldExposePartialBatchDetailsAndNullableRefreshState()
    {
        // Arrange
        var outcome = new PlaybackSucceeded(
            null,
            3,
            1,
            [
                new SkippedMediaItem(
                    2,
                    MediaItemSkipReason.MediaUnavailable,
                    "The media is no longer available."),
                new SkippedMediaItem(
                    3,
                    MediaItemSkipReason.NotAttempted,
                    "Not attempted after an earlier LMS failure.")
            ],
            "Player status could not be refreshed.");
        await using var playbackFactory = factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IPlaybackService>();
                services.AddSingleton<IPlaybackService>(new StubPlaybackService(outcome));
            }));
        using var client = playbackFactory.CreateClient();
        using var request = CreateRequest(
            "tools/call",
            16,
            """
            {"_meta":{"io.modelcontextprotocol/protocolVersion":"2026-07-28","io.modelcontextprotocol/clientInfo":{"name":"api-tests","version":"0.1.0"},"io.modelcontextprotocol/clientCapabilities":{}},"name":"play","arguments":{"player":"00:11:22:33:44:55","items":["first-reference","second-reference","third-reference"]}}
            """,
            "play");

        // Act
        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("\"structuredContent\"", body, StringComparison.Ordinal);
        Assert.Contains("\"player\":null", body, StringComparison.Ordinal);
        Assert.Contains("\"requestedItemCount\":3", body, StringComparison.Ordinal);
        Assert.Contains("\"completedItemCount\":1", body, StringComparison.Ordinal);
        Assert.Contains("\"index\":2", body, StringComparison.Ordinal);
        Assert.Contains("\"reason\":\"media_unavailable\"", body, StringComparison.Ordinal);
        Assert.Contains("\"reason\":\"not_attempted\"", body, StringComparison.Ordinal);
        Assert.Contains(
            "\"stateRefreshError\":\"Player status could not be refreshed.\"",
            body,
            StringComparison.Ordinal);
        Assert.DoesNotContain("\"isError\":true", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PlayShouldReturnStructuredStateWithAZeroCompletionToolError()
    {
        // Arrange
        var outcome = new PlaybackFailed(
            new LmsPlayerStatus(
                "00:11:22:33:44:55",
                "North Room",
                true,
                PlayerPlaybackState.Stopped),
            2,
            [
                new SkippedMediaItem(
                    1,
                    MediaItemSkipReason.LmsError,
                    "LMS did not confirm the load."),
                new SkippedMediaItem(
                    2,
                    MediaItemSkipReason.NotAttempted,
                    "Not attempted after an earlier LMS failure.")
            ],
            null,
            "Playback failed before any media item completed.");
        await using var playbackFactory = factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IPlaybackService>();
                services.AddSingleton<IPlaybackService>(new StubPlaybackService(outcome));
            }));
        using var client = playbackFactory.CreateClient();
        using var request = CreateRequest(
            "tools/call",
            18,
            """
            {"_meta":{"io.modelcontextprotocol/protocolVersion":"2026-07-28","io.modelcontextprotocol/clientInfo":{"name":"api-tests","version":"0.1.0"},"io.modelcontextprotocol/clientCapabilities":{}},"name":"play","arguments":{"player":"00:11:22:33:44:55","items":["first-reference","second-reference"]}}
            """,
            "play");

        // Act
        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("\"isError\":true", body, StringComparison.Ordinal);
        Assert.Contains("\"structuredContent\"", body, StringComparison.Ordinal);
        Assert.Contains("\"completedItemCount\":0", body, StringComparison.Ordinal);
        Assert.Contains("\"reason\":\"lms_error\"", body, StringComparison.Ordinal);
        Assert.Contains("\"reason\":\"not_attempted\"", body, StringComparison.Ordinal);
        Assert.Contains("\"poweredOn\":true", body, StringComparison.Ordinal);
        Assert.Contains("\"stateRefreshError\":null", body, StringComparison.Ordinal);
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
        Assert.Contains("\"album\":null", body, StringComparison.Ordinal);
        Assert.Contains("\"durationSeconds\":null", body, StringComparison.Ordinal);
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

    [Fact]
    public async Task ManageQueueShouldAcceptItemsAndReturnTheUpdatedLength()
    {
        // Arrange
        var queueManagementService = new StubQueueManagementService();
        await using var queueFactory = factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IQueueManagementService>();
                services.AddSingleton<IQueueManagementService>(queueManagementService);
            }));
        using var client = queueFactory.CreateClient();
        using var request = CreateRequest(
            "tools/call",
            13,
            """
            {"_meta":{"io.modelcontextprotocol/protocolVersion":"2026-07-28","io.modelcontextprotocol/clientInfo":{"name":"api-tests","version":"0.1.0"},"io.modelcontextprotocol/clientCapabilities":{}},"name":"manage_queue","arguments":{"player":"00:11:22:33:44:55","action":"insert_next","items":["first-reference","second-reference"]}}
            """,
            "manage_queue");

        // Act
        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(QueueManagementCommand.InsertNext, queueManagementService.Command);
        Assert.Equal(
            ["first-reference", "second-reference"],
            queueManagementService.References);
        Assert.Contains("\"structuredContent\"", body, StringComparison.Ordinal);
        Assert.Contains("\"player\":\"00:11:22:33:44:55\"", body, StringComparison.Ordinal);
        Assert.Contains("\"queueLength\":9", body, StringComparison.Ordinal);
        Assert.Contains("\"requestedItemCount\":2", body, StringComparison.Ordinal);
        Assert.Contains("\"completedItemCount\":2", body, StringComparison.Ordinal);
        Assert.Contains("\"skippedItems\":[]", body, StringComparison.Ordinal);
        Assert.Contains("\"stateRefreshError\":null", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ManageQueueShouldExposePartialCapacityResults()
    {
        // Arrange
        var outcome = new QueueManagementSucceeded(
            "00:11:22:33:44:55",
            300,
            2,
            1,
            [
                new SkippedMediaItem(
                    2,
                    MediaItemSkipReason.QueueCapacity,
                    "The item does not fit in the remaining queue capacity.")
            ],
            null);
        await using var queueFactory = factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IQueueManagementService>();
                services.AddSingleton<IQueueManagementService>(
                    new StubQueueManagementService(outcome));
            }));
        using var client = queueFactory.CreateClient();
        using var request = CreateRequest(
            "tools/call",
            17,
            """
            {"_meta":{"io.modelcontextprotocol/protocolVersion":"2026-07-28","io.modelcontextprotocol/clientInfo":{"name":"api-tests","version":"0.1.0"},"io.modelcontextprotocol/clientCapabilities":{}},"name":"manage_queue","arguments":{"player":"00:11:22:33:44:55","action":"append","items":["first-reference","second-reference"]}}
            """,
            "manage_queue");

        // Act
        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("\"queueLength\":300", body, StringComparison.Ordinal);
        Assert.Contains("\"requestedItemCount\":2", body, StringComparison.Ordinal);
        Assert.Contains("\"completedItemCount\":1", body, StringComparison.Ordinal);
        Assert.Contains("\"reason\":\"queue_capacity\"", body, StringComparison.Ordinal);
        Assert.Contains("\"stateRefreshError\":null", body, StringComparison.Ordinal);
        Assert.DoesNotContain("\"isError\":true", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ManageQueueShouldReturnStructuredStateWithAZeroCompletionToolError()
    {
        // Arrange
        var outcome = new QueueManagementFailed(
            "00:11:22:33:44:55",
            4,
            1,
            [
                new SkippedMediaItem(
                    1,
                    MediaItemSkipReason.LmsError,
                    "LMS did not confirm the addition.")
            ],
            null,
            "Queue management failed before any media item completed.");
        await using var queueFactory = factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IQueueManagementService>();
                services.AddSingleton<IQueueManagementService>(
                    new StubQueueManagementService(outcome));
            }));
        using var client = queueFactory.CreateClient();
        using var request = CreateRequest(
            "tools/call",
            19,
            """
            {"_meta":{"io.modelcontextprotocol/protocolVersion":"2026-07-28","io.modelcontextprotocol/clientInfo":{"name":"api-tests","version":"0.1.0"},"io.modelcontextprotocol/clientCapabilities":{}},"name":"manage_queue","arguments":{"player":"00:11:22:33:44:55","action":"append","items":["first-reference"]}}
            """,
            "manage_queue");

        // Act
        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("\"isError\":true", body, StringComparison.Ordinal);
        Assert.Contains("\"structuredContent\"", body, StringComparison.Ordinal);
        Assert.Contains("\"queueLength\":4", body, StringComparison.Ordinal);
        Assert.Contains("\"completedItemCount\":0", body, StringComparison.Ordinal);
        Assert.Contains("\"reason\":\"lms_error\"", body, StringComparison.Ordinal);
        Assert.Contains("\"stateRefreshError\":null", body, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("\"bogus\"")]
    [InlineData("null")]
    [InlineData("17")]
    [InlineData("{}")]
    public async Task ManageQueueShouldReturnACorrectiveToolErrorForAnInvalidAction(
        string actionJson)
    {
        // Arrange
        var queueManagementService = new StubQueueManagementService();
        await using var queueFactory = factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IQueueManagementService>();
                services.AddSingleton<IQueueManagementService>(queueManagementService);
            }));
        using var client = queueFactory.CreateClient();
        using var request = CreateRequest(
            "tools/call",
            14,
            """
            {"_meta":{"io.modelcontextprotocol/protocolVersion":"2026-07-28","io.modelcontextprotocol/clientInfo":{"name":"api-tests","version":"0.1.0"},"io.modelcontextprotocol/clientCapabilities":{}},"name":"manage_queue","arguments":{"player":"00:11:22:33:44:55","action":ACTION_JSON}}
            """.Replace("ACTION_JSON", actionJson, StringComparison.Ordinal),
            "manage_queue");

        // Act
        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("\"isError\":true", body, StringComparison.Ordinal);
        Assert.Contains("The queue management action is invalid.", body, StringComparison.Ordinal);
        Assert.DoesNotContain("An error occurred invoking", body, StringComparison.Ordinal);
        Assert.DoesNotContain("\"error\":", body, StringComparison.Ordinal);
        Assert.Null(queueManagementService.Command);
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

    private static JsonDocument ParseJsonRpcResponse(string body)
    {
        var data = body.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Single(line => line.StartsWith("data:", StringComparison.Ordinal));
        return JsonDocument.Parse(data["data:".Length..].Trim());
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
                    null),
                new(
                    "album-reference",
                    MediaEntityKind.Album,
                    "Copper Signals",
                    "The Copper Lines",
                    null),
                new(
                    "rated-reference",
                    MediaEntityKind.Track,
                    "Ninety Point Signal",
                    "The Copper Lines",
                    "Copper Signals",
                    90),
                new(
                    "odd-reference",
                    MediaEntityKind.Track,
                    "Odd Point Signal",
                    "The Copper Lines",
                    "Copper Signals",
                    67),
                new(
                    "missing-reference",
                    MediaEntityKind.Track,
                    "Missing Rating Signal",
                    "The Copper Lines",
                    "Copper Signals"),
                new(
                    "zero-reference",
                    MediaEntityKind.Track,
                    "Zero Rating Signal",
                    "The Copper Lines",
                    "Copper Signals",
                    0),
                new(
                    "playlist-reference",
                    MediaEntityKind.Playlist,
                    "Copper Evenings",
                    null,
                    null)
            ],
            [
                new(
                    "top-track-reference",
                    MediaEntityKind.Track,
                    "Copper Favourite",
                    "The Copper Lines",
                    "Copper Signals",
                    100)
            ]));
        }
    }

    private sealed class CapturingSearchService : ISearchService
    {
        public SearchCriteria? Criteria { get; private set; }

        public Task<SearchOutcome> SearchAsync(
            string query,
            CancellationToken cancellationToken) =>
            SearchAsync(new SearchCriteria(query), cancellationToken);

        public Task<SearchOutcome> SearchAsync(
            SearchCriteria criteria,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Criteria = criteria;
            return Task.FromResult<SearchOutcome>(new SearchSucceeded([], []));
        }
    }

    private sealed class ThrowingSearchService : ISearchService
    {
        public Task<SearchOutcome> SearchAsync(
            string query,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw new InvalidOperationException("Deliberate unexpected search failure.");
        }
    }

    private sealed class StubBrowseService(BrowseOutcome? outcome = null) : IBrowseService
    {
        public Task<BrowseOutcome> BrowseAsync(
            string? reference,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (outcome is not null)
            {
                Assert.Equal("invalid", reference);
                return Task.FromResult(outcome);
            }

            Assert.Null(reference);
            return Task.FromResult<BrowseOutcome>(new BrowseSucceeded(
            [
                new BrowseItemResult(
                    "browse-album-artists",
                    BrowseItemKind.AlbumArtist,
                    "Album artists",
                    null,
                    null,
                    true,
                    false),
                new BrowseItemResult(
                    "rated-track",
                    BrowseItemKind.Track,
                    "Ninety Point Signal",
                    "The Imaginaries",
                    "Imaginary Signals",
                    false,
                    true,
                    90)
            ],
            "browse-next"));
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
                        12.5)),
                new(
                    "66:77:88:99:aa:bb",
                    "Quiet Room",
                    true,
                    PlayerPlaybackState.Stopped,
                    null,
                    null,
                    new LmsNowPlaying(
                        "Test Tone",
                        null,
                        null,
                        null,
                        null))
            ];
            return Task.FromResult(players);
        }
    }

    private sealed class StubPlaybackService(PlaybackOutcome? outcome = null)
        : IPlaybackService
    {
        public IReadOnlyList<string>? References { get; private set; }

        public Task<PlaybackOutcome> PlayAsync(
            string playerId,
            IReadOnlyList<string> references,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Assert.Equal("00:11:22:33:44:55", playerId);
            References = references;
            return Task.FromResult(outcome ?? new PlaybackSucceeded(
                new LmsPlayerStatus(
                    playerId,
                    "North Room",
                    true,
                    PlayerPlaybackState.Playing),
                references.Count,
                references.Count,
                [],
                null));
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

    private sealed class StubQueueManagementService(
        QueueManagementOutcome? outcome = null) : IQueueManagementService
    {
        public QueueManagementCommand? Command { get; private set; }

        public IReadOnlyList<string>? References { get; private set; }

        public Task<QueueManagementOutcome> ManageAsync(
            string playerId,
            QueueManagementCommand command,
            IReadOnlyList<string>? references,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Assert.Equal("00:11:22:33:44:55", playerId);
            Command = command;
            References = references;
            return Task.FromResult<QueueManagementOutcome>(outcome
                ?? new QueueManagementSucceeded(
                    playerId,
                    9,
                    references?.Count ?? 0,
                    references?.Count ?? 0,
                    [],
                    null));
        }
    }
}
