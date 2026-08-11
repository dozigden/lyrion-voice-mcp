using LyrionVoiceMcp.Abstractions;

namespace LyrionVoiceMcp.Services;

public sealed class OperationalStatusService : IOperationalStatusService
{
    public OperationalStatus GetStatus() => new("ok");
}

