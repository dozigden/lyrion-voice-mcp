using System.Text.Json;
using LyrionVoiceMcp.Abstractions;
using LyrionVoiceMcp.Ef.Abstractions.DataAccess;
using LyrionVoiceMcp.Ef.Abstractions.Entities;
using LyrionVoiceMcp.Ef.Abstractions.Jobs;

namespace LyrionVoiceMcp.Services;

public sealed class JobRunner(
    IDbContextScopeFactory scopeFactory,
    IJobRepository jobRepository,
    IJobLogRepository jobLogRepository,
    IEnumerable<IJobHandler> handlers,
    IJobCancellationRegistry cancellationRegistry,
    IJobLifecycleGate lifecycleGate,
    IErrorLogService errorLogService,
    TimeProvider timeProvider) : IJobRunner
{
    private readonly IReadOnlyDictionary<string, IJobHandler> handlersByType = handlers
        .GroupBy(handler => handler.Type, StringComparer.Ordinal)
        .ToDictionary(group => group.Key, group => group.Single(), StringComparer.Ordinal);

    public Task MarkRunningJobsFailedAsync(CancellationToken cancellationToken) =>
        lifecycleGate.ExecuteAsync(async token =>
        {
            using var scope = scopeFactory.Create();
            var running = await jobRepository.ListRunningForUpdateAsync(token);
            if (running.Count == 0)
            {
                return;
            }

            const string message = "Job was interrupted by server startup.";
            var nowUtc = OperationalEntityMapper.ToUtcDateTime(timeProvider.GetUtcNow());
            foreach (var job in running)
            {
                job.Status = EntityJobStatus.Failed;
                job.ErrorMessage = message;
                job.ResultJson = JsonSerializer.Serialize(new { errorMessage = message });
                job.CompletedAtUtc = nowUtc;
                AddLog(
                    job.Id,
                    EntityJobLogLevel.Error,
                    "Job interrupted by server startup.",
                    null,
                    nowUtc);
            }

            await scope.SaveChangesAsync(token);
        }, cancellationToken);

    public async Task<bool> RunNextDueAsync(CancellationToken cancellationToken)
    {
        Exception? registrationException = null;
        EntityJob? failedRegistrationJob = null;
        var startedJob = await lifecycleGate.ExecuteAsync(async token =>
        {
            using var scope = scopeFactory.Create();
            var nowUtc = OperationalEntityMapper.ToUtcDateTime(timeProvider.GetUtcNow());
            var job = await jobRepository.FindNextDueAsync(nowUtc, token);
            if (job is null)
            {
                return null;
            }

            CancellationToken jobCancellationToken;
            try
            {
                jobCancellationToken = cancellationRegistry.Register(job.Id, cancellationToken);
            }
            catch (Exception exception)
            {
                registrationException = exception;
                failedRegistrationJob = job;
                job.Status = EntityJobStatus.Failed;
                job.ErrorMessage = "The job cancellation scope could not be registered.";
                job.ResultJson = "{}";
                job.CompletedAtUtc = nowUtc;
                AddLog(job.Id, EntityJobLogLevel.Error, "Job failed.", JsonSerializer.Serialize(new
                {
                    errorMessage = job.ErrorMessage
                }), nowUtc);
                await scope.SaveChangesAsync(token);
                return null;
            }

            try
            {
                job.Status = EntityJobStatus.Running;
                job.StartedAtUtc ??= nowUtc;
                job.ErrorMessage = null;
                AddLog(job.Id, EntityJobLogLevel.Information, "Job started.", null, nowUtc);
                await scope.SaveChangesAsync(token);
                return new StartedJob(
                    new JobContext(job.Id, job.Type, job.PayloadJson),
                    jobCancellationToken);
            }
            catch
            {
                cancellationRegistry.Unregister(job.Id);
                throw;
            }
        }, cancellationToken);

        if (registrationException is not null)
        {
            await errorLogService.LogExceptionAsync(
                registrationException,
                new ErrorLogContext(
                    ErrorLogSources.Backend,
                    ErrorLogAreas.JobRunner,
                    JobId: failedRegistrationJob?.Id,
                    ContextJson: JsonSerializer.Serialize(new
                    {
                        jobId = failedRegistrationJob?.Id,
                        type = failedRegistrationJob?.Type
                    })),
                cancellationToken);
            return true;
        }

        if (startedJob is null)
        {
            return false;
        }

        var context = startedJob.Context;
        try
        {
            if (!handlersByType.TryGetValue(context.Type, out var handler))
            {
                await MarkFailedAsync(
                    context.JobId,
                    $"No job handler is registered for type '{context.Type}'.",
                    "{}",
                    cancellationToken);
                return true;
            }

            JobHandlerResult result;
            try
            {
                result = await handler.HandleAsync(context, startedJob.CancellationToken);
            }
            catch (OperationCanceledException) when (
                cancellationRegistry.IsCancellationRequested(context.JobId)
                && !cancellationToken.IsCancellationRequested)
            {
                await MarkCancelledAsync(context.JobId, EntityJobStatus.Running, cancellationToken);
                return true;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                await errorLogService.LogExceptionAsync(
                    exception,
                    new ErrorLogContext(
                        ErrorLogSources.Backend,
                        ErrorLogAreas.JobRunner,
                        JobId: context.JobId,
                        ContextJson: JsonSerializer.Serialize(new { context.JobId, context.Type })),
                    cancellationToken);
                result = JobHandlerResult.Failed(exception.Message);
            }

            if (!result.ShouldFinalise)
            {
                if (cancellationRegistry.IsCancellationRequested(context.JobId))
                {
                    await MarkCancelledAsync(context.JobId, EntityJobStatus.Running, cancellationToken);
                    return true;
                }

                await RequeueAsync(
                    context.JobId,
                    NormaliseJson(result.ResultJson),
                    result.RunAfter ?? timeProvider.GetUtcNow(),
                    cancellationToken);
                return true;
            }

            if (result.Success)
            {
                await FinaliseAsync(
                    context.JobId,
                    EntityJobStatus.Completed,
                    NormaliseJson(result.ResultJson),
                    null,
                    EntityJobLogLevel.Information,
                    "Job completed.",
                    cancellationToken);
                return true;
            }

            await MarkFailedAsync(
                context.JobId,
                result.ErrorMessage ?? "Job failed.",
                result.ResultJson,
                cancellationToken);
            return true;
        }
        finally
        {
            cancellationRegistry.Unregister(context.JobId);
        }
    }

    private Task MarkFailedAsync(
        long jobId,
        string errorMessage,
        string resultJson,
        CancellationToken cancellationToken) => FinaliseAsync(
            jobId,
            EntityJobStatus.Failed,
            NormaliseJson(resultJson),
            errorMessage,
            EntityJobLogLevel.Error,
            "Job failed.",
            cancellationToken);

    private Task MarkCancelledAsync(
        long jobId,
        EntityJobStatus expectedStatus,
        CancellationToken cancellationToken) =>
        lifecycleGate.ExecuteAsync(async token =>
        {
            if (!OperationalEntityMapper.TryGetEntityId(jobId, out var id))
            {
                return;
            }

            using var scope = scopeFactory.Create();
            var job = await jobRepository.GetForUpdateAsync(id, token);
            if (job is null || job.Status != expectedStatus)
            {
                return;
            }

            var nowUtc = OperationalEntityMapper.ToUtcDateTime(timeProvider.GetUtcNow());
            job.Status = EntityJobStatus.Cancelled;
            job.ResultJson = JsonSerializer.Serialize(new { message = "Job cancelled." });
            job.ErrorMessage = null;
            job.CompletedAtUtc = nowUtc;
            AddLog(job.Id, EntityJobLogLevel.Warning, "Job cancelled.", job.ResultJson, nowUtc);
            await scope.SaveChangesAsync(token);
        }, cancellationToken);

    private Task FinaliseAsync(
        long jobId,
        EntityJobStatus status,
        string resultJson,
        string? errorMessage,
        EntityJobLogLevel logLevel,
        string logMessage,
        CancellationToken cancellationToken) =>
        lifecycleGate.ExecuteAsync(async token =>
        {
            if (!OperationalEntityMapper.TryGetEntityId(jobId, out var id))
            {
                return;
            }

            using var scope = scopeFactory.Create();
            var job = await jobRepository.GetForUpdateAsync(id, token);
            if (job is null || job.Status != EntityJobStatus.Running)
            {
                return;
            }

            var nowUtc = OperationalEntityMapper.ToUtcDateTime(timeProvider.GetUtcNow());
            job.Status = status;
            job.ResultJson = resultJson;
            job.ErrorMessage = errorMessage;
            job.CompletedAtUtc = nowUtc;
            AddLog(
                job.Id,
                logLevel,
                logMessage,
                errorMessage is null
                    ? resultJson
                    : JsonSerializer.Serialize(new { errorMessage }),
                nowUtc);
            await scope.SaveChangesAsync(token);
        }, cancellationToken);

    private Task RequeueAsync(
        long jobId,
        string resultJson,
        DateTimeOffset runAfter,
        CancellationToken cancellationToken) =>
        lifecycleGate.ExecuteAsync(async token =>
        {
            if (!OperationalEntityMapper.TryGetEntityId(jobId, out var id))
            {
                return;
            }

            using var scope = scopeFactory.Create();
            var job = await jobRepository.GetForUpdateAsync(id, token);
            if (job is null || job.Status != EntityJobStatus.Running)
            {
                return;
            }

            var nowUtc = OperationalEntityMapper.ToUtcDateTime(timeProvider.GetUtcNow());
            job.Status = EntityJobStatus.Pending;
            job.ResultJson = resultJson;
            job.RunAfterUtc = OperationalEntityMapper.ToUtcDateTime(runAfter);
            job.ErrorMessage = null;
            AddLog(job.Id, EntityJobLogLevel.Information, "Job requeued.", resultJson, nowUtc);
            await scope.SaveChangesAsync(token);
        }, cancellationToken);

    private void AddLog(
        int jobId,
        EntityJobLogLevel level,
        string message,
        string? dataJson,
        DateTime loggedAtUtc) => jobLogRepository.Add(new EntityJobLog
    {
        JobId = jobId,
        Level = level,
        Message = message,
        DataJson = dataJson,
        LoggedAtUtc = loggedAtUtc
    });

    private static string NormaliseJson(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "{}";
        }

        try
        {
            using var _ = JsonDocument.Parse(value);
            return value.Trim();
        }
        catch (JsonException)
        {
            return JsonSerializer.Serialize(new { message = value });
        }
    }

    private sealed record StartedJob(JobContext Context, CancellationToken CancellationToken);
}
