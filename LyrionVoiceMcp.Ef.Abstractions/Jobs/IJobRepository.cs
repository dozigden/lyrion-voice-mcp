using LyrionVoiceMcp.Ef.Abstractions.DataAccess;
using LyrionVoiceMcp.Ef.Abstractions.Entities;

namespace LyrionVoiceMcp.Ef.Abstractions.Jobs;

public interface IJobRepository : IRepositoryBase<EntityJob>
{
    Task<EntityJobPage> BrowseAsync(EntityJobQuery query, CancellationToken cancellationToken);
    Task<EntityJob?> GetWithLogsAsync(int id, CancellationToken cancellationToken);
    Task<EntityJob?> GetForUpdateAsync(int id, CancellationToken cancellationToken);
    Task<EntityJob?> GetLatestActiveByTypeAsync(string type, CancellationToken cancellationToken);
    Task<EntityJob?> GetLatestByTypeAsync(string type, CancellationToken cancellationToken);
    Task<EntityJob?> GetLatestActiveByCorrelationPrefixesAsync(
        string firstPrefix,
        string secondPrefix,
        CancellationToken cancellationToken);
    Task<EntityJob?> GetLatestStartedByCorrelationPrefixesAsync(
        string firstPrefix,
        string secondPrefix,
        CancellationToken cancellationToken);
    Task<bool> ExistsByCorrelationIdAsync(string correlationId, CancellationToken cancellationToken);
    Task<IReadOnlyList<EntityJob>> ListRunningForUpdateAsync(CancellationToken cancellationToken);
    Task<EntityJob?> FindNextDueAsync(DateTime nowUtc, CancellationToken cancellationToken);
    Task<int> DeleteTerminalBatchBeforeAsync(
        DateTime completedBeforeUtc,
        int excludingJobId,
        int batchSize,
        CancellationToken cancellationToken);
}

public sealed record EntityJobQuery(
    int Offset,
    int Limit,
    string? Type,
    EntityJobStatus? Status);

public sealed record EntityJobSummary(
    int Id,
    string Type,
    EntityJobStatus Status,
    DateTime RunAfterUtc,
    DateTime? StartedAtUtc,
    DateTime? CompletedAtUtc,
    string? CorrelationId,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);

public sealed record EntityJobPage(
    IReadOnlyList<EntityJobSummary> Items,
    int Total,
    int Offset,
    int Limit);
