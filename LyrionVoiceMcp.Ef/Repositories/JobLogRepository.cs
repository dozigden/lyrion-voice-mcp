using LyrionVoiceMcp.Ef.Abstractions.DataAccess;
using LyrionVoiceMcp.Ef.Abstractions.Entities;
using LyrionVoiceMcp.Ef.Abstractions.Jobs;

namespace LyrionVoiceMcp.Ef.Repositories;

public sealed class JobLogRepository(IAmbientDbContextLocator ambientDbContextLocator)
    : RepositoryBase<EntityJobLog>(ambientDbContextLocator), IJobLogRepository;
