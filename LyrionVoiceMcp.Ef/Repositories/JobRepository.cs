using LyrionVoiceMcp.Ef.Abstractions.DataAccess;
using LyrionVoiceMcp.Ef.Abstractions.Entities;
using LyrionVoiceMcp.Ef.Abstractions.Jobs;
using Microsoft.EntityFrameworkCore;

namespace LyrionVoiceMcp.Ef.Repositories;

public sealed class JobRepository(IAmbientDbContextLocator ambientDbContextLocator)
    : RepositoryBase<EntityJob>(ambientDbContextLocator), IJobRepository
{
    public async Task<EntityJobPage> BrowseAsync(
        EntityJobQuery query,
        CancellationToken cancellationToken)
    {
        var jobs = Query().AsNoTracking();
        if (!string.IsNullOrWhiteSpace(query.Type))
        {
            var type = query.Type.Trim();
            jobs = jobs.Where(item => item.Type == type);
        }

        if (query.Status is { } status)
        {
            jobs = jobs.Where(item => item.Status == status);
        }

        var total = await jobs.CountAsync(cancellationToken);
        var items = await jobs
            .OrderByDescending(item => item.CreatedAtUtc)
            .ThenByDescending(item => item.Id)
            .Skip(query.Offset)
            .Take(query.Limit)
            .Select(item => new EntityJobSummary(
                item.Id,
                item.Type,
                item.Status,
                item.RunAfterUtc,
                item.StartedAtUtc,
                item.CompletedAtUtc,
                item.CorrelationId,
                item.CreatedAtUtc,
                item.UpdatedAtUtc))
            .ToArrayAsync(cancellationToken);
        return new EntityJobPage(items, total, query.Offset, query.Limit);
    }

    public Task<EntityJob?> GetWithLogsAsync(int id, CancellationToken cancellationToken) =>
        Query()
            .AsNoTracking()
            .Include(item => item.Logs.OrderBy(log => log.Id))
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);

    public Task<EntityJob?> GetForUpdateAsync(int id, CancellationToken cancellationToken) =>
        Query().SingleOrDefaultAsync(item => item.Id == id, cancellationToken);

    public Task<EntityJob?> GetLatestActiveByTypeAsync(
        string type,
        CancellationToken cancellationToken) => Query()
        .AsNoTracking()
        .Where(item => item.Type == type
            && (item.Status == EntityJobStatus.Pending
                || item.Status == EntityJobStatus.Running))
        .OrderByDescending(item => item.CreatedAtUtc)
        .ThenByDescending(item => item.Id)
        .FirstOrDefaultAsync(cancellationToken);

    public Task<EntityJob?> GetLatestByTypeAsync(
        string type,
        CancellationToken cancellationToken) => Query()
        .AsNoTracking()
        .Where(item => item.Type == type)
        .OrderByDescending(item => item.CreatedAtUtc)
        .ThenByDescending(item => item.Id)
        .FirstOrDefaultAsync(cancellationToken);

    public Task<EntityJob?> GetLatestActiveByCorrelationPrefixesAsync(
        string firstPrefix,
        string secondPrefix,
        CancellationToken cancellationToken) => Query()
        .AsNoTracking()
        .Where(item => (item.Status == EntityJobStatus.Pending
                || item.Status == EntityJobStatus.Running)
            && item.CorrelationId != null
            && (item.CorrelationId.StartsWith(firstPrefix)
                || item.CorrelationId.StartsWith(secondPrefix)))
        .OrderByDescending(item => item.CreatedAtUtc)
        .ThenByDescending(item => item.Id)
        .FirstOrDefaultAsync(cancellationToken);

    public Task<EntityJob?> GetLatestStartedByCorrelationPrefixesAsync(
        string firstPrefix,
        string secondPrefix,
        CancellationToken cancellationToken) => Query()
        .AsNoTracking()
        .Where(item => item.StartedAtUtc != null
            && item.CorrelationId != null
            && (item.CorrelationId.StartsWith(firstPrefix)
                || item.CorrelationId.StartsWith(secondPrefix)))
        .OrderByDescending(item => item.StartedAtUtc)
        .ThenByDescending(item => item.Id)
        .FirstOrDefaultAsync(cancellationToken);

    public Task<bool> ExistsByCorrelationIdAsync(
        string correlationId,
        CancellationToken cancellationToken) => Query()
        .AsNoTracking()
        .AnyAsync(item => item.CorrelationId == correlationId, cancellationToken);

    public async Task<IReadOnlyList<EntityJob>> ListRunningForUpdateAsync(
        CancellationToken cancellationToken) => await Query()
        .Where(item => item.Status == EntityJobStatus.Running)
        .OrderBy(item => item.StartedAtUtc)
        .ThenBy(item => item.Id)
        .ToArrayAsync(cancellationToken);

    public Task<EntityJob?> FindNextDueAsync(
        DateTime nowUtc,
        CancellationToken cancellationToken) => Query()
        .Where(item => item.Status == EntityJobStatus.Pending && item.RunAfterUtc <= nowUtc)
        .OrderBy(item => item.RunAfterUtc)
        .ThenBy(item => item.Id)
        .FirstOrDefaultAsync(cancellationToken);

    public async Task<int> DeleteTerminalBatchBeforeAsync(
        DateTime completedBeforeUtc,
        int excludingJobId,
        int batchSize,
        CancellationToken cancellationToken)
    {
        var ids = await Query()
            .AsNoTracking()
            .Where(item => item.Id != excludingJobId
                && item.CompletedAtUtc < completedBeforeUtc
                && (item.Status == EntityJobStatus.Completed
                    || item.Status == EntityJobStatus.Failed
                    || item.Status == EntityJobStatus.Cancelled))
            .OrderBy(item => item.CompletedAtUtc)
            .ThenBy(item => item.Id)
            .Select(item => item.Id)
            .Take(batchSize)
            .ToArrayAsync(cancellationToken);
        return ids.Length == 0
            ? 0
            : await Query().Where(item => ids.Contains(item.Id)).ExecuteDeleteAsync(cancellationToken);
    }
}
