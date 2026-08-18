namespace LyrionVoiceMcp.Api.Configuration;

public sealed record SearchObservationSettings(int RetentionDays)
{
    public static SearchObservationSettings FromValue(string? retentionDays)
    {
        var days = 90;
        if (!string.IsNullOrWhiteSpace(retentionDays)
            && (!int.TryParse(retentionDays, out days) || days is < 1 or > 3650))
        {
            throw new InvalidOperationException(
                "LyrionVoiceMcpObservations:RetentionDays must be between 1 and 3650.");
        }

        return new SearchObservationSettings(days);
    }
}
