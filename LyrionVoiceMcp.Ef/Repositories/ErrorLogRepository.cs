using LyrionVoiceMcp.Ef.Abstractions.DataAccess;
using LyrionVoiceMcp.Ef.Abstractions.Entities;
using LyrionVoiceMcp.Ef.Abstractions.ErrorLogs;
using Microsoft.EntityFrameworkCore;

namespace LyrionVoiceMcp.Ef.Repositories;

public sealed class ErrorLogRepository(IAmbientDbContextLocator ambientDbContextLocator)
    : RepositoryBase<EntityErrorLog>(ambientDbContextLocator), IErrorLogRepository
{
    public Task<bool> ReportExistsAsync(Guid reportId, CancellationToken cancellationToken) =>
        Query().AsNoTracking().AnyAsync(item => item.ReportId == reportId, cancellationToken);

    public async Task<EntityErrorLogPage> BrowseAsync(
        EntityErrorLogQuery query,
        CancellationToken cancellationToken)
    {
        var errors = Query().AsNoTracking();
        if (!string.IsNullOrWhiteSpace(query.Source))
        {
            var source = query.Source.Trim();
            errors = errors.Where(item => item.Source == source);
        }

        if (!string.IsNullOrWhiteSpace(query.Area))
        {
            var area = query.Area.Trim();
            errors = errors.Where(item => item.Area == area);
        }

        var total = await errors.CountAsync(cancellationToken);
        var items = await errors
            .OrderByDescending(item => item.OccurredAtUtc)
            .ThenByDescending(item => item.Id)
            .Skip(query.Offset)
            .Take(query.Limit)
            .Select(item => new EntityErrorLogSummary(
                item.Id,
                item.OccurredAtUtc,
                item.Source,
                item.Area,
                item.ExceptionType,
                item.Message,
                item.TraceIdentifier,
                item.JobId))
            .ToArrayAsync(cancellationToken);
        return new EntityErrorLogPage(items, total, query.Offset, query.Limit);
    }

    public Task<EntityErrorLog?> GetAsync(int id, CancellationToken cancellationToken) =>
        Query().AsNoTracking().SingleOrDefaultAsync(item => item.Id == id, cancellationToken);

    public async Task<int> DeleteOlderThanBatchAsync(
        DateTime cutoffUtc,
        int batchSize,
        CancellationToken cancellationToken)
    {
        var ids = await Query()
            .AsNoTracking()
            .Where(item => item.OccurredAtUtc < cutoffUtc)
            .OrderBy(item => item.OccurredAtUtc)
            .ThenBy(item => item.Id)
            .Select(item => item.Id)
            .Take(batchSize)
            .ToArrayAsync(cancellationToken);
        return ids.Length == 0
            ? 0
            : await Query().Where(item => ids.Contains(item.Id)).ExecuteDeleteAsync(cancellationToken);
    }
}
