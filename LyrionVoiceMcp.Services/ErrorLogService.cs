using System.Data;
using LyrionVoiceMcp.Abstractions;
using LyrionVoiceMcp.Ef.Abstractions.DataAccess;
using LyrionVoiceMcp.Ef.Abstractions.Entities;
using LyrionVoiceMcp.Ef.Abstractions.ErrorLogs;
using Microsoft.Extensions.Logging;

namespace LyrionVoiceMcp.Services;

public sealed class ErrorLogService(
    IDbContextScopeFactory scopeFactory,
    IErrorLogRepository repository,
    OperationalPolicy policy,
    TimeProvider timeProvider,
    ILogger<ErrorLogService> logger) : IErrorLogService
{
    private const int DeleteBatchSize = 200;
    private const int SourceMaximumLength = 32;
    private const int AreaMaximumLength = 64;
    private const int ExceptionTypeMaximumLength = 512;
    private const int MessageMaximumLength = 2048;
    private const int StackTraceMaximumLength = 32768;
    private const int TraceIdentifierMaximumLength = 128;
    private const int RequestMethodMaximumLength = 16;
    private const int RequestPathMaximumLength = 2048;
    private const int ContextJsonMaximumLength = 32768;

    public int RetentionDays => policy.ErrorRetentionDays;

    public async Task<ErrorLogPage> BrowseAsync(
        ErrorLogQuery query,
        CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateReadOnly();
        var page = await repository.BrowseAsync(
            new EntityErrorLogQuery(
                query.Offset,
                query.Limit,
                query.Source,
                query.Area),
            cancellationToken);
        return new ErrorLogPage(
            page.Items.Select(OperationalEntityMapper.ToModel).ToArray(),
            page.Total,
            page.Offset,
            page.Limit);
    }

    public async Task<ErrorLog?> GetAsync(long id, CancellationToken cancellationToken)
    {
        if (!OperationalEntityMapper.TryGetEntityId(id, out var entityId))
        {
            return null;
        }

        using var scope = scopeFactory.CreateReadOnly();
        var entity = await repository.GetAsync(entityId, cancellationToken);
        return entity is null ? null : OperationalEntityMapper.ToModel(entity);
    }

    public async Task<long?> LogExceptionAsync(
        Exception exception,
        ErrorLogContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(exception);
        ArgumentNullException.ThrowIfNull(context);

        try
        {
            using var suppression = scopeFactory.SuppressAmbientContext();
            using var scope = scopeFactory.Create(DbContextScopeOption.ForceCreateNew);
            if (context.ReportId is { } reportId
                && await repository.ReportExistsAsync(reportId, cancellationToken))
            {
                return null;
            }

            var entity = new EntityErrorLog
            {
                ReportId = context.ReportId,
                OccurredAtUtc = OperationalEntityMapper.ToUtcDateTime(timeProvider.GetUtcNow()),
                Source = TruncateRequired(context.Source.Trim(), SourceMaximumLength),
                Area = TruncateRequired(context.Area.Trim(), AreaMaximumLength),
                ExceptionType = TruncateRequired(
                    exception.GetType().FullName ?? exception.GetType().Name,
                    ExceptionTypeMaximumLength),
                Message = TruncateRequired(exception.Message, MessageMaximumLength),
                StackTrace = Truncate(exception.ToString(), StackTraceMaximumLength),
                TraceIdentifier = Truncate(context.TraceIdentifier, TraceIdentifierMaximumLength),
                RequestMethod = Truncate(context.RequestMethod, RequestMethodMaximumLength),
                RequestPath = Truncate(context.RequestPath, RequestPathMaximumLength),
                JobId = OperationalEntityMapper.TryGetEntityId(context.JobId ?? 0, out var jobId)
                    ? jobId
                    : null,
                ContextJson = Truncate(context.ContextJson, ContextJsonMaximumLength)
            };
            repository.Add(entity);
            await scope.SaveChangesAsync(cancellationToken);
            return entity.Id;
        }
        catch (PersistenceConflictException) when (context.ReportId is not null)
        {
            return null;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return null;
        }
        catch (Exception logException)
        {
            logger.LogError(logException, "Failed to persist exception details to the error log.");
            return null;
        }
    }

    public async Task<int> PurgeOlderThanAsync(
        DateTimeOffset cutoff,
        CancellationToken cancellationToken)
    {
        var total = 0;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var scope = scopeFactory.CreateWithTransaction(IsolationLevel.Serializable);
            var deleted = await repository.DeleteOlderThanBatchAsync(
                OperationalEntityMapper.ToUtcDateTime(cutoff),
                DeleteBatchSize,
                cancellationToken);
            await scope.SaveChangesAsync(cancellationToken);
            total += deleted;
            if (deleted < DeleteBatchSize)
            {
                return total;
            }
        }
    }

    private static string TruncateRequired(string value, int maximumLength) =>
        value.Length <= maximumLength ? value : value[..maximumLength];

    private static string? Truncate(string? value, int maximumLength) =>
        value is null || value.Length <= maximumLength ? value : value[..maximumLength];
}
