using System.Globalization;
using LyrionVoiceMcp.Abstractions;
using LyrionVoiceMcp.Ef.Abstractions.DataAccess;
using LyrionVoiceMcp.Ef.Abstractions.Entities;
using LyrionVoiceMcp.Ef.Abstractions.Jobs;

namespace LyrionVoiceMcp.Services;

public sealed class ScheduledJobService(
    IDbContextScopeFactory scopeFactory,
    IJobRepository jobRepository,
    IScheduledJobStateRepository stateRepository,
    IJobService jobService,
    IEnumerable<IScheduledJobDefinition> schedules,
    ICronOccurrenceCalculator cronOccurrenceCalculator,
    OperationalPolicy policy,
    TimeProvider timeProvider) : IScheduledJobService
{
    public async Task<IReadOnlyList<ScheduledJob>> ListAsync(
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var items = new List<ScheduledJob>();
        foreach (var schedule in schedules.OrderBy(value => value.DisplayName))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var configuration = await schedule.GetConfigurationAsync(cancellationToken);
            var state = await GetStateAsync(schedule.SchedulerStateName, cancellationToken);
            var scheduledPrefix = $"scheduled:{schedule.Name}:";
            var adHocPrefix = $"adhoc:{schedule.Name}:";
            var current = await GetLatestActiveAsync(
                scheduledPrefix,
                adHocPrefix,
                cancellationToken);
            var lastStarted = await GetLatestStartedAsync(
                scheduledPrefix,
                adHocPrefix,
                cancellationToken);
            var nextOccurrence = configuration.Enabled
                ? cronOccurrenceCalculator.GetNextOccurrence(
                    configuration.CronExpression,
                    policy.TimeZone,
                    now)
                : null;
            items.Add(new ScheduledJob(
                schedule.Name,
                schedule.DisplayName,
                configuration.Enabled,
                configuration.CronExpression,
                policy.TimeZone.Id,
                state?.LastEvaluatedAt,
                nextOccurrence,
                ToRun(current),
                ToRun(lastStarted)));
        }

        return items;
    }

    public async Task<ScheduledJobRunOutcome> RunNowAsync(
        string scheduleName,
        CancellationToken cancellationToken)
    {
        var schedule = schedules.SingleOrDefault(value =>
            string.Equals(value.Name, scheduleName, StringComparison.OrdinalIgnoreCase));
        if (schedule is null)
        {
            return new ScheduledJobRunRejected("Scheduled job not found.");
        }

        var now = timeProvider.GetUtcNow();
        var occurrences = await schedule.CreateOccurrencesAsync(now, cancellationToken);
        var token = string.Create(
            CultureInfo.InvariantCulture,
            $"{now:yyyyMMdd'T'HHmmss'Z'}:{Guid.NewGuid():N}");
        var ids = new List<long>();
        for (var index = 0; index < occurrences.Count; index++)
        {
            var occurrence = occurrences[index];
            var outcome = await jobService.EnqueueAsync(
                new CreateJob(
                    occurrence.JobType,
                    occurrence.PayloadJson,
                    now,
                    $"adhoc:{schedule.Name}:{token}:{index}"),
                cancellationToken);
            if (outcome is not JobEnqueued enqueued)
            {
                var message = outcome is JobEnqueueRejected rejected
                    ? rejected.Message
                    : "Scheduled job could not be queued.";
                return new ScheduledJobRunRejected(message);
            }

            ids.Add(enqueued.Job.Id);
        }

        return new ScheduledJobRunStarted(ids.Count, ids);
    }

    public async Task<IReadOnlyList<ScheduledJobEnqueueResult>> EnqueueDueJobsAsync(
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var results = new List<ScheduledJobEnqueueResult>();
        foreach (var schedule in schedules)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var configuration = await schedule.GetConfigurationAsync(cancellationToken);
            var state = await GetStateAsync(schedule.SchedulerStateName, cancellationToken);
            if (state is null && (!configuration.Enabled || !configuration.RunOnInitialisation))
            {
                await UpdateStateAsync(schedule.SchedulerStateName, now, cancellationToken);
                continue;
            }

            if (!configuration.Enabled)
            {
                await UpdateStateAsync(schedule.SchedulerStateName, now, cancellationToken);
                continue;
            }

            var dueAt = state is null
                ? now
                : cronOccurrenceCalculator.GetLatestOccurrence(
                    configuration.CronExpression,
                    policy.TimeZone,
                    state.LastRunAt,
                    now);
            if (dueAt is null)
            {
                await UpdateStateAsync(schedule.SchedulerStateName, now, cancellationToken);
                continue;
            }

            var occurrences = await schedule.CreateOccurrencesAsync(dueAt.Value, cancellationToken);
            foreach (var occurrence in occurrences)
            {
                if (await JobExistsAsync(occurrence.CorrelationId, cancellationToken))
                {
                    results.Add(new ScheduledJobEnqueueResult(
                        schedule.Name,
                        occurrence.DueAt,
                        occurrence.CorrelationId,
                        false,
                        null));
                    continue;
                }

                var outcome = await jobService.EnqueueAsync(
                    new CreateJob(
                        occurrence.JobType,
                        occurrence.PayloadJson,
                        occurrence.DueAt,
                        occurrence.CorrelationId),
                    cancellationToken);
                if (outcome is not JobEnqueued enqueued)
                {
                    var message = outcome is JobEnqueueRejected rejected
                        ? rejected.Message
                        : "Unknown enqueue failure.";
                    throw new InvalidOperationException(
                        $"Scheduled job '{schedule.Name}' could not enqueue occurrence '{occurrence.CorrelationId}': {message}");
                }

                results.Add(new ScheduledJobEnqueueResult(
                    schedule.Name,
                    occurrence.DueAt,
                    occurrence.CorrelationId,
                    true,
                    enqueued.Job.Id));
            }

            await UpdateStateAsync(schedule.SchedulerStateName, now, cancellationToken);
        }

        return results;
    }

    private async Task<ScheduledJobState?> GetStateAsync(
        string name,
        CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateReadOnly();
        var state = await stateRepository.GetByNameAsync(name, cancellationToken);
        return state is null
            ? null
            : new ScheduledJobState(
                state.Name,
                new DateTimeOffset(DateTime.SpecifyKind(state.LastRunAtUtc, DateTimeKind.Utc)),
                state.LastEvaluatedAtUtc is null
                    ? null
                    : new DateTimeOffset(DateTime.SpecifyKind(
                        state.LastEvaluatedAtUtc.Value,
                        DateTimeKind.Utc)));
    }

    private async Task<bool> JobExistsAsync(
        string correlationId,
        CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateReadOnly();
        return await jobRepository.ExistsByCorrelationIdAsync(correlationId, cancellationToken);
    }

    private async Task<Job?> GetLatestActiveAsync(
        string firstPrefix,
        string secondPrefix,
        CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateReadOnly();
        var job = await jobRepository.GetLatestActiveByCorrelationPrefixesAsync(
            firstPrefix,
            secondPrefix,
            cancellationToken);
        return job is null ? null : OperationalEntityMapper.ToModel(job);
    }

    private async Task<Job?> GetLatestStartedAsync(
        string firstPrefix,
        string secondPrefix,
        CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateReadOnly();
        var job = await jobRepository.GetLatestStartedByCorrelationPrefixesAsync(
            firstPrefix,
            secondPrefix,
            cancellationToken);
        return job is null ? null : OperationalEntityMapper.ToModel(job);
    }

    private async Task UpdateStateAsync(
        string name,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.Create();
        var state = await stateRepository.GetByNameAsync(name, cancellationToken);
        if (state is null)
        {
            stateRepository.Add(new EntityScheduledJobState
            {
                Name = name,
                LastRunAtUtc = OperationalEntityMapper.ToUtcDateTime(now),
                LastEvaluatedAtUtc = OperationalEntityMapper.ToUtcDateTime(now)
            });
        }
        else
        {
            state.LastRunAtUtc = OperationalEntityMapper.ToUtcDateTime(now);
            state.LastEvaluatedAtUtc = OperationalEntityMapper.ToUtcDateTime(now);
        }

        await scope.SaveChangesAsync(cancellationToken);
    }

    private static ScheduledJobRun? ToRun(Job? job) => job is null
        ? null
        : new ScheduledJobRun(job.Id, job.Status, job.StartedAt);
}
