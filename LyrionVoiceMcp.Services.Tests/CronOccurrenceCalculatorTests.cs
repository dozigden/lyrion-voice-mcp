using LyrionVoiceMcp.Services;

namespace LyrionVoiceMcp.Services.Tests;

public sealed class CronOccurrenceCalculatorTests
{
    [Fact]
    public void GetNextOccurrenceShouldRespectTheConfiguredTimeZone()
    {
        var calculator = new CronOccurrenceCalculator();
        var london = TimeZoneInfo.FindSystemTimeZoneById("Europe/London");

        var next = calculator.GetNextOccurrence(
            "0 3 * * *",
            london,
            DateTimeOffset.Parse("2026-08-16T00:00:00Z"));

        Assert.Equal(DateTimeOffset.Parse("2026-08-16T02:00:00Z"), next);
    }

    [Fact]
    public void GetLatestOccurrenceShouldReturnOnlyAnOccurrenceInsideTheWindow()
    {
        var calculator = new CronOccurrenceCalculator();

        var occurrence = calculator.GetLatestOccurrence(
            "*/5 * * * *",
            TimeZoneInfo.Utc,
            DateTimeOffset.Parse("2026-08-16T11:56:00Z"),
            DateTimeOffset.Parse("2026-08-16T12:03:00Z"));

        Assert.Equal(DateTimeOffset.Parse("2026-08-16T12:00:00Z"), occurrence);
    }
}
