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
        Assert.True(File.Exists(factory.ApplicationDatabasePath));
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
    public async Task SearchObservationDetailShouldExposeBroadDiscoveryInterpretation()
    {
        using var isolatedFactory = new LyrionVoiceMcpApiFactory();
        using var client = isolatedFactory.CreateClient();
        using (var scope = isolatedFactory.Services.CreateScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<ISearchObservationStore>();
            await store.RecordAsync(
                new SearchObservation(
                    "broad-observation",
                    DateTimeOffset.Parse("2026-08-25T20:00:00Z"),
                    string.Empty,
                    string.Empty,
                    MediaEntityKind.Track,
                    "catalogue",
                    "whole_library",
                    "fictional-resolver",
                    "1",
                    SearchObservationStatus.Completed,
                    null,
                    12,
                    10,
                    2,
                    [],
                    [],
                    null,
                    Interpretation: SearchObservationInterpretation.BroadDiscovery),
                TestContext.Current.CancellationToken);
        }

        var response = await client.GetFromJsonAsync<SearchObservationDetailResponse>(
            "/api/search-observations/broad-observation",
            TestContext.Current.CancellationToken);
        var page = await client.GetFromJsonAsync<SearchObservationPageResponse>(
            "/api/search-observations",
            TestContext.Current.CancellationToken);

        Assert.Equal("broad_discovery", response?.Interpretation);
        Assert.Equal(string.Empty, response?.OriginalQuery);
        Assert.Equal(
            "broad_discovery",
            Assert.Single(page!.Items, item => item.Id == "broad-observation").Interpretation);
    }

    [Fact]
    public async Task ScheduledJobsShouldExposeDisabledCatalogueAndEnabledMaintenanceDefinitions()
    {
        using var client = factory.CreateClient();

        var schedules = await client.GetFromJsonAsync<ScheduledJobResponse[]>(
            "/api/scheduled-jobs",
            TestContext.Current.CancellationToken);

        Assert.Equal(4, schedules?.Length);
        Assert.False(Assert.Single(schedules!, item => item.Name == "catalogue-refresh").Enabled);
        Assert.True(Assert.Single(schedules!, item => item.Name == "error-log-purge").Enabled);
    }

    [Fact]
    public async Task RunNowShouldCreateAnInspectableDurableJobEvenWhenScheduleIsDisabled()
    {
        using var client = factory.CreateClient();

        var response = await client.PostAsync(
            "/api/scheduled-jobs/catalogue-refresh/run",
            null,
            TestContext.Current.CancellationToken);
        var result = await response.Content.ReadFromJsonAsync<ScheduledJobRunNowResponse>(
            TestContext.Current.CancellationToken);
        var details = await client.GetFromJsonAsync<JobDetailsResponse>(
            $"/api/jobs/{Assert.Single(result!.JobIds)}",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal(JobTypes.CatalogueRefresh, details?.Job.Type);
        Assert.NotEmpty(details!.Logs);
    }

    [Fact]
    public async Task UnexpectedApiFailureShouldReturnAResolvableDurableErrorReference()
    {
        await using var failingFactory = factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IJobService>();
                services.AddSingleton<IJobService>(new ThrowingJobService());
            }));
        using var client = failingFactory.CreateClient();

        var response = await client.GetAsync(
            "/api/jobs?type=force-middleware-failure",
            TestContext.Current.CancellationToken);
        var responseBody = await response.Content.ReadFromJsonAsync<ApiErrorResponse>(
            TestContext.Current.CancellationToken);
        var errors = await client.GetFromJsonAsync<ErrorLogPageResponse>(
            $"/api/error-logs?source={ErrorLogSources.Backend}&area={ErrorLogAreas.ApiRequest}",
            TestContext.Current.CancellationToken);
        var summary = Assert.Single(errors!.Items, item =>
            item.Message.Contains("Deliberate API failure.", StringComparison.Ordinal));
        var error = await client.GetFromJsonAsync<ErrorLogResponse>(
            $"/api/error-logs/{summary.Id}",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Contains(error!.Id.ToString(), responseBody!.Message, StringComparison.Ordinal);
        Assert.Equal("GET", error.RequestMethod);
        Assert.Equal("/api/jobs", error.RequestPath);
        Assert.NotNull(error.TraceIdentifier);
    }

    [Fact]
    public async Task CatalogueShouldExposeSummaryAndLatestRefreshJob()
    {
        // Arrange
        var status = CreateCatalogueStatus(JobStatus.Completed);
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
        Assert.Equal("development", response?.Summary?.SourceId);
        Assert.Equal(6_530, response?.Summary?.ArtistCount);
        Assert.Equal(33_687, response?.Summary?.TrackCount);
        Assert.Equal("succeeded", response?.LatestRefresh?.Status);
        Assert.Empty(response!.LatestRefresh!.Logs);
    }

    [Fact]
    public async Task CatalogueRefreshShouldReportConflictWhenARefreshIsAlreadyRunning()
    {
        // Arrange
        var status = CreateCatalogueStatus(JobStatus.Running);
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
        var status = CreateCatalogueStatus(JobStatus.Running);
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

    private static CatalogueStatus CreateCatalogueStatus(JobStatus status)
    {
        var summary = new CatalogueSummary(
            "development",
            "lms",
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
            summary,
            new Job(
                1,
                JobTypes.CatalogueRefresh,
                status,
                DateTimeOffset.Parse("2026-08-15T10:00:00Z"),
                "{}",
                "{}",
                null,
                DateTimeOffset.Parse("2026-08-15T10:00:00Z"),
                status == JobStatus.Running
                    ? null
                    : DateTimeOffset.Parse("2026-08-15T10:00:12Z"),
                "manual:catalogue.refresh:test",
                DateTimeOffset.Parse("2026-08-15T10:00:00Z"),
                DateTimeOffset.Parse("2026-08-15T10:00:12Z")));
    }

    private sealed class StubCatalogueRefreshService(
        CatalogueStatus status,
        CatalogueRefreshOutcome? outcome = null) : ICatalogueRefreshService
    {
        public Task<CatalogueStatus> GetStatusAsync(CancellationToken cancellationToken) =>
            Task.FromResult(status);

        public Task<CatalogueRefreshOutcome> RefreshAsync(CancellationToken cancellationToken) =>
            Task.FromResult(outcome ?? new CatalogueRefreshStarted(status));

        public Task<CatalogueRefreshOutcome> RefreshOnStartupAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult(outcome ?? new CatalogueRefreshStarted(status));
    }

    private sealed class ThrowingJobService : IJobService
    {
        public int RetentionDays => 90;

        public Task<JobPage> BrowseAsync(JobQuery query, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Deliberate API failure.");

        public Task<JobDetails?> GetAsync(long id, CancellationToken cancellationToken) =>
            Task.FromResult<JobDetails?>(null);

        public Task<JobEnqueueOutcome> EnqueueAsync(
            CreateJob request,
            CancellationToken cancellationToken) =>
            Task.FromResult<JobEnqueueOutcome>(new JobEnqueueRejected("Not used."));

        public Task<JobCancellationOutcome> RequestCancellationAsync(
            long id,
            CancellationToken cancellationToken) =>
            Task.FromResult<JobCancellationOutcome>(new JobCancellationRejected("Not used."));
    }
}
