using LyrionVoiceMcp.Ef.Abstractions.Entities;

namespace LyrionVoiceMcp.Ef.Abstractions.Jobs;

public interface IJobLogRepository
{
    void Add(EntityJobLog log);
}
