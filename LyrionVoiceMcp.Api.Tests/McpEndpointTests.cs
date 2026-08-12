using System.Net;
using System.Text;
using LyrionVoiceMcp.Abstractions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace LyrionVoiceMcp.Api.Tests;

public sealed class McpEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string ProtocolVersion = "2026-07-28";
    private readonly WebApplicationFactory<Program> factory;

    public McpEndpointTests(WebApplicationFactory<Program> factory)
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
    public async Task ListToolsShouldAdvertiseOnlySearchWithRequiredQuery()
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
        Assert.DoesNotContain("get_player_status", body, StringComparison.Ordinal);
        Assert.DoesNotContain("\"name\":\"play\"", body, StringComparison.Ordinal);
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

    private sealed class StubSearchService : ISearchService
    {
        public Task<IReadOnlyList<SearchCandidateResult>> SearchAsync(
            string query,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Assert.Equal("copper lines", query);
            IReadOnlyList<SearchCandidateResult> results =
            [
                new(
                    "opaque-reference",
                    MediaEntityKind.Artist,
                    "The Copper Lines",
                    null,
                    null)
            ];
            return Task.FromResult(results);
        }
    }
}
