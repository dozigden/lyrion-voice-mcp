using LyrionVoiceMcp.Ef.Abstractions.DataAccess;
using LyrionVoiceMcp.Ef.Abstractions.Entities;
using LyrionVoiceMcp.Ef.Abstractions.Jobs;
using Microsoft.EntityFrameworkCore;

namespace LyrionVoiceMcp.Ef.Repositories;

public sealed class ScheduledJobConfigurationRepository(
    IAmbientDbContextLocator ambientDbContextLocator)
    : RepositoryBase<EntityScheduledJobConfiguration>(ambientDbContextLocator),
        IScheduledJobConfigurationRepository
{
    public Task<EntityScheduledJobConfiguration?> GetByNameAsync(
        string name,
        CancellationToken cancellationToken) => Query()
        .SingleOrDefaultAsync(item => item.Name == name, cancellationToken);
}
