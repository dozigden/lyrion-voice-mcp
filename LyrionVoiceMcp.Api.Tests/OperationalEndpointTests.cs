using System.Net;
using System.Net.Http.Json;
using LyrionVoiceMcp.Contracts;
using Microsoft.AspNetCore.Mvc.Testing;

namespace LyrionVoiceMcp.Api.Tests;

public sealed class OperationalEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> factory;

    public OperationalEndpointTests(WebApplicationFactory<Program> factory)
    {
        this.factory = factory;
    }

    [Fact]
    public async Task HealthShouldReportOk()
    {
        // Arrange
        using var client = factory.CreateClient();

        // Act
        var response = await client.GetAsync("/api/health", TestContext.Current.CancellationToken);
        var result = await response.Content.ReadFromJsonAsync<HealthResponse>(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(result);
        Assert.Equal("ok", result.Status);
    }

    [Fact]
    public async Task VersionShouldExposeConfiguredBuildMetadata()
    {
        // Arrange
        using var configuredFactory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("LyrionVoiceMcpBuild:Version", "9.8.7");
            builder.UseSetting("LyrionVoiceMcpBuild:Channel", "test");
            builder.UseSetting("LyrionVoiceMcpBuild:Build", "test-42");
            builder.UseSetting("LyrionVoiceMcpBuild:Commit", "abcdef0");
        });
        using var client = configuredFactory.CreateClient();

        // Act
        var result = await client.GetFromJsonAsync<VersionResponse>(
            "/api/version",
            TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("9.8.7", result.Version);
        Assert.Equal("test", result.Channel);
        Assert.Equal("test-42", result.Build);
        Assert.Equal("abcdef0", result.Commit);
    }

    [Fact]
    public async Task LmsShouldReportAnUnconfiguredRuntime()
    {
        // Arrange
        using var client = factory.CreateClient();

        // Act
        var result = await client.GetFromJsonAsync<LmsConnectionResponse>(
            "/api/lms",
            TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("not_configured", result.Status);
        Assert.Null(result.ServerId);
        Assert.Null(result.BaseUrl);
    }

    [Theory]
    [InlineData("/api/not-real")]
    [InlineData("/mcp/not-real")]
    public async Task ReservedRoutesShouldNotFallBackToSpa(string path)
    {
        // Arrange
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        // Act
        var response = await client.GetAsync(path, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
