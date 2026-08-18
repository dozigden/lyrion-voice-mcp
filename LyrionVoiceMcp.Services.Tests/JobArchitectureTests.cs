using System.Text.Json;
using LyrionVoiceMcp.Abstractions;
using LyrionVoiceMcp.Ef;
using LyrionVoiceMcp.Ef.Abstractions.DataAccess;
using LyrionVoiceMcp.Ef.Abstractions.Entities;
using LyrionVoiceMcp.Ef.Abstractions.ErrorLogs;
using LyrionVoiceMcp.Ef.Abstractions.Jobs;
using LyrionVoiceMcp.Services;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
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
    private readonly ServiceProvider serviceProvider;
    private readonly IDbContextScopeFactory scopeFactory;
    private readonly IJobRepository jobRepository;
    private readonly IJobLogRepository jobLogRepository;
    private readonly IScheduledJobStateRepository stateRepository;
    private readonly IErrorLogRepository errorLogRepository;

    public JobArchitectureTests()
    {
        var services = new ServiceCollection();
        services.AddLyrionVoiceMcpEf(new ApplicationDatabaseSettings(
            Path.Combine(directory, "application.db")));
        serviceProvider = services.BuildServiceProvider();
        serviceProvider.InitialiseLyrionVoiceMcpEfAsync(CancellationToken.None)
            .GetAwaiter()
            .GetResult();
        scopeFactory = serviceProvider.GetRequiredService<IDbContextScopeFactory>();
        jobRepository = serviceProvider.GetRequiredService<IJobRepository>();
        jobLogRepository = serviceProvider.GetRequiredService<IJobLogRepository>();
        stateRepository = serviceProvider.GetRequiredService<IScheduledJobStateRepository>();
        errorLogRepository = serviceProvider.GetRequiredService<IErrorLogRepository>();
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
        var details = await service.GetAsync(
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

        var details = await service.GetAsync(
            enqueued.Job.Id,
            TestContext.Current.CancellationToken);
        var errors = await CreateErrorLogService().BrowseAsync(
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
        var service = CreateJobService(new JobCancellationRegistry(), new JobLifecycleGate());
        var job = Assert.IsType<JobEnqueued>(await service.EnqueueAsync(
            new CreateJob("test.interrupted", "{}", Now, null),
            TestContext.Current.CancellationToken));
        await MarkRunningAsync(job.Job.Id);
        var runner = CreateRunner(
            new DelegatingHandler("test.interrupted", (_, _) =>
                Task.FromResult(JobHandlerResult.Succeeded())),
            new JobCancellationRegistry(),
            new JobLifecycleGate());

        await runner.MarkRunningJobsFailedAsync(TestContext.Current.CancellationToken);

        var details = await service.GetAsync(job.Job.Id, TestContext.Current.CancellationToken);
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
            scopeFactory,
            jobRepository,
            jobLogRepository,
            [],
            new JobCancellationRegistry(),
            new JobLifecycleGate(),
            CreateErrorLogService(),
            timeProvider);

        Assert.True(await runner.RunNextDueAsync(TestContext.Current.CancellationToken));

        var details = await service.GetAsync(
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
            scopeFactory,
            jobRepository,
            stateRepository,
            service,
            [schedule],
            new CronOccurrenceCalculator(),
            policy,
            timeProvider);
        await SaveScheduledStateAsync(
            new ScheduledJobState(schedule.SchedulerStateName, Now.AddDays(-1), Now.AddDays(-1)),
            TestContext.Current.CancellationToken);

        var first = await scheduledJobs.EnqueueDueJobsAsync(
            TestContext.Current.CancellationToken);
        var second = await scheduledJobs.EnqueueDueJobsAsync(
            TestContext.Current.CancellationToken);
        var jobs = await service.BrowseAsync(
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
        var service = CreateErrorLogService();

        var errorId = await service.LogExceptionAsync(
            new InvalidOperationException("Request failed token=message-secret"),
            new ErrorLogContext(
                ErrorLogSources.Backend,
                ErrorLogAreas.ApiRequest,
                ContextJson: """{"token":"context-secret","nested":{"authorization":"Bearer nested-secret"},"safe":"retained"}"""),
            TestContext.Current.CancellationToken);

        var error = await service.GetAsync(
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
            scopeFactory,
            jobRepository,
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
        var jobService = CreateJobService(new JobCancellationRegistry(), lifecycleGate);
        var jobs = await jobService.BrowseAsync(
            new JobQuery(Type: JobTypes.SearchIndexRebuild),
            TestContext.Current.CancellationToken);
        Assert.Equal(1, jobs.Total);
        var job = (await jobService.GetAsync(jobId.Value, TestContext.Current.CancellationToken))!.Job;
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
        serviceProvider.Dispose();
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private JobService CreateJobService(
        IJobCancellationRegistry cancellationRegistry,
        IJobLifecycleGate lifecycleGate) =>
        new(
            scopeFactory,
            jobRepository,
            jobLogRepository,
            cancellationRegistry,
            lifecycleGate,
            policy,
            timeProvider);

    private JobRunner CreateRunner(
        IJobHandler handler,
        IJobCancellationRegistry cancellationRegistry,
        IJobLifecycleGate lifecycleGate) =>
        new(
            scopeFactory,
            jobRepository,
            jobLogRepository,
            [handler],
            cancellationRegistry,
            lifecycleGate,
            CreateErrorLogService(),
            timeProvider);

    private ErrorLogService CreateErrorLogService() => new(
        scopeFactory,
        errorLogRepository,
        policy,
        timeProvider,
        NullLogger<ErrorLogService>.Instance);

    private async Task MarkRunningAsync(long jobId)
    {
        Assert.True(jobId <= int.MaxValue);
        using var scope = scopeFactory.Create();
        var job = await jobRepository.GetForUpdateAsync(
            (int)jobId,
            TestContext.Current.CancellationToken);
        Assert.NotNull(job);
        job.Status = EntityJobStatus.Running;
        job.StartedAtUtc = Now.UtcDateTime;
        await scope.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private async Task SaveScheduledStateAsync(
        ScheduledJobState state,
        CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.Create();
        stateRepository.Add(new EntityScheduledJobState
        {
            Name = state.Name,
            LastRunAtUtc = state.LastRunAt.UtcDateTime,
            LastEvaluatedAtUtc = state.LastEvaluatedAt?.UtcDateTime
        });
        await scope.SaveChangesAsync(cancellationToken);
    }

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

    private sealed class TestCatalogueStore(CatalogueState state) : ICatalogueLifecycleService
    {
        public Task RecoverInterruptedRefreshAsync(CancellationToken cancellationToken) =>
            Task.CompletedTask;
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
    }
}
