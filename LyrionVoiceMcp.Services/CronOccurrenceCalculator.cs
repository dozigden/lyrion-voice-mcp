using Cronos;
using LyrionVoiceMcp.Abstractions;

namespace LyrionVoiceMcp.Services;

public sealed class CronOccurrenceCalculator : ICronOccurrenceCalculator
{
    public DateTimeOffset? GetLatestOccurrence(
        string cronExpression,
        TimeZoneInfo timeZone,
        DateTimeOffset fromExclusive,
        DateTimeOffset throughInclusive) =>
        Parse(cronExpression)
            .GetOccurrences(
                fromExclusive.UtcDateTime,
                throughInclusive.UtcDateTime,
                timeZone,
                fromInclusive: false,
                toInclusive: true)
            .Select(value => (DateTimeOffset?)new DateTimeOffset(value, TimeSpan.Zero))
            .LastOrDefault();

    public DateTimeOffset? GetNextOccurrence(
        string cronExpression,
        TimeZoneInfo timeZone,
        DateTimeOffset after)
    {
        var value = Parse(cronExpression).GetNextOccurrence(after.UtcDateTime, timeZone);
        return value is null ? null : new DateTimeOffset(value.Value, TimeSpan.Zero);
    }

    private static CronExpression Parse(string cronExpression) =>
        CronExpression.Parse(cronExpression, CronFormat.Standard);
}
