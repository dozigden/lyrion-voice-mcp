using LyrionVoiceMcp.Abstractions;
using LyrionVoiceMcp.Ef.Abstractions.DataAccess;
using LyrionVoiceMcp.Ef.Abstractions.Entities;
using LyrionVoiceMcp.Ef.Abstractions.Jobs;

namespace LyrionVoiceMcp.Services;

public sealed class ScheduledJobConfigurationProvider(
    IDbContextScopeFactory scopeFactory,
    IScheduledJobConfigurationRepository configurationRepository,
    IScheduledJobStateRepository stateRepository,
    TimeProvider timeProvider)
{
    public async Task<ScheduledJobConfiguration> ResolveAsync(
        string name,
        OperationalSchedule deploymentDefault,
        bool available,
        ScheduledJobConfigurationKind kind,
        CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateReadOnly();
        var persisted = await configurationRepository.GetByNameAsync(name, cancellationToken);
        var configuredEnabled = persisted?.Enabled ?? deploymentDefault.Enabled;
        var cronExpression = persisted?.CronExpression ?? deploymentDefault.CronExpression;
        var editableConfiguration = CreateEditableConfiguration(
            kind,
            configuredEnabled,
            cronExpression);
        return new ScheduledJobConfiguration(
            configuredEnabled && available,
            cronExpression,
            deploymentDefault.RunOnInitialisation,
            editableConfiguration);
    }

    public async Task SaveAsync(
        string name,
        bool enabled,
        string cronExpression,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        using var scope = scopeFactory.Create();
        var configuration = await configurationRepository.GetByNameAsync(
            name,
            cancellationToken);
        if (configuration is null)
        {
            configurationRepository.Add(new EntityScheduledJobConfiguration
            {
                Name = name,
                Enabled = enabled,
                CronExpression = cronExpression
            });
        }
        else
        {
            configuration.Enabled = enabled;
            configuration.CronExpression = cronExpression;
        }

        var stateName = $"schedule:{name}";
        var state = await stateRepository.GetByNameAsync(stateName, cancellationToken);
        if (state is null)
        {
            stateRepository.Add(new EntityScheduledJobState
            {
                Name = stateName,
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

    private static ScheduledJobEditableConfiguration CreateEditableConfiguration(
        ScheduledJobConfigurationKind kind,
        bool configuredEnabled,
        string cronExpression)
    {
        var intervalMinutes = kind == ScheduledJobConfigurationKind.Interval
            ? ScheduledJobConfigurationConversions.TryGetIntervalMinutes(cronExpression)
            : null;
        var dailyTime = kind == ScheduledJobConfigurationKind.DailyTime
            ? ScheduledJobConfigurationConversions.TryGetDailyTime(cronExpression)
            : null;
        return new ScheduledJobEditableConfiguration(
            kind,
            configuredEnabled,
            intervalMinutes,
            dailyTime);
    }
}

internal static class ScheduledJobConfigurationConversions
{
    private static readonly int[] SupportedIntervals = [1, 5, 10, 15, 30, 60];

    public static bool IsSupportedInterval(int value) => SupportedIntervals.Contains(value);

    public static string ToIntervalCron(int intervalMinutes) => intervalMinutes switch
    {
        1 => "* * * * *",
        60 => "0 * * * *",
        _ => $"*/{intervalMinutes} * * * *"
    };

    public static int? TryGetIntervalMinutes(string cronExpression)
    {
        if (cronExpression == "* * * * *")
        {
            return 1;
        }

        if (cronExpression is "0 * * * *" or "*/60 * * * *")
        {
            return 60;
        }

        foreach (var interval in SupportedIntervals.Where(value => value is not 1 and not 60))
        {
            if (cronExpression == $"*/{interval} * * * *")
            {
                return interval;
            }
        }

        return null;
    }

    public static string ToDailyCron(TimeOnly dailyTime) =>
        $"{dailyTime.Minute} {dailyTime.Hour} * * *";

    public static string? TryGetDailyTime(string cronExpression)
    {
        var parts = cronExpression.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 5
            || parts[2] != "*"
            || parts[3] != "*"
            || parts[4] != "*"
            || !int.TryParse(parts[0], out var minute)
            || !int.TryParse(parts[1], out var hour)
            || minute is < 0 or > 59
            || hour is < 0 or > 23)
        {
            return null;
        }

        return new TimeOnly(hour, minute).ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture);
    }
}
