namespace LyrionVoiceMcp.Abstractions;

public interface ILmsConnectionProbe
{
    Task<LmsConnectionStatus> CheckAsync(CancellationToken cancellationToken);
}

public interface ILmsConnectionStatusService
{
    Task<LmsConnectionStatus> GetStatusAsync(CancellationToken cancellationToken);
}

public enum LmsConnectionState
{
    NotConfigured,
    Online,
    Unavailable
}

public sealed record LmsConnectionStatus(
    LmsConnectionState State,
    string? ServerId,
    string? BaseUrl,
    string? ServerVersion,
    string Message);
