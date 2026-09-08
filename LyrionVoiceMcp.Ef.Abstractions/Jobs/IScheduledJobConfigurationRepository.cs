using LyrionVoiceMcp.Ef.Abstractions.DataAccess;
using LyrionVoiceMcp.Ef.Abstractions.Entities;

namespace LyrionVoiceMcp.Ef.Abstractions.Jobs;

public interface IScheduledJobConfigurationRepository
    : IRepositoryBase<EntityScheduledJobConfiguration>
{
    Task<EntityScheduledJobConfiguration?> GetByNameAsync(
        string name,
        CancellationToken cancellationToken);
}
