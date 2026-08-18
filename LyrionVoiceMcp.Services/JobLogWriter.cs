using System.Text.Json;
using LyrionVoiceMcp.Abstractions;
using LyrionVoiceMcp.Ef.Abstractions.DataAccess;
using LyrionVoiceMcp.Ef.Abstractions.Entities;
using LyrionVoiceMcp.Ef.Abstractions.Jobs;

namespace LyrionVoiceMcp.Services;

public sealed class JobLogWriter(
    IDbContextScopeFactory scopeFactory,
    IJobLogRepository repository,
    TimeProvider timeProvider) : IJobLogWriter
{
    public async Task WriteAsync(
        long jobId,
        JobLogLevel level,
        string message,
        object? data,
        CancellationToken cancellationToken)
    {
        if (!OperationalEntityMapper.TryGetEntityId(jobId, out var id))
        {
            return;
        }

        using var suppression = scopeFactory.SuppressAmbientContext();
        using var scope = scopeFactory.Create(DbContextScopeOption.ForceCreateNew);
        repository.Add(new EntityJobLog
        {
            JobId = id,
            Level = OperationalEntityMapper.ToEntity(level),
            Message = message,
            DataJson = data is null ? null : JsonSerializer.Serialize(data),
            LoggedAtUtc = OperationalEntityMapper.ToUtcDateTime(timeProvider.GetUtcNow())
        });
        await scope.SaveChangesAsync(cancellationToken);
    }
}
