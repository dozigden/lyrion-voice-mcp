using System.Globalization;
using LyrionVoiceMcp.Abstractions;

namespace LyrionVoiceMcp.Services;

public sealed class ScheduledJobService(
    IJobStore store,
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
            var state = await store.GetScheduledJobStateAsync(
                schedule.SchedulerStateName,
                cancellationToken);
            var scheduledPrefix = $"scheduled:{schedule.Name}:";
            var adHocPrefix = $"adhoc:{schedule.Name}:";
            var current = await store.GetLatestActiveByCorrelationPrefixesAsync(
                scheduledPrefix,
                adHocPrefix,
                cancellationToken);
            var lastStarted = await store.GetLatestStartedByCorrelationPrefixesAsync(
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
            var state = await store.GetScheduledJobStateAsync(
                schedule.SchedulerStateName,
                cancellationToken);
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
                if (await store.ExistsByCorrelationIdAsync(occurrence.CorrelationId, cancellationToken))
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

    private Task UpdateStateAsync(
        string name,
        DateTimeOffset now,
        CancellationToken cancellationToken) =>
        store.UpsertScheduledJobStateAsync(
            new ScheduledJobState(name, now, now),
            cancellationToken);

    private static ScheduledJobRun? ToRun(Job? job) => job is null
        ? null
        : new ScheduledJobRun(job.Id, job.Status, job.StartedAt);
}
