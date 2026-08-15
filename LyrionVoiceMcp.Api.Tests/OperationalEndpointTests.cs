using System.Net;
using System.Net.Http.Json;
using LyrionVoiceMcp.Abstractions;
using LyrionVoiceMcp.Contracts;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace LyrionVoiceMcp.Api.Tests;

public sealed class OperationalEndpointTests : IClassFixture<LyrionVoiceMcpApiFactory>
{
    private readonly LyrionVoiceMcpApiFactory factory;

    public OperationalEndpointTests(LyrionVoiceMcpApiFactory factory)
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

    [Fact]
    public async Task SearchObservationBrowseShouldExposeRetentionWithoutChangingMcpSurface()
    {
        // Arrange
        using var client = factory.CreateClient();

        // Act
        var response = await client.GetFromJsonAsync<SearchObservationPageResponse>(
            "/api/search-observations?review=unreviewed&result=no-results",
            TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(response);
        Assert.Equal(90, response.RetentionDays);
        Assert.NotNull(response.Items);
    }

    [Fact]
    public async Task CatalogueShouldExposePublishedGenerationAndLatestRefresh()
    {
        // Arrange
        var status = CreateCatalogueStatus(CatalogueRefreshRunStatus.Succeeded);
        using var configuredFactory = factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<ICatalogueRefreshService>();
                services.AddSingleton<ICatalogueRefreshService>(new StubCatalogueRefreshService(status));
            }));
        using var client = configuredFactory.CreateClient();

        // Act
        var response = await client.GetFromJsonAsync<CatalogueStatusResponse>(
            "/api/catalogue",
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("generation-1", response?.PublishedGeneration?.Id);
        Assert.Equal(33_687, response?.PublishedGeneration?.TrackCount);
        Assert.Equal("succeeded", response?.LatestRefresh?.Status);
        Assert.Equal("generation-1", response?.LatestRefresh?.PublishedGenerationId);
    }

    [Fact]
    public async Task CatalogueRefreshShouldReportConflictWhenARefreshIsAlreadyRunning()
    {
        // Arrange
        var status = CreateCatalogueStatus(CatalogueRefreshRunStatus.Running);
        using var configuredFactory = factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<ICatalogueRefreshService>();
                services.AddSingleton<ICatalogueRefreshService>(
                    new StubCatalogueRefreshService(
                        status,
                        new CatalogueRefreshAlreadyRunning(status)));
            }));
        using var client = configuredFactory.CreateClient();

        // Act
        var response = await client.PostAsync(
            "/api/catalogue/refresh",
            null,
            TestContext.Current.CancellationToken);
        var body = await response.Content.ReadFromJsonAsync<CatalogueStatusResponse>(
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("running", body?.LatestRefresh?.Status);
    }

    [Fact]
    public async Task CatalogueRefreshShouldQueueBackgroundWorkAndReturnStatusLocation()
    {
        // Arrange
        var status = CreateCatalogueStatus(CatalogueRefreshRunStatus.Running);
        using var configuredFactory = factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<ICatalogueRefreshService>();
                services.AddSingleton<ICatalogueRefreshService>(
                    new StubCatalogueRefreshService(
                        status,
                        new CatalogueRefreshStarted(status)));
            }));
        using var client = configuredFactory.CreateClient();

        // Act
        var response = await client.PostAsync(
            "/api/catalogue/refresh",
            null,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal("/api/catalogue", response.Headers.Location?.OriginalString);
    }

    private static CatalogueStatus CreateCatalogueStatus(CatalogueRefreshRunStatus status)
    {
        var published = new PublishedCatalogueGeneration(
            "generation-1",
            "development",
            "revision-1",
            "9.1.2",
            DateTimeOffset.Parse("2026-08-15T09:59:58Z"),
            DateTimeOffset.Parse("2026-08-15T09:55:00Z"),
            DateTimeOffset.Parse("2026-08-15T10:00:12Z"),
            6_530,
            3_003,
            128,
            33_687,
            6,
            0);
        return new CatalogueStatus(
            published,
            new CatalogueRefreshRun(
                "refresh-1",
                status,
                DateTimeOffset.Parse("2026-08-15T10:00:00Z"),
                status == CatalogueRefreshRunStatus.Running
                    ? null
                    : DateTimeOffset.Parse("2026-08-15T10:00:12Z"),
                status == CatalogueRefreshRunStatus.Running ? null : 12_000,
                status == CatalogueRefreshRunStatus.Succeeded ? published.Id : null,
                null));
    }

    private sealed class StubCatalogueRefreshService(
        CatalogueStatus status,
        CatalogueRefreshOutcome? outcome = null) : ICatalogueRefreshService
    {
        public Task<CatalogueStatus> GetStatusAsync(CancellationToken cancellationToken) =>
            Task.FromResult(status);

        public Task<CatalogueRefreshOutcome> RefreshAsync(CancellationToken cancellationToken) =>
            Task.FromResult(outcome ?? new CatalogueRefreshStarted(status));
    }
}
