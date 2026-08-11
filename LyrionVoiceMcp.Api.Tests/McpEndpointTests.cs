using System.Net;
using System.Text;
using Microsoft.AspNetCore.Mvc.Testing;

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
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Lyrion Voice MCP", body, StringComparison.Ordinal);
        Assert.Contains("\"tools\"", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ListToolsShouldReturnAnEmptyCollection()
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
        Assert.Contains("\"tools\":[]", body, StringComparison.Ordinal);
    }

    private static HttpRequestMessage CreateRequest(string method, int id, string parameters)
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
        return request;
    }
}
