using System.Text.Json;
using LyrionVoiceMcp.Abstractions;
using LyrionVoiceMcp.Persistence;
using LyrionVoiceMcp.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace LyrionVoiceMcp.Services.Tests;

public sealed class JobArchitectureTests : IDisposable
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-08-16T04:00:00Z");
    private readonly string directory = Path.Combine(
        Path.GetTempPath(),
        $"lyrion-job-architecture-{Guid.NewGuid():N}");
    private readonly FixedTimeProvider timeProvider = new(Now);
    private readonly OperationalPolicy policy = new(90, 90, 30, 4096, TimeZoneInfo.Utc);
    private readonly SqliteOperationalStore store;

    public JobArchitectureTests()
    {
        store = new SqliteOperationalStore(
            new OperationalSettings(
                Path.Combine(directory, "operations.db"),
                90,
                90,
                30,
                4096,
                "UTC"),
            timeProvider);
        store.InitialiseAsync(CancellationToken.None).GetAwaiter().GetResult();
    }

    [Fact]
    public async Task RunningJobShouldBeCancellableThroughTheDurableJobService()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new DelegatingHandler("test.wait", async (_, cancellationToken) =>
        {
            entered.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return JobHandlerResult.Succeeded();
        });
        var cancellationRegistry = new JobCancellationRegistry();
        var lifecycleGate = new JobLifecycleGate();
        var service = CreateJobService(cancellationRegistry, lifecycleGate);
        var enqueued = Assert.IsType<JobEnqueued>(await service.EnqueueAsync(
            new CreateJob(handler.Type, "{}", Now, null),
            TestContext.Current.CancellationToken));
        var runner = CreateRunner(handler, cancellationRegistry, lifecycleGate);

        var run = runner.RunNextDueAsync(TestContext.Current.CancellationToken);
        await entered.Task.WaitAsync(
            TimeSpan.FromSeconds(5),
            TestContext.Current.CancellationToken);
        var cancellation = await service.RequestCancellationAsync(
            enqueued.Job.Id,
            TestContext.Current.CancellationToken);
        await run;

        Assert.IsType<JobCancellationAccepted>(cancellation);
        var details = await store.GetAsync(
            enqueued.Job.Id,
            TestContext.Current.CancellationToken);
        Assert.Equal(JobStatus.Cancelled, details?.Job.Status);
        Assert.Contains(details!.Logs, log => log.Message == "Job cancellation requested.");
        Assert.Contains(details.Logs, log => log.Message == "Job cancelled.");
    }

    [Fact]
    public async Task UnexpectedJobFailureShouldLinkTheFailedJobToTheErrorLog()
    {
        var handler = new DelegatingHandler(
            "test.failure",
            (_, _) => throw new InvalidOperationException("Deliberate job failure."));
        var cancellationRegistry = new JobCancellationRegistry();
        var lifecycleGate = new JobLifecycleGate();
        var service = CreateJobService(cancellationRegistry, lifecycleGate);
        var enqueued = Assert.IsType<JobEnqueued>(await service.EnqueueAsync(
            new CreateJob(handler.Type, "{}", Now, null),
            TestContext.Current.CancellationToken));
        var runner = CreateRunner(handler, cancellationRegistry, lifecycleGate);

        Assert.True(await runner.RunNextDueAsync(TestContext.Current.CancellationToken));

        var details = await store.GetAsync(
            enqueued.Job.Id,
            TestContext.Current.CancellationToken);
        var errors = await store.BrowseAsync(
            new ErrorLogQuery(Area: ErrorLogAreas.JobRunner),
            TestContext.Current.CancellationToken);
        var error = Assert.Single(errors.Items);
        Assert.Equal(JobStatus.Failed, details?.Job.Status);
        Assert.Equal(enqueued.Job.Id, error.JobId);
        Assert.Contains("Deliberate job failure.", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StartupRecoveryShouldFailAndLogEveryAbandonedRunningJob()
    {
        var job = await store.CreateAsync(
            new CreateJob("test.interrupted", "{}", Now, null),
            Now,
            TestContext.Current.CancellationToken);
        await store.TryStartNextDueAsync(Now, TestContext.Current.CancellationToken);
        var runner = CreateRunner(
            new DelegatingHandler("test.interrupted", (_, _) =>
                Task.FromResult(JobHandlerResult.Succeeded())),
            new JobCancellationRegistry(),
            new JobLifecycleGate());

        await runner.MarkRunningJobsFailedAsync(TestContext.Current.CancellationToken);

        var details = await store.GetAsync(job.Id, TestContext.Current.CancellationToken);
        Assert.Equal(JobStatus.Failed, details?.Job.Status);
        Assert.Equal("Job was interrupted by server startup.", details?.Job.ErrorMessage);
        Assert.Contains(details!.Logs, log => log.Message == "Job interrupted by server startup.");
    }

    [Fact]
    public async Task MissingHandlerShouldFailTheClaimedJobSafely()
    {
        var service = CreateJobService(new JobCancellationRegistry(), new JobLifecycleGate());
        var enqueued = Assert.IsType<JobEnqueued>(await service.EnqueueAsync(
            new CreateJob("test.missing", "{}", Now, null),
            TestContext.Current.CancellationToken));
        var runner = new JobRunner(
            store,
            [],
            new JobCancellationRegistry(),
            new JobLifecycleGate(),
            new ErrorLogService(
                store,
                policy,
                timeProvider,
                NullLogger<ErrorLogService>.Instance),
            timeProvider);

        Assert.True(await runner.RunNextDueAsync(TestContext.Current.CancellationToken));

        var details = await store.GetAsync(
            enqueued.Job.Id,
            TestContext.Current.CancellationToken);
        Assert.Equal(JobStatus.Failed, details?.Job.Status);
        Assert.Contains("No job handler is registered", details?.Job.ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DueScheduleShouldCreateOneDurableCorrelatedOccurrence()
    {
        var lifecycleGate = new JobLifecycleGate();
        var service = CreateJobService(new JobCancellationRegistry(), lifecycleGate);
        var schedule = new TestSchedule();
        var scheduledJobs = new ScheduledJobService(
            store,
            service,
            [schedule],
            new CronOccurrenceCalculator(),
            policy,
            timeProvider);
        await store.UpsertScheduledJobStateAsync(
            new ScheduledJobState(schedule.SchedulerStateName, Now.AddDays(-1), Now.AddDays(-1)),
            TestContext.Current.CancellationToken);

        var first = await scheduledJobs.EnqueueDueJobsAsync(
            TestContext.Current.CancellationToken);
        var second = await scheduledJobs.EnqueueDueJobsAsync(
            TestContext.Current.CancellationToken);
        var jobs = await store.BrowseAsync(
            new JobQuery(Type: "test.scheduled"),
            TestContext.Current.CancellationToken);

        var occurrence = Assert.Single(first);
        Assert.True(occurrence.Enqueued);
        Assert.Equal("scheduled:test-schedule:20260816T030000.0000000Z", occurrence.CorrelationId);
        Assert.Empty(second);
        Assert.Single(jobs.Items);
    }

    [Fact]
    public async Task ErrorLogShouldRetainBoundedDiagnosticTextAndStructuredContext()
    {
        var service = new ErrorLogService(
            store,
            policy,
            timeProvider,
            NullLogger<ErrorLogService>.Instance);

        var errorId = await service.LogExceptionAsync(
            new InvalidOperationException("Request failed token=message-secret"),
            new ErrorLogContext(
                ErrorLogSources.Backend,
                ErrorLogAreas.ApiRequest,
                ContextJson: """{"token":"context-secret","nested":{"authorization":"Bearer nested-secret"},"safe":"retained"}"""),
            TestContext.Current.CancellationToken);

        var error = await store.GetErrorLogAsync(
            errorId!.Value,
            TestContext.Current.CancellationToken);
        Assert.Contains("message-secret", error!.Message, StringComparison.Ordinal);
        Assert.Contains("context-secret", error.ContextJson, StringComparison.Ordinal);
        Assert.Contains("nested-secret", error.ContextJson, StringComparison.Ordinal);
        Assert.Contains("retained", error.ContextJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SuccessfulCatalogueShouldEnqueueOneProductionSearchJob()
    {
        // Arrange
        var catalogue = new TestCatalogueStore(CreateCatalogueState("job-42"));
        var builder = new RecordingIndexBuilder();
        var lifecycleGate = new JobLifecycleGate();
        var service = new SearchIndexService(
            builder,
            catalogue,
            store,
            CreateJobService(new JobCancellationRegistry(), lifecycleGate),
            lifecycleGate,
            timeProvider);

        var status = await service.GetAsync(TestContext.Current.CancellationToken);

        // Act
        var jobId = await service.EnqueueForCatalogueAsync(
            "job-42",
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(builder.Descriptor.Name, status.Resolver);
        Assert.NotNull(jobId);
        var jobs = await store.BrowseAsync(
            new JobQuery(Type: JobTypes.SearchIndexRebuild),
            TestContext.Current.CancellationToken);
        Assert.Equal(1, jobs.Total);
        var job = (await store.GetAsync(jobId.Value, TestContext.Current.CancellationToken))!.Job;
        var payload = JsonSerializer.Deserialize<SearchIndexRebuildPayload>(job.PayloadJson);
        Assert.NotNull(payload);
        Assert.Equal("job-42", payload.CatalogueRefreshId);
        Assert.Equal("search-index:production:catalogue:job-42", job.CorrelationId);
    }

    [Fact]
    public async Task SearchIndexHandlerShouldBuildAgainstTheMatchingSuccessfulCatalogue()
    {
        // Arrange
        var builder = new RecordingIndexBuilder();
        var logs = new RecordingLogWriter();
        var handler = new SearchIndexRebuildJobHandler(
            new TestCatalogueStore(CreateCatalogueState("job-42")),
            builder,
            logs);
        var payload = JsonSerializer.Serialize(new SearchIndexRebuildPayload("job-42"));

        // Act
        var result = await handler.HandleAsync(
            new JobContext(101, JobTypes.SearchIndexRebuild, payload),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.Success);
        Assert.Equal("job-42", builder.RebuiltCatalogueRefreshId);
        Assert.Equal(101, builder.RebuiltJobId);
        Assert.Contains(logs.Messages, message => message == "Search-index rebuild started.");
        Assert.Contains(logs.Messages, message => message == "Search-index rebuild completed.");
    }

    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private JobService CreateJobService(
        IJobCancellationRegistry cancellationRegistry,
        IJobLifecycleGate lifecycleGate) =>
        new(store, cancellationRegistry, lifecycleGate, policy, timeProvider);

    private JobRunner CreateRunner(
        IJobHandler handler,
        IJobCancellationRegistry cancellationRegistry,
        IJobLifecycleGate lifecycleGate) =>
        new(
            store,
            [handler],
            cancellationRegistry,
            lifecycleGate,
            new ErrorLogService(
                store,
                policy,
                timeProvider,
                NullLogger<ErrorLogService>.Instance),
            timeProvider);

    private sealed class DelegatingHandler(
        string type,
        Func<JobContext, CancellationToken, Task<JobHandlerResult>> action) : IJobHandler
    {
        public string Type => type;

        public Task<JobHandlerResult> HandleAsync(
            JobContext context,
            CancellationToken cancellationToken) => action(context, cancellationToken);
    }

    private sealed class TestSchedule : IScheduledJobDefinition
    {
        public string Name => "test-schedule";
        public string DisplayName => "Test schedule";
        public string SchedulerStateName => "schedule:test-schedule";

        public Task<ScheduledJobConfiguration> GetConfigurationAsync(
            CancellationToken cancellationToken) => Task.FromResult(
            new ScheduledJobConfiguration(true, "0 3 * * *"));

        public Task<IReadOnlyList<ScheduledJobOccurrence>> CreateOccurrencesAsync(
            DateTimeOffset dueAt,
            CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ScheduledJobOccurrence>>(
            [new(
                "test.scheduled",
                dueAt,
                "{}",
                $"scheduled:{Name}:{dueAt.UtcDateTime:yyyyMMdd'T'HHmmss.fffffff'Z'}")]);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static CatalogueState CreateCatalogueState(string refreshId)
    {
        var summary = new CatalogueSummary(
            "development",
            "lms",
            "revision-1",
            "9.1.2",
            Now,
            null,
            Now,
            10,
            20,
            3,
            100,
            2,
            0);
        return new CatalogueState(
            refreshId,
            CatalogueStateStatus.Succeeded,
            Now,
            Now,
            summary);
    }

    private sealed class RecordingIndexBuilder : ISearchIndexBuilder
    {
        public SearchResolverDescriptor Descriptor { get; } = new(
            "fictional-index",
            "3");

        public string? RebuiltCatalogueRefreshId { get; private set; }
        public long? RebuiltJobId { get; private set; }

        public Task<SearchIndexArtifact?> GetArtifactAsync(CancellationToken cancellationToken) =>
            Task.FromResult<SearchIndexArtifact?>(null);

        public async Task<SearchIndexRebuildResult> RebuildAsync(
            string catalogueRefreshId,
            long jobId,
            ISearchIndexProgress progress,
            CancellationToken cancellationToken)
        {
            RebuiltCatalogueRefreshId = catalogueRefreshId;
            RebuiltJobId = jobId;
            await progress.ReportAsync("Building fictional index.", null, cancellationToken);
            return new SearchIndexRebuildResult(new SearchIndexArtifact(
                Descriptor.Name,
                Descriptor.Version,
                catalogueRefreshId,
                Now,
                100,
                50,
                1_024));
        }
    }

    private sealed class RecordingLogWriter : IJobLogWriter
    {
        public List<string> Messages { get; } = [];

        public Task WriteAsync(
            long jobId,
            JobLogLevel level,
            string message,
            object? data,
            CancellationToken cancellationToken)
        {
            Messages.Add(message);
            return Task.CompletedTask;
        }
    }

    private sealed class TestCatalogueStore(CatalogueState state) : IMediaCatalogueStore
    {
        public Task InitialiseAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<CatalogueState?> GetStateAsync(CancellationToken cancellationToken) =>
            Task.FromResult<CatalogueState?>(state);
        public Task<CatalogueSummary?> GetSummaryAsync(CancellationToken cancellationToken) =>
            Task.FromResult(state.Summary);
        public Task BeginRefreshAsync(string refreshId, DateTimeOffset startedAt, CancellationToken cancellationToken) =>
            Task.CompletedTask;
        public Task<CatalogueRefreshCompletion> CompleteRefreshAsync(string refreshId, CatalogueSourceReadResult source, DateTimeOffset completedAt, int existingWarningCount, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task FinishRefreshAsync(string refreshId, CatalogueStateStatus status, DateTimeOffset completedAt, CancellationToken cancellationToken) =>
            Task.CompletedTask;
        public Task WriteAlbumsAsync(string refreshId, IReadOnlyList<CatalogueImportAlbum> albums, CancellationToken cancellationToken) =>
            Task.CompletedTask;
        public Task WriteGenresAsync(string refreshId, IReadOnlyList<CatalogueImportGenre> genres, CancellationToken cancellationToken) =>
            Task.CompletedTask;
        public Task WriteTracksAsync(string refreshId, IReadOnlyList<CatalogueImportTrack> tracks, CancellationToken cancellationToken) =>
            Task.CompletedTask;
        public Task WriteArtistsAsync(string refreshId, IReadOnlyList<CatalogueImportArtist> artists, CancellationToken cancellationToken) =>
            Task.CompletedTask;
        public Task WriteVirtualLibrariesAsync(string refreshId, IReadOnlyList<CatalogueImportVirtualLibrary> libraries, CancellationToken cancellationToken) =>
            Task.CompletedTask;
        public Task WriteVirtualLibraryTracksAsync(string refreshId, string librarySourceId, IReadOnlyList<string> trackSourceIds, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }
}
