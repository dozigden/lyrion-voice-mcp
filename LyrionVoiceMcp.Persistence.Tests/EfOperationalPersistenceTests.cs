using LyrionVoiceMcp.Abstractions;
using LyrionVoiceMcp.Ef;
using LyrionVoiceMcp.Ef.Abstractions.DataAccess;
using LyrionVoiceMcp.Ef.Abstractions.Entities;
using LyrionVoiceMcp.Ef.Abstractions.ErrorLogs;
using LyrionVoiceMcp.Ef.Abstractions.Jobs;
using LyrionVoiceMcp.Ef.Abstractions.ToolCalls;
using LyrionVoiceMcp.Services;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace LyrionVoiceMcp.Persistence.Tests;

public sealed class EfOperationalPersistenceTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Now =
        DateTimeOffset.Parse("2026-08-18T12:00:00Z");

    private readonly string directory = Path.Combine(
        Path.GetTempPath(),
        $"lvm-ef-operations-{Guid.NewGuid():N}");
    private readonly OperationalPolicy policy = new(
        90,
        90,
        30,
        4096,
        TimeZoneInfo.Utc);
    private readonly FixedTimeProvider timeProvider = new(Now);
    private ServiceProvider serviceProvider = null!;
    private IDbContextScopeFactory scopeFactory = null!;
    private IJobRepository jobRepository = null!;
    private IJobLogRepository jobLogRepository = null!;
    private IErrorLogRepository errorRepository = null!;
    private IToolCallRepository toolCallRepository = null!;

    public async ValueTask InitializeAsync()
    {
        var services = new ServiceCollection();
        services.AddLyrionVoiceMcpEf(new ApplicationDatabaseSettings(
            Path.Combine(directory, "application.db")));
        serviceProvider = services.BuildServiceProvider();
        await serviceProvider.InitialiseLyrionVoiceMcpEfAsync(
            TestContext.Current.CancellationToken);
        scopeFactory = serviceProvider.GetRequiredService<IDbContextScopeFactory>();
        jobRepository = serviceProvider.GetRequiredService<IJobRepository>();
        jobLogRepository = serviceProvider.GetRequiredService<IJobLogRepository>();
        errorRepository = serviceProvider.GetRequiredService<IErrorLogRepository>();
        toolCallRepository = serviceProvider.GetRequiredService<IToolCallRepository>();
    }

    public async ValueTask DisposeAsync()
    {
        if (serviceProvider is not null)
        {
            await serviceProvider.DisposeAsync();
        }

        SqliteConnection.ClearAllPools();
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task EnqueueShouldPersistInitialLogAndRejectDatabaseConflicts()
    {
        var service = CreateJobService();
        var first = Assert.IsType<JobEnqueued>(await service.EnqueueAsync(
            new CreateJob(JobTypes.CatalogueRefresh, "{\"input\":42}", Now, "first-correlation"),
            TestContext.Current.CancellationToken));

        var duplicateCorrelation = await service.EnqueueAsync(
            new CreateJob("fictional.work", "{}", Now, "first-correlation"),
            TestContext.Current.CancellationToken);
        var duplicateActiveType = await service.EnqueueAsync(
            new CreateJob(JobTypes.CatalogueRefresh, "{}", Now, "second-correlation"),
            TestContext.Current.CancellationToken);
        var details = await service.GetAsync(first.Job.Id, TestContext.Current.CancellationToken);

        Assert.IsType<JobEnqueueRejected>(duplicateCorrelation);
        Assert.IsType<JobEnqueueRejected>(duplicateActiveType);
        Assert.Equal("{\"input\":42}", details?.Job.PayloadJson);
        Assert.Equal("Job enqueued.", Assert.Single(details!.Logs).Message);
    }

    [Fact]
    public async Task CorrelationPrefixQueriesShouldTreatLikeCharactersLiterally()
    {
        const string prefix = "scheduled:fictional_%\\:";
        var service = CreateJobService();
        var target = Assert.IsType<JobEnqueued>(await service.EnqueueAsync(
            new CreateJob("fictional.work", "{}", Now, prefix + "occurrence-1"),
            TestContext.Current.CancellationToken));

        using var scope = scopeFactory.CreateReadOnly();
        var active = await jobRepository.GetLatestActiveByCorrelationPrefixesAsync(
            prefix,
            "adhoc:fictional:",
            TestContext.Current.CancellationToken);

        Assert.NotNull(active);
        Assert.Equal(target.Job.Id, active.Id);
    }

    [Fact]
    public async Task ErrorAndToolCallShouldRetainDetailsLinksAndIdempotentReports()
    {
        var errors = CreateErrorLogService();
        var reportId = Guid.NewGuid();
        var errorId = await errors.LogExceptionAsync(
            new InvalidOperationException("Fictional failure."),
            new ErrorLogContext(
                ErrorLogSources.Mcp,
                ErrorLogAreas.McpToolCall,
                TraceIdentifier: "trace-1",
                ContextJson: "{\"context\":true}",
                ReportId: reportId),
            TestContext.Current.CancellationToken);
        var duplicateId = await errors.LogExceptionAsync(
            new InvalidOperationException("Duplicate fictional failure."),
            new ErrorLogContext(
                ErrorLogSources.Mcp,
                ErrorLogAreas.McpToolCall,
                ReportId: reportId),
            TestContext.Current.CancellationToken);

        var calls = CreateToolCallService();
        var recording = await calls.StartAsync(
            "search",
            "{\"query\":\"night\"}",
            "trace-1",
            TestContext.Current.CancellationToken);
        Assert.NotNull(recording);
        await calls.CompleteAsync(
            recording.Id,
            ToolCallStatus.Failed,
            recording.StartedAt,
            "{\"isError\":true}",
            "Fictional failure.",
            errorId,
            TestContext.Current.CancellationToken);

        var call = await calls.GetAsync(recording.Id, TestContext.Current.CancellationToken);
        var error = await errors.GetAsync(errorId!.Value, TestContext.Current.CancellationToken);

        Assert.Null(duplicateId);
        Assert.Equal(errorId, call?.ErrorLogId);
        Assert.Equal("{\"query\":\"night\"}", call?.ArgumentsJson);
        Assert.Equal("{\"isError\":true}", call?.ResultJson);
        Assert.Equal("{\"context\":true}", error?.ContextJson);
    }

    [Fact]
    public async Task StartupRecoveryShouldInterruptEveryRunningToolCall()
    {
        var calls = CreateToolCallService();
        var first = await calls.StartAsync(
            "browse",
            "{}",
            null,
            TestContext.Current.CancellationToken);
        var second = await calls.StartAsync(
            "search",
            "{}",
            null,
            TestContext.Current.CancellationToken);

        await calls.MarkRunningInterruptedAsync(TestContext.Current.CancellationToken);

        var firstCall = await calls.GetAsync(first!.Id, TestContext.Current.CancellationToken);
        var secondCall = await calls.GetAsync(second!.Id, TestContext.Current.CancellationToken);
        Assert.Equal(ToolCallStatus.Interrupted, firstCall?.Status);
        Assert.Equal(ToolCallStatus.Interrupted, secondCall?.Status);
        Assert.Equal("Tool call was interrupted by server startup.", firstCall?.ErrorMessage);
    }

    [Fact]
    public async Task RetentionShouldDeleteBoundedHistoryAndClearDiagnosticLinks()
    {
        int oldJobId;
        int currentJobId;
        int oldErrorId;
        using (var scope = scopeFactory.Create())
        {
            var oldJob = new EntityJob
            {
                Type = "fictional.old",
                Status = EntityJobStatus.Completed,
                RunAfterUtc = Now.AddDays(-100).UtcDateTime,
                CompletedAtUtc = Now.AddDays(-100).UtcDateTime
            };
            var currentJob = new EntityJob
            {
                Type = JobTypes.JobHistoryPurge,
                Status = EntityJobStatus.Running,
                RunAfterUtc = Now.UtcDateTime,
                StartedAtUtc = Now.UtcDateTime
            };
            jobRepository.Add(oldJob);
            jobRepository.Add(currentJob);
            jobLogRepository.Add(new EntityJobLog
            {
                Job = oldJob,
                Level = EntityJobLogLevel.Information,
                Message = "Old fictional log.",
                LoggedAtUtc = Now.AddDays(-100).UtcDateTime
            });
            var oldError = new EntityErrorLog
            {
                OccurredAtUtc = Now.AddDays(-100).UtcDateTime,
                Source = ErrorLogSources.Backend,
                Area = ErrorLogAreas.JobRunner,
                ExceptionType = "FictionalException",
                Message = "Old fictional error.",
                Job = oldJob
            };
            errorRepository.Add(oldError);
            toolCallRepository.Add(new EntityToolCall
            {
                ToolCallId = "old-call",
                ToolName = "search",
                Status = EntityToolCallStatus.Succeeded,
                StartedAtUtc = Now.AddDays(-100).UtcDateTime,
                CompletedAtUtc = Now.AddDays(-100).UtcDateTime,
                ArgumentsJson = "{}",
                ErrorLog = oldError
            });
            await scope.SaveChangesAsync(TestContext.Current.CancellationToken);
            oldJobId = oldJob.Id;
            currentJobId = currentJob.Id;
            oldErrorId = oldError.Id;
        }

        var jobs = new JobHistoryPurgeJobHandler(
            scopeFactory,
            jobRepository,
            policy,
            timeProvider);
        var jobResult = await jobs.HandleAsync(
            new JobContext(currentJobId, JobTypes.JobHistoryPurge, "{}"),
            TestContext.Current.CancellationToken);
        var calls = CreateToolCallService();
        var deletedCalls = await calls.PurgeOlderThanAsync(
            Now.AddDays(-30),
            TestContext.Current.CancellationToken);

        Assert.True(jobResult.Success);
        Assert.Null(await CreateJobService().GetAsync(oldJobId, TestContext.Current.CancellationToken));
        Assert.Null((await CreateErrorLogService().GetAsync(
            oldErrorId,
            TestContext.Current.CancellationToken))?.JobId);
        Assert.Equal(1, deletedCalls);

        var errors = CreateErrorLogService();
        var deletedErrors = await errors.PurgeOlderThanAsync(
            Now.AddDays(-90),
            TestContext.Current.CancellationToken);
        Assert.Equal(1, deletedErrors);
        Assert.Null(await errors.GetAsync(oldErrorId, TestContext.Current.CancellationToken));
    }

    private JobService CreateJobService() => new(
        scopeFactory,
        jobRepository,
        jobLogRepository,
        new JobCancellationRegistry(),
        new JobLifecycleGate(),
        policy,
        timeProvider);

    private ErrorLogService CreateErrorLogService() => new(
        scopeFactory,
        errorRepository,
        policy,
        timeProvider,
        NullLogger<ErrorLogService>.Instance);

    private ToolCallHistoryService CreateToolCallService() => new(
        scopeFactory,
        toolCallRepository,
        policy,
        timeProvider,
        NullLogger<ToolCallHistoryService>.Instance);

    private sealed class FixedTimeProvider(DateTimeOffset value) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => value;
    }
}
