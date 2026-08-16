using LyrionVoiceMcp.Abstractions;
using LyrionVoiceMcp.Persistence;

namespace LyrionVoiceMcp.Persistence.Tests;

public sealed class SqliteOperationalStoreTests : IDisposable
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-08-16T12:00:00Z");
    private readonly string directory = Path.Combine(Path.GetTempPath(), $"lyrion-operations-{Guid.NewGuid():N}");
    private readonly SqliteOperationalStore store;

    public SqliteOperationalStoreTests()
    {
        store = new SqliteOperationalStore(
            new OperationalSettings(Path.Combine(directory, "operations.db"), 90, 90, 30, 4096, "UTC"),
            new FixedTimeProvider(Now));
        store.InitialiseAsync(CancellationToken.None).GetAwaiter().GetResult();
    }

    [Fact]
    public async Task JobLifecycleShouldPersistPayloadResultAndOrderedLogs()
    {
        var job = await store.CreateAsync(
            new CreateJob("fictional.work", "{\"input\":42}", Now, "test:job:1"),
            Now,
            TestContext.Current.CancellationToken);

        var started = await store.TryStartNextDueAsync(Now, TestContext.Current.CancellationToken);
        await store.AppendLogAsync(job.Id, JobLogLevel.Information, "Half way.", "{\"processed\":1}", Now.AddSeconds(1), TestContext.Current.CancellationToken);
        var completed = await store.CompleteAsync(job.Id, "{\"output\":84}", Now.AddSeconds(2), TestContext.Current.CancellationToken);
        var details = await store.GetAsync(job.Id, TestContext.Current.CancellationToken);

        Assert.Equal(job.Id, started?.Id);
        Assert.True(completed);
        Assert.Equal(JobStatus.Completed, details?.Job.Status);
        Assert.Equal("{\"input\":42}", details?.Job.PayloadJson);
        Assert.Equal("{\"output\":84}", details?.Job.ResultJson);
        Assert.Equal(["Job enqueued.", "Job started.", "Half way.", "Job completed."], details?.Logs.Select(log => log.Message));
    }

    [Fact]
    public async Task ErrorAndToolCallShouldRetainTheirCrossReferenceAndFullJson()
    {
        var errorId = await store.AddAsync(
            new ErrorLogEntry(null, Now, "mcp", "tool-call", "FictionalException", "Fictional failure", "stack", "trace-1", null, null, null, "{\"context\":true}"),
            TestContext.Current.CancellationToken);
        await store.StartAsync(
            new ToolCallStart("call-1", "search", Now, "{\"query\":\"night\"}", false, "trace-1"),
            TestContext.Current.CancellationToken);
        await store.CompleteAsync(
            new ToolCallCompletion("call-1", ToolCallStatus.Failed, Now.AddMilliseconds(25), 25, "{\"isError\":true}", false, "Fictional failure", errorId),
            TestContext.Current.CancellationToken);

        var call = await store.GetAsync("call-1", TestContext.Current.CancellationToken);
        var error = await store.GetErrorLogAsync(errorId!.Value, TestContext.Current.CancellationToken);

        Assert.Equal(errorId, call?.ErrorLogId);
        Assert.Equal("{\"query\":\"night\"}", call?.ArgumentsJson);
        Assert.Equal("{\"isError\":true}", call?.ResultJson);
        Assert.Equal("trace-1", error?.TraceIdentifier);
    }

    [Fact]
    public async Task InitialisationShouldMarkAbandonedToolCallsInterrupted()
    {
        await store.StartAsync(
            new ToolCallStart("call-2", "browse", Now.AddSeconds(-5), "{}", false, null),
            TestContext.Current.CancellationToken);

        await store.InitialiseAsync(TestContext.Current.CancellationToken);
        var call = await store.GetAsync("call-2", TestContext.Current.CancellationToken);

        Assert.Equal(ToolCallStatus.Interrupted, call?.Status);
        Assert.Equal("Tool call was interrupted by server startup.", call?.ErrorMessage);
    }

    [Fact]
    public async Task ScheduledStateAndReportIdentityShouldRoundTripIdempotently()
    {
        var reportId = Guid.NewGuid();
        var firstErrorId = await store.AddAsync(
            new ErrorLogEntry(reportId, Now, "backend", "scheduler", "FictionalException", "First", null, null, null, null, null, null),
            TestContext.Current.CancellationToken);
        var duplicateErrorId = await store.AddAsync(
            new ErrorLogEntry(reportId, Now, "backend", "scheduler", "FictionalException", "Duplicate", null, null, null, null, null, null),
            TestContext.Current.CancellationToken);
        var state = new ScheduledJobState("schedule:test", Now, Now.AddSeconds(1));
        await store.UpsertScheduledJobStateAsync(state, TestContext.Current.CancellationToken);

        Assert.NotNull(firstErrorId);
        Assert.Null(duplicateErrorId);
        Assert.True(await store.ReportExistsAsync(reportId, TestContext.Current.CancellationToken));
        Assert.Equal(state, await store.GetScheduledJobStateAsync("schedule:test", TestContext.Current.CancellationToken));
    }

    public void Dispose()
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
