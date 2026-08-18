using System.Globalization;
using System.Text.Json;
using LyrionVoiceMcp.Abstractions;
using LyrionVoiceMcp.Ef.Abstractions.DataAccess;
using LyrionVoiceMcp.Ef.Abstractions.Jobs;
using System.Data;

namespace LyrionVoiceMcp.Services;

public sealed class ErrorLogPurgeJobHandler(
    IErrorLogService service,
    OperationalPolicy policy,
    TimeProvider timeProvider) : JobHandlerBase<RetentionPayload>
{
    public override string Type => JobTypes.ErrorLogPurge;

    protected override async Task<JobHandlerResult> HandleAsync(
        JobContext context,
        RetentionPayload payload,
        CancellationToken cancellationToken)
    {
        var retentionDays = payload.RetentionDays ?? policy.ErrorRetentionDays;
        var deleted = await service.PurgeOlderThanAsync(
            timeProvider.GetUtcNow().AddDays(-retentionDays),
            cancellationToken);
        return JobHandlerResult.Succeeded(JsonSerializer.Serialize(new { retentionDays, deleted }));
    }
}

public sealed class ToolCallHistoryPurgeJobHandler(
    IToolCallHistoryService service,
    OperationalPolicy policy,
    TimeProvider timeProvider) : JobHandlerBase<RetentionPayload>
{
    public override string Type => JobTypes.ToolCallHistoryPurge;

    protected override async Task<JobHandlerResult> HandleAsync(
        JobContext context,
        RetentionPayload payload,
        CancellationToken cancellationToken)
    {
        var retentionDays = payload.RetentionDays ?? policy.ToolCallRetentionDays;
        var deleted = await service.PurgeOlderThanAsync(
            timeProvider.GetUtcNow().AddDays(-retentionDays),
            cancellationToken);
        return JobHandlerResult.Succeeded(JsonSerializer.Serialize(new { retentionDays, deleted }));
    }
}

public sealed class JobHistoryPurgeJobHandler(
    IDbContextScopeFactory scopeFactory,
    IJobRepository repository,
    OperationalPolicy policy,
    TimeProvider timeProvider) : JobHandlerBase<RetentionPayload>
{
    private const int BatchSize = 200;
    public override string Type => JobTypes.JobHistoryPurge;

    protected override async Task<JobHandlerResult> HandleAsync(
        JobContext context,
        RetentionPayload payload,
        CancellationToken cancellationToken)
    {
        var retentionDays = payload.RetentionDays ?? policy.JobRetentionDays;
        var cutoff = timeProvider.GetUtcNow().AddDays(-retentionDays);
        var deleted = 0;
        if (!OperationalEntityMapper.TryGetEntityId(context.JobId, out var currentJobId))
        {
            return JobHandlerResult.Failed("The current job identity is invalid.");
        }

        while (true)
        {
            using var scope = scopeFactory.CreateWithTransaction(IsolationLevel.Serializable);
            var count = await repository.DeleteTerminalBatchBeforeAsync(
                OperationalEntityMapper.ToUtcDateTime(cutoff),
                currentJobId,
                BatchSize,
                cancellationToken);
            await scope.SaveChangesAsync(cancellationToken);
            deleted += count;
            if (count < BatchSize)
            {
                break;
            }
        }

        return JobHandlerResult.Succeeded(JsonSerializer.Serialize(new { retentionDays, deleted }));
    }
}

public sealed record RetentionPayload(int? RetentionDays = null);

public abstract class SingleOperationalSchedule(
    OperationalSchedule configuration) : IScheduledJobDefinition
{
    public abstract string Name { get; }
    public abstract string DisplayName { get; }
    protected abstract string JobType { get; }
    protected virtual string PayloadJson => "{}";
    public string SchedulerStateName => $"schedule:{Name}";

    public Task<ScheduledJobConfiguration> GetConfigurationAsync(
        CancellationToken cancellationToken) => Task.FromResult(new ScheduledJobConfiguration(
        configuration.Enabled,
        configuration.CronExpression,
        configuration.RunOnInitialisation));

    public Task<IReadOnlyList<ScheduledJobOccurrence>> CreateOccurrencesAsync(
        DateTimeOffset dueAt,
        CancellationToken cancellationToken)
    {
        var correlation = string.Create(
            CultureInfo.InvariantCulture,
            $"scheduled:{Name}:{dueAt.UtcDateTime:yyyyMMdd'T'HHmmss.fffffff'Z'}");
        return Task.FromResult<IReadOnlyList<ScheduledJobOccurrence>>(
            [new ScheduledJobOccurrence(JobType, dueAt, PayloadJson, correlation)]);
    }
}

public sealed class CatalogueRefreshSchedule(OperationalSchedulePolicy policy)
    : SingleOperationalSchedule(policy.CatalogueRefresh)
{
    public override string Name => "catalogue-refresh";
    public override string DisplayName => "Catalogue refresh";
    protected override string JobType => JobTypes.CatalogueRefresh;
}

public sealed class ErrorLogPurgeSchedule(OperationalSchedulePolicy policy)
    : SingleOperationalSchedule(policy.ErrorLogPurge)
{
    public override string Name => "error-log-purge";
    public override string DisplayName => "Error log retention";
    protected override string JobType => JobTypes.ErrorLogPurge;
}

public sealed class JobHistoryPurgeSchedule(OperationalSchedulePolicy policy)
    : SingleOperationalSchedule(policy.JobHistoryPurge)
{
    public override string Name => "job-history-purge";
    public override string DisplayName => "Job history retention";
    protected override string JobType => JobTypes.JobHistoryPurge;
}

public sealed class ToolCallHistoryPurgeSchedule(OperationalSchedulePolicy policy)
    : SingleOperationalSchedule(policy.ToolCallHistoryPurge)
{
    public override string Name => "tool-call-history-purge";
    public override string DisplayName => "MCP tool-call retention";
    protected override string JobType => JobTypes.ToolCallHistoryPurge;
}
