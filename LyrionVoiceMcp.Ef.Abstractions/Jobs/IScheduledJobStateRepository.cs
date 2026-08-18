using LyrionVoiceMcp.Ef.Abstractions.DataAccess;
using LyrionVoiceMcp.Ef.Abstractions.Entities;

namespace LyrionVoiceMcp.Ef.Abstractions.Jobs;

public interface IScheduledJobStateRepository : IRepositoryBase<EntityScheduledJobState>
{
    Task<EntityScheduledJobState?> GetByNameAsync(
        string name,
        CancellationToken cancellationToken);
}
