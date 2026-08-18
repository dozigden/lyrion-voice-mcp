using LyrionVoiceMcp.Ef.Abstractions.DataAccess;
using LyrionVoiceMcp.Ef.Abstractions.Entities;
using LyrionVoiceMcp.Ef.Abstractions.ToolCalls;
using Microsoft.EntityFrameworkCore;

namespace LyrionVoiceMcp.Ef.Repositories;

public sealed class ToolCallRepository(IAmbientDbContextLocator ambientDbContextLocator)
    : RepositoryBase<EntityToolCall>(ambientDbContextLocator), IToolCallRepository
{
    public async Task<EntityToolCallPage> BrowseAsync(
        EntityToolCallQuery query,
        CancellationToken cancellationToken)
    {
        var calls = Query().AsNoTracking();
        if (!string.IsNullOrWhiteSpace(query.ToolName))
        {
            var toolName = query.ToolName.Trim();
            calls = calls.Where(item => item.ToolName == toolName);
        }

        if (query.Status is { } status)
        {
            calls = calls.Where(item => item.Status == status);
        }

        var total = await calls.CountAsync(cancellationToken);
        var items = await calls
            .OrderByDescending(item => item.StartedAtUtc)
            .ThenByDescending(item => item.Id)
            .Skip(query.Offset)
            .Take(query.Limit)
            .Select(item => new EntityToolCallSummary(
                item.ToolCallId,
                item.ToolName,
                item.Status,
                item.StartedAtUtc,
                item.CompletedAtUtc,
                item.DurationMilliseconds,
                item.TraceIdentifier,
                item.ErrorLogId))
            .ToArrayAsync(cancellationToken);
        return new EntityToolCallPage(items, total, query.Offset, query.Limit);
    }

    public Task<EntityToolCall?> GetAsync(
        string toolCallId,
        CancellationToken cancellationToken) => Query()
        .AsNoTracking()
        .SingleOrDefaultAsync(item => item.ToolCallId == toolCallId, cancellationToken);

    public Task<EntityToolCall?> GetForUpdateAsync(
        string toolCallId,
        CancellationToken cancellationToken) => Query()
        .SingleOrDefaultAsync(item => item.ToolCallId == toolCallId, cancellationToken);

    public async Task<IReadOnlyList<EntityToolCall>> ListRunningForUpdateAsync(
        CancellationToken cancellationToken) => await Query()
        .Where(item => item.Status == EntityToolCallStatus.Running)
        .OrderBy(item => item.StartedAtUtc)
        .ThenBy(item => item.Id)
        .ToArrayAsync(cancellationToken);

    public async Task<int> DeleteOlderThanBatchAsync(
        DateTime cutoffUtc,
        int batchSize,
        CancellationToken cancellationToken)
    {
        var ids = await Query()
            .AsNoTracking()
            .Where(item => item.StartedAtUtc < cutoffUtc)
            .OrderBy(item => item.StartedAtUtc)
            .ThenBy(item => item.Id)
            .Select(item => item.Id)
            .Take(batchSize)
            .ToArrayAsync(cancellationToken);
        return ids.Length == 0
            ? 0
            : await Query().Where(item => ids.Contains(item.Id)).ExecuteDeleteAsync(cancellationToken);
    }
}
