using LyrionVoiceMcp.Abstractions;

namespace LyrionVoiceMcp.Services;

public sealed class LmsConnectionStatusService(
    ILmsConnectionProbe probe) : ILmsConnectionStatusService
{
    public Task<LmsConnectionStatus> GetStatusAsync(CancellationToken cancellationToken) =>
        probe.CheckAsync(cancellationToken);
}
