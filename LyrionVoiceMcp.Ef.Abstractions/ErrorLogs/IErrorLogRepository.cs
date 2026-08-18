using LyrionVoiceMcp.Ef.Abstractions.DataAccess;
using LyrionVoiceMcp.Ef.Abstractions.Entities;

namespace LyrionVoiceMcp.Ef.Abstractions.ErrorLogs;

public interface IErrorLogRepository : IRepositoryBase<EntityErrorLog>
{
    Task<bool> ReportExistsAsync(Guid reportId, CancellationToken cancellationToken);
    Task<EntityErrorLogPage> BrowseAsync(EntityErrorLogQuery query, CancellationToken cancellationToken);
    Task<EntityErrorLog?> GetAsync(int id, CancellationToken cancellationToken);
    Task<int> DeleteOlderThanBatchAsync(
        DateTime cutoffUtc,
        int batchSize,
        CancellationToken cancellationToken);
}

public sealed record EntityErrorLogQuery(
    int Offset,
    int Limit,
    string? Source,
    string? Area);

public sealed record EntityErrorLogSummary(
    int Id,
    DateTime OccurredAtUtc,
    string Source,
    string Area,
    string ExceptionType,
    string Message,
    string? TraceIdentifier,
    int? JobId);

public sealed record EntityErrorLogPage(
    IReadOnlyList<EntityErrorLogSummary> Items,
    int Total,
    int Offset,
    int Limit);
