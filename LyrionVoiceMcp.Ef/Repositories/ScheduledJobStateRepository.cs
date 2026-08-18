using LyrionVoiceMcp.Ef.Abstractions.DataAccess;
using LyrionVoiceMcp.Ef.Abstractions.Entities;
using LyrionVoiceMcp.Ef.Abstractions.Jobs;
using Microsoft.EntityFrameworkCore;

namespace LyrionVoiceMcp.Ef.Repositories;

public sealed class ScheduledJobStateRepository(IAmbientDbContextLocator ambientDbContextLocator)
    : RepositoryBase<EntityScheduledJobState>(ambientDbContextLocator),
        IScheduledJobStateRepository
{
    public Task<EntityScheduledJobState?> GetByNameAsync(
        string name,
        CancellationToken cancellationToken) => Query()
        .SingleOrDefaultAsync(item => item.Name == name, cancellationToken);
}
