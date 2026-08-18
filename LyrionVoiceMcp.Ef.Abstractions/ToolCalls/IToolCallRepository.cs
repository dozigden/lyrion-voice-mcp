using LyrionVoiceMcp.Ef.Abstractions.DataAccess;
using LyrionVoiceMcp.Ef.Abstractions.Entities;

namespace LyrionVoiceMcp.Ef.Abstractions.ToolCalls;

public interface IToolCallRepository : IRepositoryBase<EntityToolCall>
{
    Task<EntityToolCallPage> BrowseAsync(EntityToolCallQuery query, CancellationToken cancellationToken);
    Task<EntityToolCall?> GetAsync(string toolCallId, CancellationToken cancellationToken);
    Task<EntityToolCall?> GetForUpdateAsync(string toolCallId, CancellationToken cancellationToken);
    Task<IReadOnlyList<EntityToolCall>> ListRunningForUpdateAsync(CancellationToken cancellationToken);
    Task<int> DeleteOlderThanBatchAsync(
        DateTime cutoffUtc,
        int batchSize,
        CancellationToken cancellationToken);
}

public sealed record EntityToolCallQuery(
    int Offset,
    int Limit,
    string? ToolName,
    EntityToolCallStatus? Status);

public sealed record EntityToolCallSummary(
    string ToolCallId,
    string ToolName,
    EntityToolCallStatus Status,
    DateTime StartedAtUtc,
    DateTime? CompletedAtUtc,
    long? DurationMilliseconds,
    string? TraceIdentifier,
    int? ErrorLogId);

public sealed record EntityToolCallPage(
    IReadOnlyList<EntityToolCallSummary> Items,
    int Total,
    int Offset,
    int Limit);
