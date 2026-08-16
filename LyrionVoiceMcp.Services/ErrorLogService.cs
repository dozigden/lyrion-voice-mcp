using LyrionVoiceMcp.Abstractions;
using Microsoft.Extensions.Logging;

namespace LyrionVoiceMcp.Services;

public sealed class ErrorLogService(
    IErrorLogStore store,
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

    public Task<ErrorLogPage> BrowseAsync(
        ErrorLogQuery query,
        CancellationToken cancellationToken) =>
        store.BrowseAsync(query, cancellationToken);

    public Task<ErrorLog?> GetAsync(long id, CancellationToken cancellationToken) =>
        store.GetErrorLogAsync(id, cancellationToken);

    public async Task<long?> LogExceptionAsync(
        Exception exception,
        ErrorLogContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(exception);
        ArgumentNullException.ThrowIfNull(context);

        try
        {
            if (context.ReportId is { } reportId
                && await store.ReportExistsAsync(reportId, cancellationToken))
            {
                return null;
            }

            return await store.AddAsync(new ErrorLogEntry(
                context.ReportId,
                timeProvider.GetUtcNow(),
                TruncateRequired(context.Source.Trim(), SourceMaximumLength),
                TruncateRequired(context.Area.Trim(), AreaMaximumLength),
                TruncateRequired(exception.GetType().FullName ?? exception.GetType().Name, ExceptionTypeMaximumLength),
                TruncateRequired(exception.Message, MessageMaximumLength),
                Truncate(exception.ToString(), StackTraceMaximumLength),
                Truncate(context.TraceIdentifier, TraceIdentifierMaximumLength),
                Truncate(context.RequestMethod, RequestMethodMaximumLength),
                Truncate(context.RequestPath, RequestPathMaximumLength),
                context.JobId,
                Truncate(context.ContextJson, ContextJsonMaximumLength)), cancellationToken);
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
            var deleted = await store.DeleteOlderThanAsync(
                cutoff,
                DeleteBatchSize,
                cancellationToken);
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
