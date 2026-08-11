namespace LyrionVoiceMcp.Abstractions;

public interface IOperationalStatusService
{
    OperationalStatus GetStatus();
}

public sealed record OperationalStatus(string Status);

