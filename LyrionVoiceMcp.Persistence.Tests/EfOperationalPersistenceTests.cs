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
    private IScheduledJobConfigurationRepository scheduleConfigurationRepository = null!;
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
        scheduleConfigurationRepository = serviceProvider
            .GetRequiredService<IScheduledJobConfigurationRepository>();
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
    public async Task ScheduledJobConfigurationShouldPersistWithAuditFieldsAndUniqueNames()
    {
        using (var scope = scopeFactory.Create())
        {
            scheduleConfigurationRepository.Add(new EntityScheduledJobConfiguration
            {
                Name = "fictional-schedule",
                Enabled = true,
                CronExpression = "*/10 * * * *"
            });
            await scope.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        EntityScheduledJobConfiguration persisted;
        using (var scope = scopeFactory.CreateReadOnly())
        {
            persisted = Assert.IsType<EntityScheduledJobConfiguration>(
                await scheduleConfigurationRepository.GetByNameAsync(
                    "fictional-schedule",
                    TestContext.Current.CancellationToken));
        }

        Assert.True(persisted.Id > 0);
        Assert.NotEqual(default, persisted.CreatedAtUtc);
        Assert.Equal(persisted.CreatedAtUtc, persisted.UpdatedAtUtc);

        using var duplicateScope = scopeFactory.Create();
        scheduleConfigurationRepository.Add(new EntityScheduledJobConfiguration
        {
            Name = persisted.Name,
            Enabled = false,
            CronExpression = "0 * * * *"
        });
        await Assert.ThrowsAsync<PersistenceConflictException>(() =>
            duplicateScope.SaveChangesAsync(TestContext.Current.CancellationToken));
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
    public async Task ToolCallPageShouldSummariseOnlyTheRequestedPageAndRespectTruncation()
    {
        var calls = CreateToolCallService();
        await calls.StartAsync("search", """{"name":"First fictional search"}""", null, TestContext.Current.CancellationToken);
        var second = await calls.StartAsync("search", """{"name":"Second fictional search"}""", null, TestContext.Current.CancellationToken);
        await calls.StartAsync("browse", "{}", null, TestContext.Current.CancellationToken);
        await calls.StartAsync("search", System.Text.Json.JsonSerializer.Serialize(new { name = new string('x', 5000) }), null, TestContext.Current.CancellationToken);

        var page = await calls.BrowseAsync(new ToolCallQuery(Offset: 1, Limit: 1, ToolName: "search"), TestContext.Current.CancellationToken);
        var newest = await calls.BrowseAsync(new ToolCallQuery(Offset: 0, Limit: 1, ToolName: "search"), TestContext.Current.CancellationToken);

        Assert.Equal(3, page.Total);
        Assert.Equal(1, page.Offset);
        Assert.Equal(1, page.Limit);
        var summary = Assert.Single(page.Items);
        Assert.Equal(second!.Id, summary.Id);
        Assert.Equal("Second fictional search", summary.RequestSummary);
        Assert.Null(Assert.Single(newest.Items).RequestSummary);
    }

    [Fact]
    public async Task ToolCallReferencesShouldRemainLabelledAfterTheLiveResolverIsUnavailable()
    {
        // Arrange
        const string arguments =
            """{"player":"Studio player","items":["track_fiction","unknown_fiction"]}""";
        var metadata = new ReferenceDisplayMetadata(
            ReferenceDisplayKind.Track,
            "Paper Satellites",
            "The Lantern Hours",
            "Northern Windows");
        var calls = CreateToolCallService(new FixedReferenceDisplayMetadataResolver(
            new Dictionary<string, ReferenceDisplayMetadata>
            {
                ["track_fiction"] = metadata
            }));
        var recording = await calls.StartAsync(
            "play",
            arguments,
            null,
            TestContext.Current.CancellationToken);

        // Act
        var restartedCalls = CreateToolCallService();
        var call = await restartedCalls.GetAsync(
            recording!.Id,
            TestContext.Current.CancellationToken);
        var page = await restartedCalls.BrowseAsync(
            new ToolCallQuery(ToolName: "play"),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(arguments, call?.ArgumentsJson);
        Assert.False(call?.ReferenceSnapshotsTruncated);
        var snapshots = Assert.IsAssignableFrom<IReadOnlyList<ToolCallReferenceSnapshot>>(
            call!.ReferenceSnapshots);
        Assert.Equal(["track_fiction", "unknown_fiction"], snapshots.Select(item => item.Reference));
        Assert.Equal(metadata, snapshots[0].DisplayMetadata);
        Assert.Null(snapshots[1].DisplayMetadata);
        Assert.Equal(
            "Studio player · Paper Satellites · The Lantern Hours · Northern Windows + 1 more",
            Assert.Single(page.Items).RequestSummary);
    }

    [Fact]
    public async Task ReferenceCaptureFailureShouldRetainTheCallAndOriginalReference()
    {
        var calls = CreateToolCallService(new ThrowingReferenceDisplayMetadataResolver());

        var recording = await calls.StartAsync(
            "browse",
            """{"browseRef":"albums_fiction"}""",
            null,
            TestContext.Current.CancellationToken);
        var call = await calls.GetAsync(recording!.Id, TestContext.Current.CancellationToken);

        var snapshot = Assert.Single(call!.ReferenceSnapshots!);
        Assert.Equal("albums_fiction", snapshot.Reference);
        Assert.Null(snapshot.DisplayMetadata);
    }

    [Fact]
    public async Task ReferenceSnapshotsShouldRespectTheToolCallJsonBound()
    {
        var references = Enumerable.Range(0, 10)
            .Select(index => $"track_{index}")
            .ToArray();
        var metadata = references.ToDictionary(
            reference => reference,
            reference => new ReferenceDisplayMetadata(
                ReferenceDisplayKind.Track,
                reference + new string('x', 500),
                new string('y', 500),
                new string('z', 500)));
        var calls = CreateToolCallService(new FixedReferenceDisplayMetadataResolver(metadata));

        var recording = await calls.StartAsync(
            "play",
            System.Text.Json.JsonSerializer.Serialize(new
            {
                player = "Studio player",
                items = references
            }),
            null,
            TestContext.Current.CancellationToken);
        var call = await calls.GetAsync(recording!.Id, TestContext.Current.CancellationToken);

        Assert.False(call?.ArgumentsTruncated);
        Assert.True(call?.ReferenceSnapshotsTruncated);
        Assert.Null(call?.ReferenceSnapshots);
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

    private ToolCallHistoryService CreateToolCallService(
        IReferenceDisplayMetadataResolver? referenceResolver = null) => new(
        scopeFactory,
        toolCallRepository,
        policy,
        timeProvider,
        referenceResolver ?? NullReferenceDisplayMetadataResolver.Instance,
        NullLogger<ToolCallHistoryService>.Instance);

    private sealed class FixedReferenceDisplayMetadataResolver(
        IReadOnlyDictionary<string, ReferenceDisplayMetadata> metadata)
        : IReferenceDisplayMetadataResolver
    {
        public ReferenceDisplayMetadata? Resolve(string reference) =>
            metadata.GetValueOrDefault(reference);
    }

    private sealed class ThrowingReferenceDisplayMetadataResolver
        : IReferenceDisplayMetadataResolver
    {
        public ReferenceDisplayMetadata? Resolve(string reference) =>
            throw new InvalidOperationException("Fictional capture failure.");
    }

    private sealed class NullReferenceDisplayMetadataResolver : IReferenceDisplayMetadataResolver
    {
        public static readonly NullReferenceDisplayMetadataResolver Instance = new();

        public ReferenceDisplayMetadata? Resolve(string reference) => null;
    }

    private sealed class FixedTimeProvider(DateTimeOffset value) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => value;
    }
}
